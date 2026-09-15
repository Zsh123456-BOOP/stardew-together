"""Fourth-round native seam suite in one AgentLab process; no resource/progress seeding."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',default='AgentLab_449667006');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/Round4Mods';b=None;checks=[]
def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
def check(n,ok,data=None):
 checks.append(dict(name=n,passed=bool(ok),data=data));write('checks.json',checks);print(n,ok,flush=True)
 if not ok:raise RuntimeError(n)
write('manifest.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest(),save=a.save,normal_time=True))
with (out/'game.log').open('w') as log:
 proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--mods-dir',str(mods),'--port','18775'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
 try:
  deadline=time.monotonic()+120;commanded=False
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('owned_game_exited')
   try:
    c=json.loads((mods/'AgentBridge/config.json').read_text());local=out/'bridge-private.json';fd=os.open(local,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18775',token=c['Token']),f)
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
  def wait(r,label,seconds=400):
   until=time.monotonic()+seconds
   while r.get('status')=='running' and time.monotonic()<until:
    time.sleep(.5);r=tool('action.status',id=r['command_id']);write('latest-action.json',r)
   write(label+'.json',r);return r
  def scene():return sc('round3',mode='read')
  def events():
   paths=(mods/'Together/logs'/str(initial['save_id'])).glob('*/day-*.jsonl')
   return [json.loads(x) for pth in paths for x in pth.read_text().splitlines() if x.endswith('}')]
  sc('agent_pause');sc('preparation_probe')
  def chain(calls,label,seconds=500):
   r=sc('decision_chain',turn=dict(plan='验证动作完成后才执行后续查询',speech='',calls=calls));write(label+'-submit.json',r)
   until=time.monotonic()+seconds
   while time.monotonic()<until:
    time.sleep(1);d=b.request('GET','/lab/together')['autoplay'];write('latest.json',d)
    if not d.get('continuation') and not any(x['state'] in ('running','queued') for x in d['schedule']['tasks']):break
   ev=events();write(label+'-events.json',ev);return r,ev,d
  tool('farm.autonomy',enabled=False,budget_per_day=-1,keep_gold=0,plots=96,max_daily_manual_water=-1)
  before=sc('round4',mode='read');write('before.json',before)
  check('model-can-remove-saved-policy-ceilings',before['investment']['ManualWaterLimit']==-1 and before['investment']['KeepGold']==0 and before['investment']['BudgetPerDay']==-1,before['investment'])
  r=wait(tool('work.run',goal='water',location='Farm',count=0,reserve_stamina=0),'care',300)
  check('zero-extra-stamina-reserve-accepted',r.get('status')=='succeeded',r)
  r=wait(tool('player.travel',location='Town'),'town',240)
  check('walk-to-town',r.get('status')=='succeeded',r)
  until=time.monotonic()+220
  while time.monotonic()<until:
   d=sc('round4',mode='read')
   if d['snapshot']['time']>=900:break
   time.sleep(2)
  r=wait(tool('player.service',location='SeedShop',service='shop',shop='SeedShop'),'shop',240)
  check('native-service',r.get('status')=='succeeded',r)
  quote=tool('shop.read');write('quote.json',quote);dec=quote['seed_decision']
  check('quote-no-business-enable-required',dec['budget']>0 and len(dec['candidates'])>1,dec)
  chosen=[dict(item='(O)472',count=2),dict(item='(O)475',count=1)]
  r=tool('farm.select_seeds',quote_token=dec['quote_token'],items=chosen,reason='对比短周期防风草与土豆，验证已超过24株也能采购播种')
  write('select.json',r);check('select-without-automation-approval',r.get('status')=='selection_approved_not_purchased',r)
  until=time.monotonic()+540
  while time.monotonic()<until:
   d=sc('round4',mode='read');write('latest.json',d)
   if d['investment']['Phase'] in ('done','blocked'):break
   time.sleep(1)
  check('native-two-crop-chain',d['investment']['Phase']=='done' and not d['investment']['Error'],d)
  check('more-than-24-crops',len(d['farm']['farm'])>24 and all(x['water']==1 for x in d['farm']['farm']),d['farm']['farm'])
  check('purchase-ledger-exact',d['ledger']['SpentToday']==90,d['ledger'])
  check('single-purchase-plan-stops',not d['investment']['Enabled'] and not d['investment']['Repeat'],d['investment'])
  discovery=tool('tools.lookup',query='greet NPC');check('synonym-discovery-native','player.social' in discovery.get('definitions',{}),discovery)
  overlay=sc('round4',mode='overlay');write('overlay.json',overlay)
  check('overlay-concise-no-duplicate-sections',not any(x.startswith(('想做','打算','目标')) for x in overlay['lines']) and not any('request_id' in x or 'stock_target' in x for x in overlay['lines']),overlay)
  before=sc('round4',mode='read');r=tool('farm.cleanup',request_id='native-autonomy-patch',scopes=['general','courtyard'],daily_limit=6,reserve_stamina=0,until=2200)
  write('cleanup-order.json',r);check('model-created-cleanup-no-hidden-reserve','order' in r,r)
  until=time.monotonic()+240
  while time.monotonic()<until:
   d=sc('round4',mode='read');write('cleanup-latest.json',d)
   ev=events()
   progress=[e for e in ev if e['kind']=='cleanup_progress' and e['payload'].get('Id')=='native-autonomy-patch']
   if progress and progress[-1]['payload']['CompletedToday']>=6:break
   time.sleep(1)
  check('continuous-mixed-cleanup',bool(progress) and progress[-1]['payload']['CompletedToday']>=6,progress)
  write('cleanup-routes.json',[e for e in ev if e['kind'] in ('cleanup_route','cleanup_patch_switch','labor_reservation')])
  tool('farm.cleanup',request_id='native-autonomy-patch',mode='pause')
  sc('agent_pause');time.sleep(1)
  check('pause-retains-game-window',proc.poll() is None)

  write('result.json',dict(passed=True,checks=checks))
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
  if 'initial' in locals():
   source=mods/'Together/logs'/str(initial['save_id'])
   if source.exists():shutil.copytree(source,out/'native-logs',dirs_exist_ok=True)
sys.exit(0 if json.loads((out/'result.json').read_text())['passed'] else 1)
