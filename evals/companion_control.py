"""Real Squad integration checks; only run in an AgentLab test save on Farm."""
from pathlib import Path
import json
import sys
import time
import uuid
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,BridgeError,ROOT


def actor(state,name='Abigail'):
    return next(a for a in state['actors'] if a['name']==name)


def command(b,a,target):
    return {'session_id':b.session,'command_id':uuid.uuid4().hex,'actor_id':a['id'],
            'skill':'mine','target_id':target['target_id']}


def wait(b,result):
    deadline=time.monotonic()+35
    while result['status']=='running':
        assert time.monotonic()<deadline,'command did not terminate'
        time.sleep(.1)
        result=b.request('GET','/commands/'+result['command_id'])
    return result


def expect_error(b,request,expected):
    try: b.request('POST','/commands',request)
    except BridgeError as e: assert str(e)==expected,(str(e),expected)
    else: raise AssertionError('Expected rejection: '+expected)


def run():
    b=Bridge(); b.reset_lab(); state=b.state(); a=actor(state)
    target=next(c for c in a['candidates'] if c['tile']==[49,18])
    other=actor(state,'Leah')
    distance=lambda who: sum((who['tile'][i]-target['tile'][i])**2 for i in range(2))
    assert distance(other)<distance(a),'fixture should place Leah closer than Abigail'
    cmd=command(b,a,target)
    result=wait(b,b.request('POST','/commands',cmd))
    assert result['status']=='succeeded',result
    assert result['actor_id']==a['id'] and result['evidence']['mined_by_actor']
    assert not result['evidence']['target_remaining']
    assert result['evidence']['actor_before']!=result['evidence']['actor_after']
    assert b.request('POST','/commands',cmd)==result,'retry changed terminal evidence'
    expect_error(b,dict(cmd,skill='follow'),'command_id_conflict')
    expect_error(b,dict(cmd,command_id=uuid.uuid4().hex,session_id='old'),'stale_session')
    expect_error(b,dict(cmd,command_id=uuid.uuid4().hex),'stale_target')
    follow=b.request('POST','/commands',{'session_id':b.session,'command_id':uuid.uuid4().hex,'actor_id':a['id'],'skill':'follow'})
    assert follow['status']=='succeeded' and follow['evidence']['follow_mode_enabled']
    b.reset_lab(); fresh=actor(b.state())
    new_target=next(c for c in fresh['candidates'] if c['tile']==[49,18])
    cancel_cmd=command(b,fresh,new_target)
    b.request('POST','/commands',cancel_cmd)
    cancelled=b.request('POST','/commands/'+cancel_cmd['command_id']+'/cancel',{'session_id':b.session})
    assert cancelled['status']=='cancelled' and not cancelled['evidence']['mined_by_actor'],cancelled
    time.sleep(.5)
    after=actor(b.state())
    assert after['task'] is None
    assert any(c['target_id']==new_target['target_id'] for c in after['candidates'])
    report={'passed':True,'checks':['specified_actor_despite_closer_other','real_movement_and_mining',
        'immutable_idempotent_result','conflicting_id_rejected','stale_session_rejected',
        'stale_target_rejected','follow_mode','cancel_without_mining'],
        'mine':result,'cancel':cancelled,'follow':follow}
    output=ROOT/'outputs/companion-control-report.json'; output.parent.mkdir(exist_ok=True)
    output.write_text(json.dumps(report,ensure_ascii=False,indent=2))
    print(json.dumps(report,ensure_ascii=False,indent=2))


if __name__=='__main__': run()
