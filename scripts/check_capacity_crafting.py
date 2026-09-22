"""R-A1 native craft order probe. Uses an existing AgentLab, no item fixture.

Never writes progress, inventory, money or time. Exits without saving so the
source checkpoint stays available as evidence. First native failure stops.
"""
import argparse,json,os,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge
p=argparse.ArgumentParser();p.add_argument('--save',required=True);p.add_argument('--output',required=True,type=Path);p.add_argument('--count',type=int,default=1);p.add_argument('--storage',action='store_true');p.add_argument('--constraints',action='store_true');a=p.parse_args()
out=a.output;out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/CapacityMods'
def write(name,value):(out/name).write_text(json.dumps(value,ensure_ascii=False,indent=2))
passed=False;b=None
with (out/'game.log').open('w') as log:
 proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--mods-dir',str(mods),'--port','18769'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
 try:
  commanded=False;deadline=time.monotonic()+120
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('owned_game_exited')
   try:
    config=json.loads((mods/'AgentBridge/config.json').read_text());private=out/'bridge-private.json'
    fd=os.open(private,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18769',token=config['Token']),f)
    b=Bridge(private);health=b.request('GET','/health')
    if health['api_connected'] and not commanded:proc.stdin.write('agent_load '+a.save+'\n');proc.stdin.flush();commanded=True
    if health['ready']:break
   except (OSError,ValueError,RuntimeError):pass
   time.sleep(1)
  assert b.state()['player']['name']=='AgentLab'
  def sc(name,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=name,**kw))
  def tool(name,**kw):return sc('agent_tool',tool=name,args=kw)
  sc('agent_pause')
  diag=b.request('GET','/lab/together');write('initial.json',diag)
  tool('menu.close')
  audit=sc('capacity_stack_audit');write('stack-audit.json',audit);assert not any(row['unsafe_match'] for row in audit),'native stack equivalence must not overestimate'
  before=sc('capacity_read');write('before.json',before)
  assert before['capacity']['FreeSlots']==0,'probe requires a genuinely full native bag'
  assert sum(i['count'] for i in before['body']['items'] if i['id']=='(O)388')==50*a.count,'native saved materials required'
  assert before['chest']['Feasible'],'model must admit consume-before-output'
  if a.constraints:
   import shutil,hashlib
   source=Path.home()/'.config/StardewValley/Saves'/a.save
   shutil.copytree(source,out/'source-save-backup')
   state=sc('capacity_constraints',mode='block');write('constraint-before.json',state)
   for goal in ['harvest','forage']:
    guard=sc('capacity_constraints',mode='guard',tool='work.run',args={'goal':goal});write('guard-'+goal+'.json',guard);assert guard['blocked'],'same root must block two different tools'
   sleep=sc('capacity_native_sleep');deadline=time.monotonic()+90
   while sleep['status']=='running' and time.monotonic()<deadline:
    time.sleep(.2);sleep=tool('action.status',id=sleep['command_id'])
   write('native-sleep.json',sleep);assert sleep['status']=='succeeded','real bed/save/day chain'
   next_state=sc('capacity_constraints',mode='read');write('constraint-next-day.json',next_state)
   assert next_state['Version']==state['Version'] and next_state['Constraints'],'date alone must not clear capacity root'
   assert sc('capacity_read')['body']['items']==before['body']['items'],'no inventory fixtures or overnight capacity change'
  result=tool('work.run',goal='store',required_free_slots=2) if a.storage else tool('player.craft',recipe='Chest',count=a.count);deadline=time.monotonic()+150
  while result['status']=='running' and time.monotonic()<deadline:
   time.sleep(.15);result=tool('action.status',id=result['command_id'])
   with (out/'samples.jsonl').open('a') as f:f.write(json.dumps(result,ensure_ascii=False)+'\n')
  write('receipt.json',result);after=sc('capacity_read');write('after.json',after)
  assert result['status']=='succeeded',str(result.get('error'))
  counts=lambda body:{id:sum(i['count'] for i in body['items'] if i['id']==id) for id in {i['id'] for i in body['items'] if i['id']}}
  expected=counts(before['body']);expected['(O)388']-=50*a.count;expected['(BC)130']=expected.get('(BC)130',0)+a.count;expected={k:v for k,v in expected.items() if v}
  if not a.storage:assert counts(after['body'])==expected,'native material/output conservation'
  else:
   assert after['capacity']['FreeSlots']>=2,'automatic crafting, placement and storage frees actual slots'
  assert after['body']['recipes']['Chest']-before['body']['recipes']['Chest']==a.count,'native recipe count'
  assert after['menu'] is None,'held output fully placed and native menu closed'
  if a.storage:
   result=tool('work.run',goal='withdraw',item='(O)390',count=3);deadline=time.monotonic()+60
   while result['status']=='running' and time.monotonic()<deadline:
    time.sleep(.2);result=tool('action.status',id=result['command_id'])
   write('withdraw.json',result);assert result['status']=='succeeded','native withdraw'
   result=tool('player.craft',recipe='Cobblestone Path',count=2);deadline=time.monotonic()+30
   while result['status']=='running' and time.monotonic()<deadline:
    time.sleep(.15);result=tool('action.status',id=result['command_id'])
   write('batch-receipt.json',result);write('batch-after.json',sc('capacity_read'))
   assert result['status']=='succeeded' and result['completed']==2,'two native iterations and insertion between batches'
  if a.constraints:
   released=sc('capacity_constraints',mode='read');write('constraint-after-crafting.json',released)
   assert released['Version']>state['Version'] and not released['Constraints'],'native capacity change releases root'
  passed=True
 except Exception as e:
  write('failure.json',dict(type=type(e).__name__,error=str(e)));print('STOP',type(e).__name__,str(e),flush=True)
 finally:
  write('result.json',dict(passed=passed,source_save=a.save,count=a.count,no_fixture_mutations=True))
  if b:
   try:sc('agent_pause')
   except Exception:pass
  if proc.poll() is None:
   proc.stdin.write('agent_quit\n');proc.stdin.flush()
   try:proc.wait(timeout=30)
   except subprocess.TimeoutExpired:print('owned exit pending',proc.pid)
print('native_crafting_passed',passed,flush=True)
sys.exit(0 if passed else 1)
