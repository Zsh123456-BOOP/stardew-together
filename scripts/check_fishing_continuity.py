"""Isolated native fishing/deadline regression. Fixtures never count as seven-day gameplay."""
import json,sys,time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path(sys.argv[1]);out.mkdir(parents=True,exist_ok=True)
checks=[]
def sc(name,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=name,**kw))
def tool(name,**kw):return sc('agent_tool',tool=name,args=kw)
def check(ok,label):
 checks.append(dict(passed=bool(ok),label=label));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True);assert ok,label
def poll(r,seconds,name):
 end=time.monotonic()+seconds
 while r['status']=='running' and time.monotonic()<end:
  time.sleep(.5);r=tool('action.status',id=r['command_id'])
  with (out/(name+'-samples.jsonl')).open('a') as f:f.write(json.dumps(sc('fishing_continuity_read'),ensure_ascii=False)+'\n')
 (out/(name+'-receipt.json')).write_text(json.dumps(r,ensure_ascii=False,indent=2));return r
try:
 sc('fishing_continuity_fixture');time.sleep(2)
 r=tool('work.run',goal='fish',location='Beach',count=10,until=1020,reserve_stamina=40)
 r=poll(r,35,'deadline')
 check(r['status']=='failed' and r['stop_reason']=='time_reserve_reached','native running fishing/travel stops at trip deadline')
 time.sleep(1);s=sc('fishing_continuity_read')['snapshot']
 check(not s['using_tool'] and s['can_move'],'deadline releases native rod and player movement')
 sc('fishing_continuity_fixture');time.sleep(2)
 r=poll(tool('player.fish',count=1,reserve_stamina=40),210,'catch')
 check(r['status']=='succeeded' and r['completed']>=1,'real native catch completes and records Farmer fish progress')
 time.sleep(1);s=sc('fishing_continuity_read')['snapshot']
 check(not s['using_tool'] and s['can_move'],'completed catch releases player for next work')
finally:
 sc('agent_pause');sc('agent_ui')
