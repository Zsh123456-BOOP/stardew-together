"""One-session native regression: real AgentLab stock; no resource or progress writes."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',required=True);a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/U6BuildMods';checks=[];b=None;initial=None

def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
def check(n,ok,data=None):
 checks.append(dict(name=n,passed=bool(ok),data=data));write('checks.json',checks);print(n,ok,flush=True)
config=json.loads((ROOT/'work/U5BuildMods/Together/config.json').read_text());config.update(Autonomy=False,RecordModelTrace=True)
(mods/'Together/config.json').write_text(json.dumps(config,ensure_ascii=False,indent=2))
write('manifest.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest(),save=a.save,no_save=True,normal_time=True))
log=(out/'game.log').open('w');proc=subprocess.Popen([sys.executable,'scripts/launch.py','--keep-window','--companion','--lab','--mods-dir',str(mods),'--port','18778'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True,start_new_session=True)
try:
 deadline=time.monotonic()+120;commanded=False
 while time.monotonic()<deadline:
  if proc.poll() is not None:raise RuntimeError('owned_game_exited')
  try:
   c=json.loads((mods/'AgentBridge/config.json').read_text());local=out/'bridge-private.json';fd=os.open(local,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
   with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18778',token=c['Token']),f)
   b=Bridge(local);h=b.request('GET','/health')
   if h['api_connected'] and not commanded:proc.stdin.write('agent_load '+a.save+'\n');proc.stdin.flush();commanded=True
   if h['ready']:break
  except (OSError,ValueError,RuntimeError):pass
  time.sleep(.5)
 initial=b.state();check('isolated-AgentLab',initial['player']['name']=='AgentLab');write('initial.json',initial)
 def sc(n,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=n,**kw))
 def tool(n,**kw):
  try:r=sc('agent_tool',tool=n,args=kw)
  except BridgeError as e:r=dict(status='failed',error=str(e))
  with (out/'calls.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),tool=n,args=kw,result=r),ensure_ascii=False)+'\n')
  return r
 def diag():return b.request('GET','/lab/together')['autoplay']
 def obs():return sc('round4',mode='u6_read')
 def wait(r,label,seconds=300):
  until=time.monotonic()+seconds
  while r.get('status')=='running' and time.monotonic()<until:time.sleep(.5);r=tool('action.status',id=r['command_id'])
  write(label+'.json',r);return r
 def chain(calls,label,seconds=300):
  r=sc('decision_chain',turn=dict(plan='集中原生检查；不睡觉、不改物品或进度',speech='',calls=calls));write(label+'-submit.json',r)
  until=time.monotonic()+seconds
  while time.monotonic()<until:
   time.sleep(.5);d=diag();write('latest.json',d)
   if d['state']['Status']!='running' or not d.get('continuation') and not any(x['state'] in ('running','queued') for x in d['schedule']['tasks']):break
  write(label+'-state.json',d);return d
 sc('agent_pause');sc('preparation_probe');time.sleep(.5)
 before=obs();write('before.json',before)
 scope=sc('round4',mode='u6_capacity_scope');write('capacity-scope.json',scope)
 check('larger-capacity-denied-smaller-admitted',scope.get('large') and scope.get('small') is None and scope['inventory_unchanged'],scope)
 d=chain([dict(tool='work.run',args=dict(goal='store',required_free_slots=scope['free'],until=2500))],'capacity-noop')
 tasks=d['state']['Schedule']['Tasks'];t=tasks[-1];r=json.loads(t['receipt']) if t.get('receipt') else {}
 after=obs();write('after-noop.json',after)
 check('native-capacity-noop-no-storage-trip',t['state']=='succeeded' and r.get('deposited')==0 and before['inventory_plan']['ownership']==after['inventory_plan']['ownership'],r)
 packed=sc('round2_observation');write('compressed-observation.json',packed)
 packed=json.loads(packed['packed']);inv=packed['inventory_plan']
 check('ownership-and-scalars-survive-compression',all(k in inv for k in ('slot_count','free_slots','occupied','ownership')) and 'warehouse' in inv['ownership'],inv)
 # Declaration rejection must retain an independent native travel, but not a true dependent buy.
 rev=diag()['state']['Schedule']['Revision']
 d=chain([dict(tool='plan.submit',args=dict(submission_id='u6-independent',expected_revision=rev,tasks=[dict(id='u6-store',tool='work.run',args=dict(goal='store',required_free_slots=scope['free']+1)),dict(id='u6-farm',tool='player.travel',args=dict(location='Farm'),sequence_after=['u6-store']),dict(id='u6-dependent',tool='player.ship_items',args=dict(items=[]),after=['u6-store'])]))],'independent-native')
 byid={t['spec']['id']:t for t in d['state']['Schedule']['Tasks']}
 check('independent-native-travel-survives-storage-rejection',byid.get('u6-farm',{}).get('state')=='succeeded' and byid.get('u6-store',{}).get('state')=='blocked' and byid.get('u6-dependent',{}).get('state')=='blocked',byid)
 sc('preparation_probe')
 # Work with the checkpoint's actual individual fish. Candidate must include its lone item.
 stock=obs();write('sale-stock.json',stock)
 single=next((x for x in stock['sales'] if x['owned']==1 and x['count']==1 and x['protected_count']==0),None)
 check('single-real-item-is-sale-candidate',single is not None,single)
 if single:
  bag=stock['inventory_plan']['ownership']['carried']
  if not any(x['id']==single['item'] for x in bag):wait(tool('work.run',goal='withdraw',item=single['item'],count=1,until=2500),'withdraw-sale')
  r=wait(tool('player.ship_items',items=[dict(item=single['item'],count=1)]),'single-native-sale')
  check('single-item-native-shipment',r.get('status')=='succeeded',r)
 sleep=sc('round4',mode='u6_sleep_review');write('sleep-review.json',sleep)
 check('old-sleep-reassessed-without-native-sleep',sleep['reviewed'] and sleep['state']=='cancelled' and sleep['native_state_unchanged'],sleep)
 r=wait(tool('player.service',location='Town',service='daily_quests'),'service-coordinates');tool('menu.close')
 effects=r.get('effects',[]);coords=[e for e in effects if e.get('kind')=='service_counter_stand']
 check('native-counter-coordinates-explicit',r.get('status')=='succeeded' and bool(coords) and all('x' in e['counter'] and 'y' in e['stand'] for e in coords),r)
 # Last check deliberately triggers the stop: the fourth read must never execute.
 sc('preparation_probe')
 d=chain([dict(tool='tools.u6_invalid',args={}) for _ in range(2)],'first-two-failures')
 version=d['state']['Capacity']['Version'];check('failure-captures-current-capacity-version',version>0 and d['state']['Status']=='running',version)
 transfer=wait(tool('work.run',goal='withdraw',item='(O)18',count=1,until=2500),'version-changing-native-withdraw')
 check('real-stock-change-before-failure-retest',transfer.get('status')=='succeeded',transfer)
 d=chain([dict(tool='tools.u6_invalid',args={}) for _ in range(3)]+[dict(tool='inventory.read',args={})],'synchronous-third-stop')
 check('stock-change-advances-failure-version',d['state']['Capacity']['Version']>version,d['state']['Capacity']['Version'])
 check('third-identical-failure-stops-execution-layer',d['state']['Status']!='running' and 'same_root_three_distinct_attempts' in d['state']['Detail'],d['state']['Detail'])
except Exception as e:
 check('suite-exception',False,repr(e))
finally:
 if b:
  try:write('final.json',diag());sc('agent_pause')
  except Exception:pass
 if initial:
  src=mods/'Together/logs'/str(initial['save_id'])
  if src.exists():shutil.copytree(src,out/'native-logs',dirs_exist_ok=True)
 passed=bool(checks) and all(x['passed'] for x in checks)
 write('result.json',dict(passed=passed,checks=checks,game_pid=proc.pid,window_retained=not passed))
 if passed and proc.poll() is None:
  proc.stdin.write('agent_quit\n');proc.stdin.flush()
  try:proc.wait(timeout=30)
  except subprocess.TimeoutExpired:pass
 log.close()
sys.exit(0 if passed else 1)
