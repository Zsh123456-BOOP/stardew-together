"""Fifth-round combined native checks; existing AgentLab stock, no resource fixtures.
The supplied AgentLab checkpoint is loaded but never saved by this suite.
"""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',required=True);p.add_argument('--remaining',action='store_true');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/U5BuildMods';b=None;checks=[];initial=None

def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
def check(n,ok,data=None):
 checks.append(dict(name=n,passed=bool(ok),data=data));write('checks.json',checks);print(n,ok,flush=True)
 if not ok:raise RuntimeError(n)
write('manifest.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest(),save=a.save,normal_time=True,no_save=True))
with (out/'game.log').open('w') as log:
 proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--mods-dir',str(mods),'--port','18777'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
 try:
  deadline=time.monotonic()+120;commanded=False
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('owned_game_exited')
   try:
    c=json.loads((mods/'AgentBridge/config.json').read_text());local=out/'bridge-private.json';fd=os.open(local,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18777',token=c['Token']),f)
    b=Bridge(local);h=b.request('GET','/health')
    if h['api_connected'] and not commanded:proc.stdin.write('agent_load '+a.save+'\n');proc.stdin.flush();commanded=True
    if h['ready']:break
   except (OSError,ValueError,RuntimeError):pass
   time.sleep(1)
  initial=b.state();check('AgentLab-isolated',initial['player']['name']=='AgentLab')
  def sc(n,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=n,**kw))
  def tool(n,**kw):
   try:r=sc('agent_tool',tool=n,args=kw)
   except BridgeError as e:r=dict(status='failed',error=str(e))
   with (out/'calls.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),tool=n,args=kw,result=r),ensure_ascii=False)+'\n')
   return r
  def wait(r,label,seconds=300):
   until=time.monotonic()+seconds
   while r.get('status')=='running' and time.monotonic()<until:
    time.sleep(.6);r=tool('action.status',id=r['command_id']);write('latest-action.json',r)
   write(label+'.json',r);return r
  def snap():return sc('round4',mode='autonomy7')
  def events():
   return [json.loads(line) for f in (mods/'Together/logs'/str(initial['save_id'])).glob('*/day-*.jsonl') for line in f.read_text().splitlines() if line.endswith('}')]
  def chain(calls,label,seconds=500):
   r=sc('decision_chain',turn=dict(plan='第五轮组合检查，核验原生结果',speech='',calls=calls));write(label+'-submit.json',r)
   until=time.monotonic()+seconds
   while time.monotonic()<until:
    time.sleep(1);d=b.request('GET','/lab/together')['autoplay'];write('latest.json',d)
    if not d.get('continuation') and not any(x['state'] in ('running','queued') for x in d['schedule']['tasks']):break
   write(label+'-state.json',d);return d
  sc('agent_pause');sc('preparation_probe');time.sleep(1)
  if not a.remaining:
   r=wait(tool('player.travel',location='Farm'),'farm');check('native-travel',r.get('status')=='succeeded',r)
   before=snap();write('before.json',before)
   sale=next((r for r in before['opportunities'] if r['Id']=='sell:surplus'),None)
   check('sale-candidate-native-stock-location-and-protection',sale is not None and sale.get('Requirements',{}).get('estimated_total_upper',0)>0,sale)
   r=sc('round4',mode='opportunity_receipt');write('blocked-observation.json',r)
   check('blocked-receipt-has-real-alternatives',len(r['receipt']['alternatives'])>0,r)
   check('pasture-protection-reason-observable',any('牧草' in x for x in r['protection_reasons']),r['protection_reasons'])
   r=tool('player.service',location='Town',service='shop',shop='FishShop');check('service-parameter-mismatch-is-declaration',r.get('error','').startswith('parameter_service_location_mismatch') and 'FishShop' in r['error'],r)
   r=tool('player.place_facility',location='Farm');check('facility-missing-item-is-parameter-error',r.get('error','').startswith('parameter_required:item'),r)
   # A model turn, using the production candidates, must perform daytime shipping.
   # No scripted shipping call is used in this part.
   sc('agent_pause');start_utc=time.time();r=sc('survival_start',goal='组合检查：请先读取经营候选中的变现候选，按其真实库存和受保护数量取货并执行一次player.ship_items。现在仍是白天，显式出货可以立即执行。此次只做这次真实出货，结束后等待我继续，不睡觉、不改资源。');write('model-sale-start.json',r)
   deadline=time.monotonic()+400;native_sale=None
   while time.monotonic()<deadline:
    time.sleep(2);d=b.request('GET','/lab/together')['autoplay'];write('latest.json',d)
    panel=sc('preparation_read')['overlay'];
    if any('保留' in x for x in panel['lines']):write('protection-panel.json',panel)
    ev=events();matches=[e for e in ev if e.get('kind')=='shipment_sequence' and e.get('payload',{}).get('phase')=='native_click']
    if matches:native_sale=matches[-1];break
    if d['state']['Status']!='running':break
   # Wait for the native transaction, then pause without touching any inventory.
   if native_sale:
    for _ in range(60):
     d=b.request('GET','/lab/together')['autoplay']
     if not any(t['state']=='running' for t in d['schedule']['tasks']):break
     time.sleep(.5)
   sc('agent_pause');write('model-sale-events.json',events())
   check('model-executes-native-shipment-before-1700',native_sale is not None and native_sale['time']<1700,native_sale)
   sc('preparation_probe')
   # Exact remaining native state is reused for purchase/plant and task-board checks.
   d=chain([dict(tool='player.service',args=dict(location='SeedShop',service='shop',shop='SeedShop'))],'shop');r=next(t for t in d['state']['Schedule']['Tasks'] if t['spec']['tool']=='player.service');check('native-shop-open',r['state']=='succeeded',r)
   quote=tool('shop.read');write('quote.json',quote)
   r=tool('farm.plan',seed='(O)472',count=1);check('farm-plan-wrong-map-is-declaration',r.get('error','').startswith('plan_on_farm_or_greenhouse'),r)
   r=wait(tool('player.buy',shop='SeedShop',item='(O)472',count=2,max_unit_price=20,budget=40,keep_gold=0),'buy');check('native-seed-purchase',r.get('status')=='succeeded',r)
   tool('menu.close');r=wait(tool('player.travel',location='Farm'),'farm-return');check('native-farm-return',r.get('status')=='succeeded',r)
   planted=snap();write('unplanted.json',planted);check('owned-seed-planting-candidate',any(x['Tool']=='farm.plan' for x in planted['opportunities']),planted['opportunities'])
   plan=tool('farm.plan',seed='(O)472',count=2);write('plot.json',plan);check('native-plot-plan',bool(plan.get('options')),plan)
   d=chain([dict(tool='work.run',args=dict(goal='plant',plan_id=plan['options'][0]['plan_id'],until=2400,reserve_stamina=0))],'plant')
   task=[t for t in d['state']['Schedule']['Tasks'] if t['spec']['tool']=='work.run' and t['spec']['args'].get('goal')=='plant'][-1]
   check('buy-return-plant-chain',task['state']=='succeeded',task)
  d=chain([dict(tool='knowledge.search',args=dict(query='初始种子'))],'array-receipt')
  check('array-observation-does-not-break-adoption-telemetry',d['state']['Status']=='running' and not d.get('continuation'),d['state']['Status'])
  # Create a planning goal only (no resources or native counters written).
  goal=tool('goal.create',request_id='u5-knowledge',entity='craft:Keg',count=1,run=False)
  obs=snap();write('dependency.json',obs)
  check('unresolved-native-goal-has-encyclopedia-step',any(x['Tool']=='knowledge.get' and 'goal=agent-u5-knowledge' in x['Evidence'] for x in obs['opportunities']),goal)
  board=next((x for x in obs['opportunities'] if x['Id']=='open:quest-board'),None)
  check('native-quest-board-has-today-step',board is not None,board)
  r=wait(tool('player.service',location='Town',service='daily_quests'),'board');check('native-board-open',r.get('status')=='succeeded',r)
  obs=snap();write('board-opportunities.json',obs);check('native-board-acceptance-candidate',any(x['Tool']=='player.accept_quest' for x in obs['opportunities']),obs['opportunities'])
  tool('menu.close')
  r=tool('work.run',goal='fiber',location='Farm',count=1,reserve_stamina=0,until=2400)
  until=time.monotonic()+120;panel=None
  while r.get('status')=='running' and time.monotonic()<until:
   view=sc('preparation_read')['overlay']
   if any('保留' in x for x in view['lines']):panel=view;write('protection-panel.json',panel)
   time.sleep(.5);r=tool('action.status',id=r['command_id'])
  write('protection-work.json',r);check('protection-explanation-in-live-panel',panel is not None,panel)
  sc('agent_pause');write('result.json',dict(passed=True,checks=checks))
 except Exception as e:
  write('result.json',dict(passed=False,error=str(e),checks=checks));print(type(e).__name__,str(e),flush=True)
 finally:
  if b:
   try:write('final.json',sc('preparation_read'));sc('agent_pause')
   except Exception:pass
  if proc.poll() is None:
   proc.stdin.write('agent_quit\n');proc.stdin.flush()
   try:proc.wait(timeout=30)
   except subprocess.TimeoutExpired:print('owned_exit_pending',flush=True)
  if initial:
   source=mods/'Together/logs'/str(initial['save_id'])
   if source.exists():shutil.copytree(source,out/'native-logs',dirs_exist_ok=True)
sys.exit(0 if json.loads((out/'result.json').read_text())['passed'] else 1)
