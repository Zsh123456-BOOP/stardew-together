"""One AgentLab session; native stock and actions only. No progress/resource seeding."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge
from root_failure_watch import RootFailureWatch
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',default='AgentLab_449652194');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/Round2Mods';b=None;checks=[]
(out/'manifest.json').write_text(json.dumps(dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest(),save=a.save,normal_time=True),indent=2))
def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
def check(n,ok,data=None):
 checks.append(dict(name=n,passed=bool(ok),data=data));write('checks.json',checks);print(n,ok,flush=True)
 if not ok:raise RuntimeError(n)
with (out/'game.log').open('w') as log:
 proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--mods-dir',str(mods),'--port','18773'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
 try:
  deadline=time.monotonic()+120;commanded=False
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('owned_game_exited')
   try:
    c=json.loads((mods/'AgentBridge/config.json').read_text());local=out/'bridge-private.json';fd=os.open(local,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18773',token=c['Token']),f)
    b=Bridge(local);h=b.request('GET','/health')
    if h['api_connected'] and not commanded:proc.stdin.write('agent_load '+a.save+'\n');proc.stdin.flush();commanded=True
    if h['ready']:break
   except (OSError,ValueError,RuntimeError):pass
   time.sleep(1)
  initial=b.state();check('AgentLab-isolated',initial['player']['name']=='AgentLab')
  def sc(n,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=n,**kw))
  def tool(n,**kw):
   r=sc('agent_tool',tool=n,args=kw)
   with (out/'calls.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),tool=n,args=kw,result=r),ensure_ascii=False)+'\n')
   return r
  def wait(r,label,seconds=600):
   until=time.monotonic()+seconds
   while r['status']=='running' and time.monotonic()<until:
    time.sleep(.5);r=tool('action.status',id=r['command_id']);write('latest-action.json',r)
   write(label+'.json',r);return r
  sc('agent_pause');sc('preparation_probe');write('initial.json',sc('preparation_read'))
  d=sc('round2_observation');write('observation-before.json',d);packed=json.loads(d['packed']);inv=packed['inventory_plan']
  check('F1-scalars-and-real-world-query',all(k in inv for k in ['slot_count','free_slots','occupied','capacity_constraints','capacity_release_conditions']) and isinstance(packed['recent'][0]['data']['result']['inventory_plan'],dict),inv)
  check('F2-nested-not-physical',d['nested']['RootCause']=='relief_depth_limit' and not d['nested_is_physical_constraint'],d['nested'])
  # Wait at normal time for the loaded native event's own acquisition window.
  until=time.monotonic()+150
  while d['fishing_dependency']['state']!='available' and time.monotonic()<until:
   if d['fishing_dependency']['state']=='complete':break
   time.sleep(2);d=sc('round2_observation')
  write('capability.json',d)
  check('F5-native-rod-acquisition-candidate',any(c['Id']=='acquire:fishing' for c in d['candidates']),d['fishing_dependency'])
  candidate=next(c for c in d['candidates'] if c['Id']=='acquire:fishing')
  if candidate['Tool']=='player.read_mail':
   mail=wait(tool('player.read_mail',count=1),'native-willy-mail',120);check('native-invitation-read',mail['status']=='succeeded')
   d=sc('round2_observation');write('rod-event-after-mail.json',d)
   check('native-event-now-available',d['fishing_dependency']['state']=='available')
  r=wait(tool('player.travel',location='Beach'),'native-willy-travel',120)
  until=time.monotonic()+130
  while time.monotonic()<until:
   d=sc('round2_observation');snap=sc('preparation_read')['autoplay']['snapshot']
   if d['fishing_dependency']['state']=='complete' and not snap['event_up'] and not snap['menu']:break
   time.sleep(1)
  write('native-rod.json',d);check('native-rod-obtained',d['fishing_dependency']['state']=='complete')
  # Direct semantic invocation intentionally retains current kit, to exercise
  # repeated recovery inside ONE long parent task, not a new task each time.
  r=wait(tool('work.run',goal='fish',location='Town',count=20,max_food=0,reserve_stamina=15,until=2300),'long-fishing',800)
  entries=[e for e in r.get('evidence',[]) if e.get('kind')=='native_storage']
  check('F2-two-unloads-one-parent',r['status']=='succeeded' and len(entries)>=2,dict(status=r['status'],error=r.get('error'),deposits=len(entries),completed=r.get('completed'),gained=r.get('gained')))
  # A fresh departure task has a single automatic loadout, using real leftovers.
  home=wait(tool('player.travel',location='Farm'),'return-for-next-trip',120);check('native-return-for-departure',home['status']=='succeeded')
  before=sc('preparation_read');write('departure-before.json',before)
  plan=tool('plan.read');label='round2-departure'
  tool('plan.submit',submission_id=label,expected_revision=plan['revision'],tasks=[dict(id=label,actor='player',tool='work.run',args=dict(goal='fish',location='Beach',count=1,reserve_stamina=15,until=2300),purpose='出行前卸载无关物资')])
  until=time.monotonic()+160
  while time.monotonic()<until:
   d=sc('preparation_read');write('departure-latest.json',d)
   done=next((t for t in d['autoplay']['schedule']['recent_results'] if t['id']==label),None)
   if done:break
   time.sleep(.5)
  logs=list((mods/'Together/logs'/str(initial['save_id'])).glob('*/day-*.jsonl'))
  events=[json.loads(x) for pth in logs for x in pth.read_text().splitlines()]
  check('F4-native-loadout-transfer',any(e['kind']=='loadout_transferred' and e['payload']['id']==label for e in events))
  watcher=RootFailureWatch();run=d['autoplay']['state']['RunId'];stop=None
  for n in range(3):
   result=sc('round2_declaration',tool='work.run',args=dict(actor_id='npc:not-present',goal='wood',count=1));write('declaration-'+str(n+1)+'.json',result)
   time.sleep(.5)
   for pth in logs:
    for line in pth.read_text().splitlines():
     ev=json.loads(line)
     if ev.get('run')==run:
      found=watcher.observe(ev)
      if found:stop=found
   if stop:
    write('third-refusal-before-stop.json',dict(stop=stop,scene=sc('preparation_read')));sc('agent_pause');write('third-refusal-paused.json',sc('preparation_read'));break
  check('F3-third-declaration-stops-live',n==2 and stop and sc('preparation_read')['autoplay']['state']['Status']=='paused',stop)
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
