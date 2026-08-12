"""Regression for continuous native movement across the house boundary, AgentLab only."""
from pathlib import Path
import json,time,sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
records=[]
def t(n,**args):return b.request('POST','/lab/together',{'session_id':b.session,'scenario':'agent_tool','tool':n,'args':args})
# Deliberate round trips are requested here, never unexplained AI reversals.
for location in ['Farm','FarmHouse']*3:
    time.sleep(1)
    r=t('player.travel',location=location);end=time.monotonic()+45
    while r['status']=='running' and time.monotonic()<end:
        time.sleep(.15);r=t('action.status',id=r['command_id'])
    records.append(r);Path('work/autoplay-transit-checks.json').write_text(json.dumps(records,ensure_ascii=False,indent=2))
    print(location,r['status'],r.get('error'),flush=True)
    assert r['status']=='succeeded' and r['after']['location']==location

# Regression for the reported NPC-dependent exit: hold the NPC inside, walk out once.
a=t('world.read')['companions'][0]
r=t('companion.assign',actor_id=a['id'],skill='travel',destination='FarmHouse')
end=time.monotonic()+45
while r['status']=='running' and time.monotonic()<end:
    time.sleep(.15);r=t('action.status',id=r['command_id'])
assert r['status']=='succeeded',r
rest=t('companion.assign',actor_id=a['id'],skill='rest',seconds=60)
try:
    r=t('player.travel',location='Farm');end=time.monotonic()+40;locations=[]
    while r['status']=='running' and time.monotonic()<end:
        time.sleep(.1);d=t('world.read');loc=d['snapshot']['location']
        if not locations or locations[-1]!=loc:locations.append(loc)
        r=t('action.status',id=r['command_id'])
    assert r['status']=='succeeded',r
    for _ in range(20):
        time.sleep(.2);w=t('world.read')
        assert w['snapshot']['location']=='Farm','unrequested door reentry'
        assert w['companions'][0]['location']=='FarmHouse','NPC must remain inside'
    assert locations.count('Farm')==1,locations
    records.append({'check':'player exits independently while NPC rests inside; no reentry for four seconds','pass':True,'locations':locations})
    Path('work/autoplay-transit-checks.json').write_text(json.dumps(records,ensure_ascii=False,indent=2))
    print('PASS independent exit without NPC arrival',flush=True)
finally:t('action.cancel',id=rest['command_id'])
