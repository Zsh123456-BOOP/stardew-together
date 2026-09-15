"""Fourth-round native seam suite in one AgentLab process; no resource/progress seeding."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',default='AgentLab_449657228');a=p.parse_args()
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
  # First use normal travel to let opening hours pass; no time or position edits.
  r=wait(tool('player.travel',location='Town'),'town',240)
  check('walk-to-town',r.get('status')=='succeeded',r)
  until=time.monotonic()+180
  while time.monotonic()<until:
   d=sc('round4',mode='read')
   if d['snapshot']['time']>=900:break
   time.sleep(2)
  calls=[dict(tool='player.service',args=dict(location='SeedShop',service='shop',shop='SeedShop')),dict(tool='shop.read',args={}),dict(tool='player.travel',args=dict(location='Farm'))]
  r,ev,d=chain(calls,'native-shop')
  rows=[x for x in ev if x.get('kind')=='tool_result']
  write('tool-events.json',rows)
  # Event payloads use the same business JSON envelope as baseline analysis.
  check('continuation-finished',not d.get('continuation'),d)
  shops=[x for x in rows if x.get('run')==d['state']['RunId'] and x['payload'].get('tool')=='shop.read']
  check('real-quote-after-native-service',len(shops)==1 and 'error' not in shops[0]['payload']['result'] and any(x['kind']=='task_finished' and x['payload'].get('state')=='succeeded' and x['utc']<shops[0]['utc'] for x in ev),shops)
  check('native-menu-closed' ,d['snapshot']['menu'] is None,d['snapshot'])
  check('queued-travel-reaches-farm',d['snapshot']['location']=='Farm' and not any(x['state'] in ('queued','running') for x in d['schedule']['tasks']),d['snapshot'])
  check('native-departure-close-recorded',any(x['kind']=='shop_closed_for_departure' and x['payload']['closed'] for x in ev))

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
