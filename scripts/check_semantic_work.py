"""Combined native semantic skills regression. Explicit AgentLab fixtures; no LLM."""
from pathlib import Path
import json,time,sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,BridgeError
b=Bridge();assert b.state()['player']['name']=='AgentLab'
checks=[]
def scenario(name,**kw):return b.request('POST','/lab/together',{'session_id':b.session,'scenario':name,**kw})
def tool(name,**args):return scenario('agent_tool',tool=name,args=args)
def diag():return b.request('GET','/lab/together')['autoplay']
def check(ok,label,detail=None):
    checks.append({'pass':bool(ok),'check':label,'detail':detail});Path('work/semantic-checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
def wait(r,timeout=160):
    end=time.monotonic()+timeout
    while r['status']=='running' and time.monotonic()<end:
        time.sleep(.25);r=tool('action.status',id=r['command_id'])
    if r['status']=='running':tool('action.cancel',id=r['command_id'])
    return r
try:
    scenario('agent_semantic_fixture');time.sleep(2);a=tool('world.read')['companions'][0]['id']
    scenario('agent_schedule_probe')
    tool('plan.submit',submission_id='semantic-batch',expected_revision=tool('plan.read')['revision'],tasks=[
        {'id':'wood','actor':'player','tool':'work.run','args':{'goal':'wood','count':12}},
        {'id':'water','actor':'player','tool':'work.run','args':{'goal':'water','count':6}},
        {'id':'stone','actor':a,'tool':'work.run','args':{'actor_id':a,'goal':'stone','location':'Farm','count':3}}])
    end=time.monotonic()+180;overlap=False
    while time.monotonic()<end:
        d=diag();tasks=d['state']['Schedule']['Tasks'];active={t['spec']['actor'] for t in tasks if t['state']=='running'};overlap|=len(active)==2
        if all(t['state'] not in ('queued','running') for t in tasks):break
        time.sleep(.3)
    results={t['spec']['id']:json.loads(t['receipt']) if t.get('receipt') else {'status':t['state']} for t in tasks}
    check(results['wood'].get('gained',0)>=12 and results['wood']['status']=='succeeded','one wood goal collects twelve actual wood without coordinates',results['wood'])
    check(results['water']['status']=='succeeded' and results['water'].get('completed',0)>=6 and results['water'].get('refills',0)>=1,'one water goal refills during work and continues six crops',results['water'])
    check(results['stone']['status']=='succeeded' and results['stone'].get('gained',0)>=3,'NPC autonomously selects multiple rocks until actual stone goal',results['stone'])
    check(overlap and d['state']['Decisions']==0,'both actors progress independently without per-target LLM calls')
    scenario('agent_pause')
    r=wait(tool('work.run',goal='wood',count=1,reserve_stamina=270))
    check(r['status']=='failed' and r.get('stop_reason')=='energy_reserve_reached','energy reserve stops work without claiming completion',r)
    r=tool('work.run',goal='wood',count=999)
    try:tool('player.move',x=62,y=17);blocked=False
    except BridgeError as e:blocked='actor_owned_by_work_job' in str(e)
    tool('action.cancel',id=r['command_id']);check(blocked,'semantic job owns actor across gaps between native actions')
    scenario('agent_full_inventory_fixture');time.sleep(1)
    before=tool('inventory.read');r=wait(tool('work.run',goal='wood',count=2));after=tool('inventory.read')
    check(r['status']=='succeeded' and r.get('deposited',0)>0 and r.get('gained',0)>=2,'full player inventory automatically stores and resumes original gathering',r)
    check(after['items'][5]['item'].get('id')=='(O)472' and after['items'][5]['item'].get('count')==10 and after['items'][6]['item'].get('count',0)>=2,'automatic storage retains seeds and food reserve',after)
    r=wait(tool('work.run',actor_id=a,goal='stone',location='Farm',count=1))
    check(r['status']=='succeeded' and any(e.get('kind')=='storage_deposit' for e in r.get('evidence',[])),'full NPC cargo automatically deposits then resumes mining',r)
    scenario('agent_empty_can_fixture');r=wait(tool('work.run',goal='refill'))
    check(r['status']=='succeeded' and r.get('refills',0)==1,'standalone refill needs no source coordinate or tool slot',r)
finally:scenario('agent_pause')
print('COMPLETED',len(checks),'checks; failures',sum(not c['pass'] for c in checks),flush=True)
sys.exit(1 if any(not c['pass'] for c in checks) else 0)
