"""Native shared actor identity, timed companion work and queued bedtime contract."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,BridgeError
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path('work/closing-coordination');out.mkdir(exist_ok=True);checks=[]
def sc(code,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**kw))
def tool(code,**kw):return sc('agent_tool',tool=code,args=kw)
def read():return b.request('GET','/lab/together')['autoplay']
def check(ok,label):
 checks.append(dict(passed=bool(ok),label=label));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True);assert ok,label
try:
 sc('bedtime_review_fixture');time.sleep(2);sc('agent_schedule_probe');tool('day.routine',enabled=False)
 actor=b.state()['actors'][0]['id'];day=read()['snapshot']['day'];rev=tool('plan.read')['revision']
 tool('plan.submit',submission_id='closing-companion',expected_revision=rev,tasks=[dict(id='closing-companion',actor=actor,tool='companion.assign',args=dict(skill='rest',seconds=5),day=day)])
 time.sleep(.5)
 tool('plan.submit',submission_id='closing-sleep',expected_revision=tool('plan.read')['revision'],tasks=[dict(id='closing-sleep',actor='player',tool='player.sleep',args=dict(reason='今日农务结束，伙伴收尾后正常过夜',review='低体力、晚间，无紧急农務，交接完成即返家保存'),day=day)])
 d=read();sleep=next(t for t in d['state']['Schedule']['Tasks'] if t['spec']['id']=='closing-sleep')
 check(sleep['state']=='queued','sleep remains queued while companion finishes, without failure')
 start=time.monotonic();end=start+150
 while time.monotonic()<end:
  d=read();tasks={t['spec']['id']:t for t in d['state']['Schedule']['Tasks']}
  if tasks['closing-sleep']['state'] in ['succeeded','failed','blocked']:break
  time.sleep(.3)
 (out/'evidence.json').write_text(json.dumps(d,ensure_ascii=False,indent=2))
 check(tasks['closing-companion']['state']=='succeeded','outer actor inherited by native timed companion action')
 check(tasks['closing-sleep']['state']=='succeeded' and d['state']['SleepDays']==1,'sleep resumes after native timer and completes exactly one saved day')
 check(d['state']['Decisions']==0,'waiting and resumption use no model calls')
finally:
 sc('agent_pause');sc('agent_ui')
