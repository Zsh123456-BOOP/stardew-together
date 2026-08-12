"""Regression for continuous native movement across the house boundary, AgentLab only."""
from pathlib import Path
import json,time,sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
records=[]
def t(n,**args):return b.request('POST','/lab/together',{'session_id':b.session,'scenario':'agent_tool','tool':n,'args':args})
for location in ['Farm','FarmHouse']*3:
    time.sleep(1)
    r=t('player.travel',location=location);end=time.monotonic()+45
    while r['status']=='running' and time.monotonic()<end:
        time.sleep(.15);r=t('action.status',id=r['command_id'])
    records.append(r);Path('work/autoplay-transit-checks.json').write_text(json.dumps(records,ensure_ascii=False,indent=2))
    print(location,r['status'],r.get('error'),flush=True)
    assert r['status']=='succeeded' and r['after']['location']==location
