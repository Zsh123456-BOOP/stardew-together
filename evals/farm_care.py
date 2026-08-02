"""Real animal/machine effects in the dedicated AgentLab; no simulated success."""
from pathlib import Path
import sys,json,time,uuid
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,ROOT

def run():
    b=Bridge();b.reset_lab();results=[]
    for skill in ('pet','collect'):
        for attempt in range(4):
            a=next(a for a in b.state()['actors'] if a['name']=='Abigail')
            targets=[c for c in a['candidates'] if c['skill']==skill]
            assert targets,('capability unavailable',skill,a['candidates'])
            r=b.request('POST','/commands',dict(session_id=b.session,command_id=uuid.uuid4().hex,actor_id=a['id'],skill=skill,target_id=targets[0]['target_id']))
            deadline=time.monotonic()+30
            while r['status']=='running':
                assert time.monotonic()<deadline,r
                time.sleep(.1);r=b.request('GET','/commands/'+r['command_id'])
            if r['status']=='succeeded':break
            assert skill=='pet' and r['error']=='target_moved',r
        assert r['status']=='succeeded' and r['evidence']['effect_by_actor'],r
        assert not [c for c in next(a for a in b.state()['actors'] if a['name']=='Abigail')['candidates'] if c['skill']==skill]
        results.append(r)
    report={'passed':True,'checks':['animal_actual_pet','machine_actual_collection'],'actions':results}
    (ROOT/'outputs/farm-care-report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2))
    print(json.dumps(report,ensure_ascii=False,indent=2))
if __name__=='__main__':run()
