"""Grouped native farm navigation checks. A fresh native AgentLab; no inventory/map writes."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=18780);a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/U7VerifiedMods';checks=[];b=None;initial=None

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
   if label.startswith('clear'):
    with (out/'route-samples.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),receipt=r),ensure_ascii=False)+'\n')
  write(label+'.json',r);return r
 sc('agent_pause');sc('preparation_probe');time.sleep(.5)
 r=wait(tool('player.travel',location='Farm'),'farm-entry');check('native-farm-entry',r.get('status')=='succeeded',r)
 # Search observed native clutter, not a fabricated obstacle fixture.
 found=[]
 for offset in range(0,144,12):
  probe=sc('clearance_probe',offset=offset);write('probe-'+str(offset)+'.json',probe)
  if probe.get('found'):found.append(probe);break
 check('native-map-has-beneficial-clearance-route',bool(found),found)
 if not found:raise RuntimeError('no_native_clearance_case')
 probe=found[0];end=probe['end'];before=b.state();write('before-clear.json',before)
 held=None
 if probe.get('ordinary_steps') is None:
  held=wait(tool('player.move',x=end[0],y=end[1],reserve_stamina=270),'reserved-energy-route')
  check('reserved-energy-not-spent-on-clearing',held.get('before',{}).get('stamina')==held.get('after',{}).get('stamina'),held)
 r=wait(tool('player.move',x=end[0],y=end[1]),'clear-native-route');after=b.state();write('after-clear.json',after)
 route_effects=r.get('effects',[])+(held.get('effects',[]) if held else []);clear=[e for e in route_effects if e.get('kind')=='native_route_clear'];selected=[e for e in route_effects if e.get('kind')=='clearance_route_selected']
 check('native-clear-and-original-destination',r.get('status')=='succeeded' and bool(clear),r)
 check('clearance-selection-and-native-impact-evidence',bool(selected) and all(e['before']!=e['after'] for e in clear),clear)
 check('maximum-three-cleared-obstacles',len({tuple(e['tile']) for e in clear})<=3,clear)
 drops=sc('loose_drop_read');write('after-clear-drops.json',drops)
 # Complete actual pickup and a following unrelated resource task in the same session.
 if drops:
  tiles=list({(int(d['position'][0]//64),int(d['position'][1]//64)) for d in drops})
  pickup=wait(tool('player.collect_drops',tiles=[dict(x=x,y=y) for x,y in tiles]),'post-clear-pickup')
  check('pickup-completes-or-explicitly-defers',pickup.get('status') in ('succeeded','partial'),pickup)
 r=wait(tool('work.run',goal='wood',count=4,reserve_stamina=0,until=2400),'following-resource-work')
 check('independent-resource-work-continues',r.get('status') in ('succeeded','partial') and (r.get('gained',0)>0 or r.get('completed',0)>0),r)
 # Reproduce the former felled-tree pickup blocker on the native standard farm.
 tree_map=tool('map.read',actor_id='player',x=70,y=24,radius=8);write('tree-map.json',tree_map)
 if any(c.get('terrain')=='Tree' and c['x']==70 and c['y']==23 for c in tree_map.get('cells',[])):
  before=b.state();write('tree-before.json',before)
  r=tool('player.work',skill='chop',slot=0,tiles=[dict(x=70,y=23)],reserve_stamina=0);until=time.monotonic()+240
  with (out/'tree-samples.jsonl').open('w') as f:
   while r.get('status')=='running' and time.monotonic()<until:
    time.sleep(.3);r=tool('action.status',id=r['command_id']);f.write(json.dumps(dict(utc=time.time(),receipt=r,drops=sc('loose_drop_read'),player=b.state()['player']),ensure_ascii=False)+'\n');f.flush()
  write('tree-receipt.json',r);after=b.state();write('tree-after.json',after);drops=sc('loose_drop_read');write('tree-drops-after.json',drops)
  gained=after['player']['inventory'].get('(O)388:0',0)-before['player']['inventory'].get('(O)388:0',0)
  check('felled-tree-native-pickup-complete',r.get('status')=='succeeded' and gained>0 and len(drops)==0,dict(receipt=r,wood_gained=gained,ground_remaining=len(drops)))
 else:check('former-tree-case-present',False,tree_map)
 r=wait(tool('work.run',goal='stone',count=5,location='Farm',reserve_stamina=0,until=2400),'independent-stone-after-tree')
 check('no-old-drop-block-on-independent-task',r.get('status')=='succeeded' and r.get('gained',0)>0,r)
 r=wait(tool('player.travel',location='FarmHouse'),'return-house');check('normal-route-after-clearing',r.get('status')=='succeeded',r)
 r=wait(tool('player.travel',location='Forest'),'cross-map-route');check('cross-map-clearance-state-ownership',r.get('status')=='succeeded',r)
 write('final-world.json',b.state());write('final-drops.json',sc('loose_drop_read'))
except Exception as e:check('suite-exception',False,repr(e))
finally:
 if b:
  try:write('final.json',b.request('GET','/lab/together'));sc('agent_pause')
  except Exception:pass
 if initial:
  src=mods/'Together/logs'/str(initial['save_id'])
  if src.exists():shutil.copytree(src,out/'native-logs',dirs_exist_ok=True)
 passed=bool(checks) and all(x['passed'] for x in checks)
 write('result.json',dict(passed=passed,checks=checks,game_pid=proc.pid,window_retained=not passed))
 if passed and proc.poll() is None:proc.stdin.write('agent_quit\n');proc.stdin.flush()
 elif b:
  try:sc('agent_ui')
  except Exception:pass
sys.exit(0 if passed else 1)
