"""Isolated UI check. Synthetic display content is never business evidence."""
import argparse,json,os,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',required=True);a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/CapacityMods';b=None
with (out/'game.log').open('w') as log:
 proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--mods-dir',str(mods),'--port','18769'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
 try:
  until=time.monotonic()+120;commanded=False
  while time.monotonic()<until:
   if proc.poll() is not None:raise RuntimeError('owned_game_exited')
   try:
    config=json.loads((mods/'AgentBridge/config.json').read_text());private=out/'bridge-private.json';fd=os.open(private,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18769',token=config['Token']),f)
    b=Bridge(private);h=b.request('GET','/health')
    if h['api_connected'] and not commanded:proc.stdin.write('agent_load '+a.save+'\n');proc.stdin.flush();commanded=True
    if h['ready']:break
   except (OSError,ValueError,RuntimeError):pass
   time.sleep(1)
  assert b.state()['player']['name']=='AgentLab'
  def sc(n,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=n,**kw))
  def tool(n,**kw):return sc('agent_tool',tool=n,args=kw)
  sc('agent_pause');tool('menu.close');sc('preparation_probe')
  tasks=[dict(id='ui-'+str(n),actor='player',tool='work.run',args=dict(goal=g,count=1,location='Farm'),not_before=2200,deadline=2210,purpose='界面测试占位，执行前取消') for n,g in enumerate(['plant','cleanup','water','harvest','store'])]
  tool('plan.submit',submission_id='ui-only',expected_revision=tool('plan.read')['revision'],tasks=tasks)
  reply=dict(plan='界面验证：先把这次用不到的工具和资源存进仓库，再准备需要的种子与工具，整理规划好的田块，完成播种和浇水；遇到空间不足时先核对现有箱子，保留任务要用的东西，之后再安排下一项工作。',speech='',calls=[dict(tool='plan.read',args={})])
  sc('agent_reply_probe',reply=json.dumps(reply,ensure_ascii=False));time.sleep(.5)
  (out/'fixture.json').write_text(json.dumps(dict(synthetic_display_only=True,reply=reply,tasks=tasks),ensure_ascii=False,indent=2))
  (out/'initial.json').write_text(json.dumps(sc('preparation_read'),ensure_ascii=False,indent=2));print('UI READY',flush=True)
  until=time.monotonic()+360
  while time.monotonic()<until and not (out/'ui-done').exists():time.sleep(1)
  tool('plan.cancel',ids=[t['id'] for t in tasks]);sc('agent_pause')
 finally:
  if b:
   try:sc('agent_pause')
   except Exception:pass
  if proc.poll() is None:
   proc.stdin.write('agent_quit\n');proc.stdin.flush()
   try:proc.wait(timeout=30)
   except subprocess.TimeoutExpired:print('exit pending',proc.pid)
