"""Third-round native seam suite in one AgentLab process; no resource/progress seeding."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',default='AgentLab_449657228');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/Round3Mods';b=None;checks=[]
def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
def check(n,ok,data=None):
 checks.append(dict(name=n,passed=bool(ok),data=data));write('checks.json',checks);print(n,ok,flush=True)
 if not ok:raise RuntimeError(n)
write('manifest.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest(),save=a.save,normal_time=True))
with (out/'game.log').open('w') as log:
 proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--mods-dir',str(mods),'--port','18774'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
 try:
  deadline=time.monotonic()+120;commanded=False
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('owned_game_exited')
   try:
    c=json.loads((mods/'AgentBridge/config.json').read_text());local=out/'bridge-private.json';fd=os.open(local,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18774',token=c['Token']),f)
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
  sc('agent_pause');sc('preparation_probe');write('initial.json',scene())
  packed=sc('round3',mode='pack');write('packed.json',packed);check('native-pack-conservation',packed['conserved'])
  before=tool('inventory.read');r=wait(tool('player.craft',recipe='Chest',count=1),'craft-rejected')
  check('S1-executor-capacity-reject-closes-owned-menu',r.get('error')=='capacity_no_free_slot' and tool('menu.read')['type']=='none',r)
  menu=tool('menu.open',page='crafting');write('craft-menu.json',menu)
  locked=[c for c in menu['choices'] if (c.get('Facts') or {}).get('locked')]
  recipe=next(c for c in menu['choices'] if (c.get('Facts') or {}).get('recipe')=='Chest')
  check('S1-locked-and-structured-ingredients',bool(locked) and all(c['Facts'].get('item') is None and not c['Facts']['available'] for c in locked) and recipe['Facts']['ingredients_available'] is True)
  r=tool('menu.choose',token=menu['token'],id=recipe['Id']);write('menu-craft-rejected.json',r)
  check('S1-no-capacity-bypass',r.get('error')=='capacity_no_free_slot' and before==tool('inventory.read'),r)
  tool('menu.close');write('unpacked.json',sc('round3',mode='unpack'))
  # Same automatic prerequisite as a normal start; only paid-model requests
  # remain suppressed by the probe. Existing AutoExpand authorization is used.
  sc('round3',mode='bootstrap');until=time.monotonic()+180
  while time.monotonic()<until:
   d=scene();write('storage-latest.json',d)
   if d['stores']:break
   time.sleep(1)
  builds=[e for e in events() if e['kind']=='goal_facility_verified']
  check('S3-storage-before-full',d['stores'] and builds and builds[-1]['payload']['free_slots']>0,builds[-1] if builds else d)
  # Run real care before optional nearby cleanup, in a single outing.
  tool('day.routine',enabled=True,assignments={'water':'player'})
  tool('farm.cleanup',request_id='round3-local',scopes=['general'],daily_limit=2,until=1800,reserve_stamina=20)
  until=time.monotonic()+260
  while time.monotonic()<until:
   d=scene();write('care-latest.json',d)
   routes=[e for e in events() if e['kind']=='cleanup_route']
   diag=sc('preparation_read')['autoplay']
   if routes and all(x['water']==1 for x in d['farm']) and not diag['snapshot']['player_action'].get('status')=='running':break
   time.sleep(1)
  ev=events();water=[e for e in ev if e['kind']=='task_started' and e['payload'].get('source')=='daily_care'];routes=[e for e in ev if e['kind']=='cleanup_route']
  first=routes[0]['payload'] if routes else None
  walks=[s['Walk'] for s in first['route']] if first else []
  check('S5-care-before-local-cleanup',water and routes and water[0]['utc']<routes[0]['utc'] and walks and max(walks)<24,dict(water=water[:1],first_cleanup=first))
  # Enable from the observed candidate, not a prompt or forced cash mutation.
  d=scene();candidate=next((c for c in d['candidates'] if c['Id']=='approve:business'),None)
  check('S4-approved-policy-candidate',candidate is not None,candidate)
  cash=d['snapshot']['money'];r=tool(candidate['Tool'],**candidate['Args']);write('policy-enabled.json',r)
  until=time.monotonic()+600;purchase=[]
  while time.monotonic()<until:
   d=scene();write('business-latest.json',d)
   ev=events();purchase=[e for e in ev if e['kind']=='action_result' and any(x.get('kind')=='native_purchase' for x in e['payload'].get('effects',[]))]
   with (out/'business-samples.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),data=d),ensure_ascii=False)+'\n')
   diag=sc('preparation_read')['autoplay'];active=diag['snapshot'].get('player_action') or {}
   if purchase and not active.get('status')=='running' and not diag['snapshot']['menu'] and not list(diag['schedule']['tasks']):break
   if d['snapshot']['time']>=2100:break
   time.sleep(2)
  quotes=[e for e in events() if e['kind']=='shop_quote_observed']
  check('S4-native-quote-and-purchase',purchase and quotes and d['snapshot']['money']<cash,dict(before_cash=cash,after_cash=d['snapshot']['money'],purchases=purchase,quotes=quotes))
  # Finish remaining independent probes with native materials, never seed them.
  sc('agent_pause');sc('preparation_probe')
  r=wait(tool('player.travel',location='Farm'),'return-for-held-probe');check('return-farm',r['status']=='succeeded')
  r=wait(tool('work.run',goal='wood',location='Farm',stock_target=55,count=0,include_trees=True,reserve_stamina=15,until=2300),'held-probe-materials',320)
  check('native-materials-for-held-probe',r['status']=='succeeded',r)
  m=tool('menu.open',page='inventory');write('noop-menu.json',m)
  empty=next(c for c in m['choices'] if (c.get('Facts') or {}).get('kind')=='inventory_slot' and not c['Facts']['locked'] and (c['Facts'].get('item') or {}).get('empty'))
  replies=[tool('menu.choose',token=m['token'],id=empty['Id']) for _ in range(3)];write('three-noop-replies.json',replies)
  stopped=sc('preparation_read');write('third-control-stop.json',stopped)
  check('S1-third-noop-execution-stop',replies[-1].get('error')=='menu_no_effect_three' and stopped['autoplay']['state']['Status']=='paused',replies)
  r=tool('menu.choose',token=m['token'],id=empty['Id']);check('fourth-native-click-rejected-before-execution',r.get('error')=='menu_execution_stopped_requires_review',r)
  tool('menu.close');write('held-probe-packed.json',sc('round3',mode='pack'));tool('menu.open',page='crafting')
  held=sc('round3',mode='held');write('native-held-created.json',held)
  m=tool('menu.read');before=tool('inventory.read');r=tool('player.craft',recipe='Chest',count=1);after=tool('inventory.read')
  write('held-preserved.json',dict(menu=m,retry=r,before=before,after=after,final_menu=tool('menu.read')))
  check('S2-explicit-block-preserves-native-held-output',m.get('pending_output',{}).get('state')=='native_held_not_received' and held['crafts_after']==held['crafts_before']+1 and r.get('error')=='production_output_pending_receive_before_next_action' and before==after and tool('menu.read')['held']==m['held'])
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
