"""Grouped native admission/supply contracts, isolated AgentLab only; no LLM."""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge(); assert b.state()['player']['name']=='AgentLab'
out=Path(sys.argv[1] if len(sys.argv)>1 else 'work/operations-kernel-native');out.mkdir(parents=True,exist_ok=True)
checks=[]
def sc(name,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=name,**kw))
def tool(name,**kw):return sc('agent_tool',tool=name,args=kw)
def read():return b.request('GET','/lab/together')['autoplay']
def check(value,label):
    checks.append(dict(passed=bool(value),label=label));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2))
    print(('PASS ' if value else 'FAIL ')+label,flush=True);assert value,label
def poll(result):
    end=time.monotonic()+150
    while result['status']=='running' and time.monotonic()<end:
        time.sleep(.3);result=tool('action.status',id=result['command_id'])
    return result
try:
    sc('door_interaction_fixture',time=600);time.sleep(2);sc('agent_schedule_probe')
    day=read()['snapshot']['day'];revision=tool('plan.read')['revision']
    tool('plan.submit',submission_id='future-shop',expected_revision=revision,tasks=[dict(id='future-shop',tool='player.service',args=dict(location='SeedShop',service='shop',shop='SeedShop'),purpose='observe shop after opening',day=day)])
    tool('plan.submit',submission_id='independent-work',expected_revision=tool('plan.read')['revision'],tasks=[dict(id='independent-work',tool='player.travel',args=dict(location='Farm'),purpose='independent useful travel',day=day)])
    end=time.monotonic()+120;observed=None
    while time.monotonic()<end:
        d=read();tasks={t['spec']['id']:t for t in d['state']['Schedule']['Tasks']}
        if tasks['independent-work']['state'] in ('succeeded','failed','blocked'):observed=d;break
        time.sleep(.3)
    check(observed is not None and tasks['independent-work']['state']=='succeeded','future shop does not block independent native travel')
    check(tasks['future-shop']['state']=='queued' and tasks['future-shop']['spec']['not_before']>=900,'native opening metadata prevents premature shop entry')
    (out/'window-evidence.json').write_text(json.dumps(observed,ensure_ascii=False,indent=2))
    check(observed['state']['Decisions']==0,'window scheduling requires no model calls')
    sc('agent_pause');sc('inventory_timing_fixture');time.sleep(2)
    tool('farm.business',enabled=True,expand=False,budget_per_day=0,keep_gold=100)
    check(sc('inventory_timing_read')['free_slots']==1,'supply fixture starts with one free slot')
    result=poll(tool('work.run',goal='store',required_free_slots=2));state=sc('inventory_timing_read')
    (out/'supply.json').write_text(json.dumps(dict(receipt=result,state=state),ensure_ascii=False,indent=2))
    check(result['status']=='succeeded' and state['free_slots']>=2 and state['stored']>0,'downstream capacity prepares space before inventory is full')
    result=poll(tool('work.run',goal='store',required_free_slots=12))
    (out/'impossible-capacity.json').write_text(json.dumps(result,ensure_ascii=False,indent=2))
    check(result['status']=='failed' and result['stop_reason']=='inventory_contains_only_protected_items','impossible capacity reports a blocker instead of false success or a loop')
finally:
    sc('agent_pause');sc('agent_ui')
