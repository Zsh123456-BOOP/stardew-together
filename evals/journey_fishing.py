"""AgentLab only: NPC walks from Farm to Beach and fishes while player stays home."""
from pathlib import Path
import sys,time,uuid,json
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,ROOT

def run():
    b=Bridge();s=b.state();assert s['player']['name']=='AgentLab' and s['location']=='Farm'
    b.reset_lab();a=next(a for a in b.state()['actors'] if a['name']=='Abigail');actions=[];maps=[]
    for skill,extra in [('travel',{'destination':'Beach'}),('fish',{'seconds':10})]:
        r=b.request('POST','/commands',dict(session_id=b.session,command_id=uuid.uuid4().hex,actor_id=a['id'],skill=skill,**extra))
        if skill=='fish':
            m=b.map(a['id']);tile=r['evidence']['interaction_tile']
            assert tile and 0<=tile[0]<m['width'] and 0<=tile[1]<m['height'],r
        end=time.monotonic()+100
        while r['status']=='running' and time.monotonic()<end:
            time.sleep(.2);r=b.request('GET','/commands/'+r['command_id'])
            s=b.state();a=next(a for a in s['actors'] if a['name']=='Abigail')
            if not maps or maps[-1]!=a['location']:maps.append(a['location'])
        if r['status']=='running':b.request('POST','/commands/'+r['command_id']+'/cancel',{'session_id':b.session})
        assert r['status']=='succeeded',r
        actions.append(r);print(skill,'succeeded',flush=True)
    s=b.state();a=next(a for a in s['actors'] if a['name']=='Abigail')
    assert a['location']=='Beach' and s['location']=='Farm'
    assert actions[1]['evidence']['activity_seconds']>=10
    report={'passed':True,'checks':['normal_exit_journey','in_bounds_fishing_spot','offscreen_fishing_duration'],'maps':maps,'actions':actions}
    (ROOT/'outputs/journey-fishing-report.json').write_text(json.dumps(report,indent=2));print(json.dumps(report))
if __name__=='__main__':run()
