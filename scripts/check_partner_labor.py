"""One real partner resource action and repeated stock-floor request; AgentLab only."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path('work/partner-labor-native');out.mkdir(exist_ok=True)
def sc(code,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**kw))
def tool(code,**kw):return sc('agent_tool',tool=code,args=kw)
try:
 sc('business_recovery_fixture');time.sleep(2)
 actor=next(a for a in b.state()['actors'] if a['name']=='Together_Partner');before=sc('operating_read');old=actor['labor']['used']
 r=tool('work.run',actor_id=actor['id'],goal='wood',count=1,stock_target=before['stock']+1);end=time.monotonic()+150
 while r['status']=='running' and time.monotonic()<end:time.sleep(.3);r=tool('action.status',id=r['command_id'])
 after=sc('operating_read');new=next(a for a in b.state()['actors'] if a['id']==actor['id'])['labor']['used']
 (out/'evidence.json').write_text(json.dumps(dict(before=before,receipt=r,after=after,labor_before=old,labor_after=new),ensure_ascii=False,indent=2))
 assert r['status']=='succeeded' and after['stock']>before['stock'],r
 assert new-old==4,(old,new)
 again=tool('work.run',actor_id=actor['id'],goal='wood',count=1,stock_target=before['stock']+1)
 assert again['status']=='succeeded' and again['stop_reason']=='shared_stock_target_already_met',again
 assert next(a for a in b.state()['actors'] if a['id']==actor['id'])['labor']['used']==new
 print('PASS real partner wood collection adds native cargo and charges exactly four labor points',flush=True)
 print('PASS repeated satisfied stock target creates no extra action or labor charge',flush=True)
finally:sc('agent_pause');sc('agent_ui')
