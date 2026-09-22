"""Grouped native dynamic navigation and fault clock checks."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=18797);p.add_argument('--mods-dir',type=Path,default=ROOT/'work/U13CheckMods');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=a.mods_dir.resolve();checks=[];b=None;initial=None

def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
def check(n,ok,data=None):
 checks.append(dict(name=n,passed=bool(ok),data=data));write('checks.json',checks);print(n,ok,flush=True)
config=json.loads((ROOT/'work/U6BuildMods/Together/config.json').read_text());config.update(Autonomy=False,RecordModelTrace=True)
(mods/'Together/config.json').write_text(json.dumps(config,ensure_ascii=False,indent=2))
write('manifest.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest(),fresh_native_save=True,no_save=True,normal_time=True))
log=(out/'game.log').open('w');proc=subprocess.Popen([sys.executable,'scripts/launch.py','--keep-window','--companion','--lab','--mods-dir',str(mods),'--port',str(a.port)],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True,start_new_session=True)
try:
 deadline=time.monotonic()+120;commanded=False
 while time.monotonic()<deadline:
  if proc.poll() is not None:raise RuntimeError('owned_game_exited')
  try:
   c=json.loads((mods/'AgentBridge/config.json').read_text());local=out/'bridge-private.json';fd=os.open(local,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
   with os.fdopen(fd,'w') as f:json.dump(dict(url=f'http://127.0.0.1:{a.port}',token=c['Token']),f)
   b=Bridge(local);h=b.request('GET','/health')
   if h['api_connected'] and not commanded:proc.stdin.write('agent_new\n');proc.stdin.flush();commanded=True
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
 def wait(r,label,seconds=180):
  until=time.monotonic()+seconds
  while r.get('status')=='running' and time.monotonic()<until:
   time.sleep(.4);r=tool('action.status',id=r['command_id'])
   if label in ('escape-from-native-origin','npc-detour','normal-route'):
    with (out/'route-samples.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),receipt=r),ensure_ascii=False)+'\n')
  write(label+'.json',r);return r
 sc('agent_pause');sc('navigation_collision_probe',mode='enter');time.sleep(3)
 setup=sc('navigation_collision_probe',mode='setup');write('collision-setup.json',setup);time.sleep(1)
 route=wait(tool('player.move',x=43,y=74),'escape-from-native-origin',60)
 check('offset-origin-escapes-npc-with-native-movement',route.get('status')=='succeeded',route)
 if route.get('status')!='succeeded':raise RuntimeError('origin_escape_failed')
 sc('navigation_collision_probe',mode='restore')
 # Same fixture, western destination forces an actual detour around Evelyn.
 setup=sc('navigation_collision_probe',mode='setup');time.sleep(1)
 route=wait(tool('player.move',x=42,y=74),'npc-detour',60)
 check('native-dynamic-body-detour',route.get('status')=='succeeded',route)
 if route.get('status')!='succeeded':raise RuntimeError('detour_failed')
 sc('navigation_collision_probe',mode='restore')
 normal=wait(tool('player.move',x=45,y=74),'normal-route',60)
 check('normal-walk-after-detour',normal.get('status')=='succeeded',normal)
 before=sc('navigation_collision_probe',mode='fault');time.sleep(9);after=sc('navigation_collision_probe',mode='read')
 check('fault-pause-holds-native-clock',before['held'] and after['held'] and before['time']==after['time'] and not after['should_pass'],dict(before=before,after=after))
 released=sc('navigation_collision_probe',mode='release_clock');time.sleep(9);advanced=sc('navigation_collision_probe',mode='read')
 check('clock-resumes-after-handoff',not advanced['held'] and advanced['time']>released['time'],dict(before=released,after=advanced))
 write('result.json',dict(passed=all(c['passed'] for c in checks),checks=checks))
finally:
 if b:
  try:sc('navigation_collision_probe',mode='restore');sc('agent_pause')
  except Exception:pass
 logs=mods/'Together/logs'
 if logs.exists():shutil.copytree(logs,out/'native-logs',dirs_exist_ok=True)
 write('process.json',dict(pid=proc.pid,alive=proc.poll() is None,window_preserved=True))
 if proc.stdin:proc.stdin.close()
