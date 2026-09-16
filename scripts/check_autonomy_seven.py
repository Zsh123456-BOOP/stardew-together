"""Seven-day fixes native seam suite in one AgentLab process; no resource/progress seeding."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',default='AgentLab_449672941');p.add_argument('--night-only',action='store_true');a=p.parse_args()
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
  before=sc('round4',mode='autonomy7');write('before.json',before)
  check('progress-always-has-real-quests-mail-achievements',all(k in before['progress'] for k in ['active_quests','unread_mail','nearby_achievements','unlocks']),before['progress'])
  ready_until=time.monotonic()+30
  while time.monotonic()<ready_until:
   ready=sc('round4',mode='autonomy7')['snapshot']
   if ready['can_move'] and not ready.get('menu') and not ready['event_up']:time.sleep(1);break
   time.sleep(.5)
  r=wait(tool('player.travel',location='Farm'),'farm',240)
  check('native-farm-exit',r.get('status')=='succeeded',r)
  paths=sc('round4',mode='autonomy7');write('paths.json',paths['paths'])
  check('planned-paths-match-map-collision',bool(paths['paths']) and all(not p['blocked'] for p in paths['paths']),paths['paths'])
  if not a.night_only:
   inventory=sc('preparation_read');write('storage-before.json',inventory)
   opportunities=sc('round4',mode='autonomy7')['opportunities']
   check('owned-stored-rod-produces-fishing-opportunity',any(x['Id']=='fish-income' for x in opportunities),opportunities)
   calls=[dict(tool='work.run',args=dict(goal='fish',location='Beach',count=1,reserve_stamina=0,until=2200))]
   _,ev,d=chain(calls,'retrieve-and-fish',600)
   tasks=[t for t in d['state']['Schedule']['Tasks'] if t['spec']['tool']=='work.run' and t['spec']['args'].get('goal')=='fish']
   check('native-stored-tool-loadout-and-catch',tasks and tasks[-1]['state']=='succeeded',tasks)
   r=wait(tool('player.travel',location='Farm'),'return',240);check('native-return',r.get('status')=='succeeded',r)
   _,ev,d=chain([dict(tool='work.run',args=dict(goal='fiber',location='Farm',count=2,reserve_stamina=0,until=2300))],'scythe',240)
   tasks=[t for t in d['state']['Schedule']['Tasks'] if t['spec']['tool']=='work.run' and t['spec']['args'].get('goal')=='fiber']
   check('native-scythe-clearing',tasks and tasks[-1]['state']=='succeeded',tasks)
   state=sc('round4',mode='autonomy7');reserve=max(0,int(state['snapshot']['stamina'])-5)
   _,ev,d=chain([dict(tool='work.run',args=dict(goal='wood',location='Farm',count=100,reserve_stamina=reserve,max_food=0,until=2300))],'budget-yield',240)
   tasks=[t for t in d['state']['Schedule']['Tasks'] if t['spec']['tool']=='work.run' and t['spec']['args'].get('goal')=='wood']
   check('native-budget-yield-is-partial-not-failure',tasks and tasks[-1]['state']=='partial',tasks)
   write('yield-state.json',sc('round4',mode='autonomy7'))
  tool('farm.cleanup',request_id='seven-review-order',scopes=['general'],daily_limit=2,reserve_stamina=0,until=2200)
  until=time.monotonic()+120
  while time.monotonic()<until:
   d=sc('round4',mode='autonomy7');orders=[o for o in d['orders'] if o['Id']=='seven-review-order']
   if orders and orders[-1]['Completed']>=2:break
   time.sleep(1)
  check('mixed-cleanup-native-progress',orders and orders[-1]['Completed']>=2,orders)
  r=wait(tool('player.sleep',reason='AgentLab组合验证跨日清理暂停，非经营选择'),'native-night',400);check('native-night-completes',r.get('status')=='succeeded',r)
  d=sc('round4',mode='autonomy7');write('next-day.json',d)
  order=next(o for o in d['orders'] if o['Id']=='seven-review-order')
  check('old-cleanup-awaits-new-day-choice',order['Status']=='paused' and order['Reason']=='new_day_replan_remaining_cleanup',order)
  sc('agent_pause');check('paused-preserves-process',proc.poll() is None)
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
