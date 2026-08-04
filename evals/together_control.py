"""Real farming + recruitment regression. Requires --lab and the AgentLab Farm.
Close the Together panel and stop Together jobs before running this check.
"""
import json
from pathlib import Path
import sys
import time
import uuid
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge, BridgeError, ROOT


def run():
    b=Bridge();b.reset_lab();results=[]
    def actor(name='Abigail'):
        return next(a for a in b.state()['actors'] if a['name']==name)
    def send(actor_id,skill,**fields):
        return b.request('POST','/commands',dict(session_id=b.session,command_id=uuid.uuid4().hex,actor_id=actor_id,skill=skill,**fields))
    for skill in ('water','water','harvest','harvest'):
        a=actor();target=next(t for t in a['candidates'] if t['skill']==skill)
        result=send(a['id'],skill,target_id=target['target_id']);end=time.monotonic()+35
        while result['status']=='running':
            assert time.monotonic()<end,'action timeout'
            time.sleep(.1);result=b.request('GET','/commands/'+result['command_id'])
        assert result['status']=='succeeded' and result['evidence']['effect_by_actor'],result
        assert not any(c['target_id']==target['target_id'] for c in actor()['candidates'])
        results.append(result)
    leah=actor('Leah')
    dismissed=send(leah['id'],'dismiss');end=time.monotonic()+310
    while dismissed['status']=='running':
        assert time.monotonic()<end,'homeward route timeout'
        time.sleep(.2);dismissed=b.request('GET','/commands/'+dismissed['command_id'])
    assert dismissed['status']=='succeeded',dismissed
    assert not any(a['name']=='Leah' for a in b.state()['actors'])
    # The companion walks normal exits to its schedule destination. Remote recruitment must not teleport them back.
    try:send('Leah','recruit',trial=True)
    except BridgeError as e:assert str(e)=='recruitment_unavailable',str(e)
    else:raise AssertionError('remote recruitment should be rejected')
    report={'passed':True,'checks':['water_real_effect_twice','harvest_real_effect_twice','dismiss_returns_npc_home','remote_recruitment_rejected'],'actions':results,'dismiss':dismissed}
    output=ROOT/'outputs/together-control-report.json';output.parent.mkdir(exist_ok=True)
    output.write_text(json.dumps(report,ensure_ascii=False,indent=2));print(json.dumps(report,ensure_ascii=False,indent=2))

if __name__=='__main__':run()
