"""Batch: empty-can recovery, remote NPC map, and actual mine entrance; AgentLab only."""
from pathlib import Path
import json,time,sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
def scenario(name,**args):return b.request('POST','/lab/together',{'session_id':b.session,'scenario':name,**args})
def tool(name,**args):return scenario('agent_tool',tool=name,args=args)
checks=[]
def check(ok,label,detail=None):
    checks.append({'pass':bool(ok),'check':label,'detail':detail})
    Path('work/autoplay-supplies-checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2))
    print(('PASS ' if ok else 'FAIL ')+label,flush=True);assert ok,label
def wait(r,seconds=120):
    end=time.monotonic()+seconds
    while r['status']=='running' and time.monotonic()<end:
        time.sleep(.2);r=tool('action.status',id=r['command_id'])
    check(r['status']=='succeeded',r.get('skill','action'),r);return r
scenario('agent_fixture');time.sleep(2);scenario('agent_empty_can_fixture')
w=tool('world.read');a=w['companions'][0]['id']
npc=tool('companion.assign',actor_id=a,skill='travel',destination='Mine')
day=tool('day.read');water=day['watering'];check(water['water_left']==0 and water['refill_options'],'empty can exposes reachable native water source',water)
option=water['refill_options'][0]
wait(tool('player.move',**option['move']))
r=wait(tool('player.use_tool',**option['use_tool']))
item=tool('inventory.read')['items'][water['slot']]['item'];check(item['water_left']>0,'actual tool use refills actual can',item)
wait(npc)
remote=tool('map.read',actor_id=a,radius=8);check(remote['location']=='Mine' and tool('world.read')['snapshot']['location']=='Farm','remote NPC map remains independent of player map',remote['location'])
actor=next(x for x in tool('world.read')['companions'] if x['id']==a)
entry=next(x for x in actor['travel_options'] if x['kind']=='mine_entrance')
check(entry['destination']=='UndergroundMine1','real entrance exposes first-floor destination',entry)
wait(tool('companion.assign',actor_id=a,skill='travel',destination=entry['destination']))
remote=tool('map.read',actor_id=a);check(remote['location']=='UndergroundMine1','NPC enters actual mine floor through entrance',remote['location'])
roadmap=tool('progress.roadmap');check('missing_achievements' in roadmap and 'native' in roadmap and len(roadmap['next_steps'])>=3,'roadmap includes native evidence and explicitly labelled recommendations')
print('COMPLETED',len(checks),'checks',flush=True)
