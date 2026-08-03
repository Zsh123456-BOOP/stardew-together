"""AgentLab only: native machine input, item conservation and cancelled cargo recovery."""
from pathlib import Path
import json,sys,time,uuid
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,ROOT

def run():
    b=Bridge();s=b.state();assert s['player']['name']=='AgentLab' and s['location']=='Farm'
    def actor():return next(a for a in b.state()['actors'] if a['name']=='Abigail')
    def start(skill):
        a=actor();target=next(c for c in a['candidates'] if c['skill']==skill)
        body=dict(session_id=b.session,command_id=uuid.uuid4().hex,actor_id=a['id'],skill=skill,target_id=target['target_id'])
        return body,b.request('POST','/commands',body)
    def finish(r):
        deadline=time.monotonic()+40
        while r['status']=='running':
            assert time.monotonic()<deadline,r
            time.sleep(.1);r=b.request('GET','/commands/'+r['command_id'])
        assert r['status']=='succeeded',r
        return r
    def storage(role):return next(c['items'] for c in b.state()['storage'] if c['role']==role)
    b.reset_lab();player_before=b.state()['player']['inventory'];body,r=start('refill');r=finish(r)
    assert r['evidence']['resource_changes']=={'(O)378:0':-5,'(O)382:0':-1},r
    assert r['evidence']['pickup_tile'] is not None and r['evidence']['actor_before']!=r['evidence']['actor_after'],r
    assert r['evidence']['processing_output']=='(O)334' and r['evidence']['caught_items']==[],r
    assert storage('supplies')=={'(O)378:0':5,'(O)382:0':1} and actor()['cargo']=={}
    assert b.state()['player']['inventory']==player_before
    repeat=b.request('POST','/commands',body);assert repeat==r
    assert storage('supplies')=={'(O)378:0':5,'(O)382:0':1}
    complete=r
    b.reset_lab();_,r=start('refill');deadline=time.monotonic()+30
    while not actor()['cargo']:
        assert time.monotonic()<deadline
        time.sleep(.08)
    cancelled=b.request('POST','/commands/'+r['command_id']+'/cancel',{'session_id':b.session})
    assert cancelled['status']=='cancelled',cancelled
    assert actor()['cargo']=={'(O)378:0':5,'(O)382:0':1}
    deposits=[]
    while actor()['cargo']:
        _,r=start('deposit');deposits.append(finish(r))
        assert len(deposits)<=2
    assert storage('output')=={'(O)378:0':5,'(O)382:0':1}
    assert storage('supplies')=={'(O)378:0':5,'(O)382:0':1}
    assert b.request('GET','/commands/'+cancelled['command_id'])==cancelled
    report={'passed':True,'checks':['walk_to_source_and_machine','native_machine_consumes_exact_recipe','idempotent_receipt','cancel_keeps_real_cargo','deposit_conserves_cancelled_materials'],
        'refill':complete,'cancel':cancelled,'deposits':deposits}
    (ROOT/'outputs/resource-flow-report.json').write_text(json.dumps(report,indent=2))
    print(json.dumps(report,ensure_ascii=False))

if __name__=='__main__':run()
