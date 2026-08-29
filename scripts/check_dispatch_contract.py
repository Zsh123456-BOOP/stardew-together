"""Native queue-vs-semantic ownership and location preconditions, AgentLab only."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,BridgeError
b=Bridge();s=b.state();assert s['player']['name']=='AgentLab'
def scenario(c,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=c,**kw))
def tool(c,**kw):return scenario('agent_tool',tool=c,args=kw)
checks=[]
def check(ok,name):
 checks.append(dict(passed=bool(ok),name=name));Path('work/dispatch-contract.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+name,flush=True)
 if not ok:raise SystemExit(name)
check(s['location']=='FarmHouse','loaded in native farmhouse')
try:tool('farm.plan',seed='(O)472',count=15);rejected=False
except BridgeError as e:rejected='first_travel_to_Farm' in str(e)
check(rejected,'farmhouse planning returns actionable location error, not empty fields')
scenario('inventory_timing_fixture');time.sleep(2);scenario('agent_schedule_probe')
job=tool('work.run',goal='wood',location='Farm',count=3)
p=tool('plan.read');tool('plan.submit',submission_id='ownership-check',expected_revision=p['revision'],tasks=[dict(id='after-work',actor='player',tool='player.move',args=dict(x=45,y=26),day=b.state()['day'])])
d=b.request('GET','/lab/together')['autoplay'];task=next(t for t in d['state']['Schedule']['Tasks'] if t['spec']['id']=='after-work');check(task['state']=='queued','queue waits while unscheduled semantic work owns the actor')
end=time.monotonic()+120
while time.monotonic()<end:
 time.sleep(.2);d=b.request('GET','/lab/together')['autoplay'];task=next(t for t in d['state']['Schedule']['Tasks'] if t['spec']['id']=='after-work')
 if task['state'] in ['failed','succeeded','blocked']:break
check(task['state']=='succeeded','queued move starts after complete resource job without false ownership failure')
check(d['state']['Decisions']==0,'automatic ownership wait needs no model decision')
scenario('agent_pause')
