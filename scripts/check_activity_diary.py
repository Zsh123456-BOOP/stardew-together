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
  before=sc('round4',mode='read');sc('round4',mode='material_autonomy')
  # No approved material target; the explicit stock target is already satisfied.
  r=wait(tool('work.run',goal='wood',stock_target=1),'satisfied-stock',30)
  check('no-project-stock-target-already-satisfied',r.get('status')=='succeeded',r)
  r=wait(tool('work.run',goal='wood',location='Farm',count=2,reserve_stamina=30),'free-material-work',240)
  check('material-work-without-project',r.get('gained',0)>=2,r)
  diary=sc('round4',mode='diary');write('native-diary.json',diary)
  check('actual-bag-gain-in-diary',any(x['Kind']=='行动期间入包' and x['Item'].startswith('(O)388') and x['Count']>=2 for x in diary['diary']['Rows']),diary)
  replay=sc('round4',mode='diary_replay');check('native-receipt-repeat-idempotent',replay['unchanged'],replay)
  # Empty scoped forage generates one remembered condition; repeat is rejected
  # at admission rather than another trip. No inventory/time/world edits.
  r=wait(tool('work.run',goal='forage',location='FarmHouse',count=1),'empty-forage',120)
  check('empty-work-native-result',r.get('status')=='failed',r)
  plan=tool('plan.read')
  t=dict(id='diary-empty-1',tool='work.run',args=dict(goal='forage',location='FarmHouse',count=1),day=before['snapshot']['day'])
  tool('plan.submit',submission_id='diary-empty',expected_revision=plan['revision'],tasks=[t])
  until=time.monotonic()+120
  while time.monotonic()<until:
   time.sleep(1);d=sc('round4',mode='read')
   if any(x['id']=='diary-empty-1' for x in d['schedule']['recent_results']):break
  ctx=sc('round4',mode='diary');write('failure-memory.json',ctx)
  check('recoverable-failure-remembered',any(x['Reason']=='remaining_targets_unreachable' or x['Reason']=='no_matching_targets' for x in ctx['context']['recent_failure_rules']),ctx['context']['recent_failure_rules'])
  plan=tool('plan.read');t['id']='diary-empty-2';t['args']['count']=2
  again=tool('plan.submit',submission_id='diary-empty-again',expected_revision=plan['revision'],tasks=[t])
  check('repeat-with-changed-count-rejected-before-trip','known_failure_conditions_unchanged' in str(again),again)
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
