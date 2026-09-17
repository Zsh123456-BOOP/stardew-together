"""Retry seams native grouped suite in one AgentLab process; no resource/progress seeding."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',default='');a=p.parse_args()
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
    if h['api_connected'] and not commanded:proc.stdin.write(('agent_load '+a.save if a.save else 'agent_new')+'\n');proc.stdin.flush();commanded=True
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
  def snap():return sc('round4',mode='autonomy7')
  def idle(seconds=45):
   end=time.monotonic()+seconds
   while time.monotonic()<end:
    d=snap()['snapshot']
    if d['can_move'] and not d.get('menu') and not d['event_up']:return
    time.sleep(.5)
   raise RuntimeError('native_idle_timeout')
  idle();before=snap();write('before.json',before)
  storage=next(x for x in before['opportunities'] if x['Id']=='infrastructure:storage')
  check('chest-recipe-is-scoped-native-fact',storage['Requirements']['entity']=='craft:Chest' and storage['Requirements']['materials']==[dict(item='(O)388',required=50,owned=0,missing=50)],storage)
  q=tool('plan.read');r=tool('plan.submit',submission_id='query-boundary',expected_revision=q['revision'],tasks=[dict(id='invalid-query',actor='player',tool='shop.read',args={})])
  check('query-not-queued-and-exact-step-reported',r.get('submitted_count')==0 and r.get('rejected_task',{}).get('id')=='invalid-query',r)
  q=tool('plan.read');r=tool('plan.submit',submission_id='door-window',expected_revision=q['revision'],tasks=[dict(id='social-after-open',actor='player',tool='player.social',args=dict(npc='Pierre',mode='talk'))])
  check('future-social-queued',r.get('status')=='queued',r)
  q=tool('plan.read');write('early-social.json',q)
  task=next(x for x in q['tasks'] if x['spec']['id']=='social-after-open')
  check('social-uses-real-door-opening',task['spec']['not_before']==900 and task['state']=='queued',task)
  r=wait(tool('player.collect_home_gifts'),'home-seeds');check('native-home-seeds',r.get('status')=='succeeded',r)
  r=wait(tool('player.travel',location='Farm'),'farm');check('native-farm-travel',r.get('status')=='succeeded',r)
  plan=tool('farm.plan',seed='(O)472',count=15);write('first-layout.json',plan);check('initial-layout',bool(plan.get('options')),plan)
  _,ev,d=chain([dict(tool='work.run',args=dict(goal='plant',plan_id=plan['options'][0]['plan_id'],reserve_stamina=0,until=2300))],'first-plant',600)
  # The independent crop job runs before the future social window, then the queued social action executes.
  tasks=d['state']['Schedule']['Tasks'];social=next(x for x in tasks if x['spec']['id']=='social-after-open')
  check('opening-window-resumes-native-social-same-day',social['state']=='succeeded',social)
  first=scene();write('first-farm.json',first);check('native-initial-crops',len(first['farm'])==15,first['farm'])
  r=wait(tool('player.service',location='SeedShop',service='shop',shop='SeedShop'),'native-shop');check('native-shop-open',r.get('status')=='succeeded',r)
  quote=tool('shop.read');write('quote.json',quote)
  r=wait(tool('player.buy',shop='SeedShop',item='(O)472',count=2,max_unit_price=20,budget=40,keep_gold=0),'purchase');check('native-seed-purchase',r.get('status')=='succeeded',r)
  tool('menu.close');idle()
  r=wait(tool('player.travel',location='Farm'),'return-farm');check('native-return-farm',r.get('status')=='succeeded',r)
  purchased=snap();write('purchased-not-planted.json',purchased)
  check('new-seeds-have-planting-candidate',any(x['Tool']=='farm.plan' and x['Args'].get('seed')=='(O)472' and x['Args'].get('count')==2 for x in purchased['opportunities']),purchased['planting_execution'])
  plan=tool('farm.plan',seed='(O)472',count=2);write('second-layout.json',plan);check('second-layout',bool(plan.get('options')),plan)
  _,ev,d=chain([dict(tool='work.run',args=dict(goal='plant',plan_id=plan['options'][0]['plan_id'],reserve_stamina=0,until=2300))],'second-plant',600)
  final_farm=scene();write('second-farm.json',final_farm)
  check('purchase-return-plant-is-separate-from-old-crops',len(final_farm['farm'])==17 and all(x['water']==1 for x in final_farm['farm']),final_farm['farm'])
  _,ev,d=chain([dict(tool='work.run',args=dict(goal='fiber',location='Farm',count=2,reserve_stamina=0,until=2300))],'scythe',300)
  fiber=[t for t in d['state']['Schedule']['Tasks'] if t['spec']['tool']=='work.run' and t['spec']['args'].get('goal')=='fiber']
  check('native-scythe-near-existing-terrain',fiber and fiber[-1]['state']=='succeeded',fiber)
  paths=snap();write('final-observations.json',paths)
  check('native-route-collision-consistent',bool(paths['paths']) and all(not x['blocked'] for x in paths['paths']),paths['paths'])
  sc('agent_pause');check('paused-process-alive',proc.poll() is None)
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
