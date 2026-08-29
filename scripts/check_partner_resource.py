"""Custom worker resource skill plus native next-day persistence; explicit fixture."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();s=b.state();assert s['player']['name']=='AgentLab'
def scenario(c,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=c,**kw))
def tool(c,**kw):return scenario('agent_tool',tool=c,args=kw)
def poll(r):
 end=time.monotonic()+180
 while r['status']=='running' and time.monotonic()<end:
  time.sleep(.2);r=tool('action.status',id=r['command_id'])
 return r
scenario('agent_semantic_fixture');time.sleep(2)
a=next(a for a in b.state()['actors'] if a['name']=='Together_Partner');assert a['managed'] and a['control_mode']=='independent'
r=poll(tool('work.run',actor_id=a['id'],goal='wood',location='Farm',count=3));Path('work/partner-resource-final.json').write_text(json.dumps(r,ensure_ascii=False,indent=2));assert r['status']=='succeeded' and r['gained']>=3,r
print('PASS resource wood is real and does not require a planting-area policy',flush=True)
r=poll(tool('player.sleep'));assert r['status']=='succeeded',r
s=b.state();a=next(a for a in s['actors'] if a['name']=='Together_Partner');assert a['managed'] and a['control_mode']=='independent'
assert a['cargo'].get('(O)388:0',0)>=3
print('PASS native next day retains independent custom worker and nonempty pouch',flush=True)
expected=dict(save_id=s['save_id'],actor=a['id'],cargo=a['cargo'],day=s['day']);Path('work/partner-resource-saved.json').write_text(json.dumps(expected,ensure_ascii=False,indent=2))
