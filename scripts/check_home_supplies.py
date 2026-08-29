"""Native starter gift + business queue, no model and no item/map fixture writes.
Requires an AgentLab save with its real unopened farmhouse gift.
"""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();s=b.state();assert s['player']['name']=='AgentLab'
out=Path(sys.argv[1] if len(sys.argv)>1 else 'work/home-supplies-native');out.mkdir(exist_ok=True)
def scenario(code,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**kw))
def tool(code,**kw):return scenario('agent_tool',tool=code,args=kw)
def read():return b.request('GET','/lab/together')['autoplay']
def poll(r):
 end=time.monotonic()+120
 while r['status']=='running' and time.monotonic()<end:
  time.sleep(.2);r=tool('action.status',id=r['command_id'])
 return r
checks=[]
def check(ok,label):
 checks.append(dict(passed=bool(ok),label=label));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
 if not ok:raise AssertionError(label)
scenario('auto',enabled=0);scenario('agent_pause')
before=b.state();(out/'before.json').write_text(json.dumps(before,ensure_ascii=False,indent=2))
check(before['player']['inventory'].get('(O)472:0',0)==0,'no seed grant in initial bag')
r=poll(tool('player.travel',location='Farm'));check(r['status']=='succeeded','native travel outside before home collection')
scenario('agent_schedule_probe');tool('farm.business',enabled=True,expand=False);tool('day.routine',enabled=False)
end=time.monotonic()+120;task=None
try:
 while time.monotonic()<end:
  d=read();task=next((t for t in d['state']['Schedule']['Tasks'] if t['spec']['tool']=='player.collect_home_gifts'),None)
  if task and task['state'] in ['succeeded','failed','blocked','needs_review']:break
  time.sleep(.2)
 (out/'dispatch.json').write_text(json.dumps(d,ensure_ascii=False,indent=2))
 check(task is not None,'business planner queues gift collection without a model')
 check(task['state']=='succeeded','native return, reachable approach, opening and reward verify')
 check(d['state']['Decisions']==0,'no LLM decision needed for starter supplies')
finally:
 scenario('agent_pause');tool('farm.business',enabled=False)
first=b.state();(out/'after.json').write_text(json.dumps(first,ensure_ascii=False,indent=2))
check(first['location']=='FarmHouse' and first['player']['inventory'].get('(O)472:0',0)==15,'15 native starter seeds really received')
r=poll(tool('player.collect_home_gifts'));(out/'repeat.json').write_text(json.dumps(r,ensure_ascii=False,indent=2))
check(r['status']=='succeeded' and r['completed']==0 and b.state()['player']['inventory']==first['player']['inventory'],'repeated call is a no-op, no duplicate reward')
scenario('agent_ui')
