"""Combined real-game scheduler contract, AgentLab only. Not an LLM gameplay result."""
from pathlib import Path
import json,sys,time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,BridgeError
b=Bridge();assert b.state()['player']['name']=='AgentLab'
results=[]
def scenario(name,**kw):return b.request('POST','/lab/together',{'session_id':b.session,'scenario':name,**kw})
def tool(name,**args):return scenario('agent_tool',tool=name,args=args)
def diag():return b.request('GET','/lab/together')['autoplay']
def check(ok,name,detail=None):
    results.append({'pass':bool(ok),'check':name,'detail':detail});print(('PASS ' if ok else 'FAIL ')+name,flush=True)
    Path('work/autoplay-scheduler-checks.json').write_text(json.dumps(results,ensure_ascii=False,indent=2));assert ok,name
scenario('agent_fixture');time.sleep(1)
w=tool('world.read');actors=w['companions'];assert actors,'recruited companion required for independent lane check'
a=actors[0]['id'];day=w['snapshot']['day']
# Guard is deliberately longer than the player's farming chain to expose global barriers.
plan=[{'id':'npc-guard','actor':a,'tool':'companion.assign','args':{'actor_id':a,'skill':'guard','seconds':45}},
      {'id':'till','actor':'player','tool':'player.work','args':{'skill':'till','slot':1,'tiles':[{'x':61,'y':18},{'x':62,'y':18}]},'location':'Farm'},
      {'id':'plant','actor':'player','tool':'player.work','args':{'skill':'plant','slot':5,'tiles':[{'x':61,'y':18},{'x':62,'y':18}]},'location':'Farm','after':['till']},
      {'id':'water','actor':'player','tool':'player.work','args':{'skill':'water','slot':2,'tiles':[{'x':61,'y':18},{'x':62,'y':18}]},'location':'Farm','after':['plant']}]
# Gated probe runs the same dispatch loop without pretending a model planned this fixture.
scenario('agent_schedule_probe')
try:
    rev=tool('plan.read')['revision'];r=tool('plan.submit',submission_id='parallel-farm',expected_revision=rev,tasks=plan)
    retry=tool('plan.submit',submission_id='parallel-farm',expected_revision=rev,tasks=plan)
    check(retry['status']=='already_submitted','repeated submission does not duplicate real work')
    end=time.monotonic()+35;overlap=False
    while time.monotonic()<end:
        d=diag();tasks=d['state']['Schedule']['Tasks'];states={t['spec']['id']:t['state'] for t in tasks}
        overlap|=states.get('npc-guard')=='running' and states.get('water') in ('running','succeeded')
        if states.get('water')=='succeeded':break
        time.sleep(.2)
    check(states.get('water')=='succeeded','one submission completes till, plant and water sequentially',states)
    check(overlap,'player third task starts while companion first task is still running')
    check(d['state']['Decisions']==0,'scheduler advances known steps without per-step model calls')
    tool('plan.cancel',ids=['npc-guard'])
    bad=[{'id':'bad-map','actor':'player','tool':'player.move','args':{'x':1,'y':1},'location':'DefinitelyNotThisMap'},
         {'id':'dependent','actor':'player','tool':'player.move','args':{'x':62,'y':17},'after':['bad-map']},
         {'id':'npc-independent','actor':a,'tool':'companion.assign','args':{'actor_id':a,'skill':'guard','seconds':10}}]
    tool('plan.submit',submission_id='failure-isolation',expected_revision=tool('plan.read')['revision'],tasks=bad)
    time.sleep(1)
    tasks=diag()['state']['Schedule']['Tasks'];states={t['spec']['id']:t['state'] for t in tasks}
    check(states['bad-map']=='failed' and states['dependent']=='blocked' and states['npc-independent']=='running','wrong map blocks dependents while companion keeps working',states)
    scenario('agent_pause');tasks=diag()['state']['Schedule']['Tasks'];check(next(t for t in tasks if t['spec']['id']=='npc-independent')['state']=='needs_review','pause retains unfinished task for review, not blind replay')
    # Both lanes must also perform real production; guard/follow alone is not labor.
    scenario('agent_resource_fixture');time.sleep(1)
    r=tool('companion.assign',actor_id=a,skill='travel',destination='Farm');end=time.monotonic()+40
    while r['status']=='running' and time.monotonic()<end:
        time.sleep(.2);r=tool('action.status',id=r['command_id'])
    check(r['status']=='succeeded','companion independently reaches work map')
    actor=tool('world.read')['companions'][0]
    rock=next(c for c in actor['candidates'] if c['skill']=='mine' and c['tile']==[63,18])
    scenario('agent_schedule_probe')
    tool('plan.submit',submission_id='both-produce',expected_revision=tool('plan.read')['revision'],tasks=[
        {'id':'npc-mine','actor':a,'tool':'companion.assign','args':{'actor_id':a,'skill':'mine','target_id':rock['target_id']}},
        {'id':'player-wood','actor':'player','tool':'player.work','location':'Farm','args':{'skill':'clear','slot':0,'tiles':[{'x':x,'y':y} for y in range(18,24) for x in (67,68)]}}
    ])
    end=time.monotonic()+60;overlap=False
    while time.monotonic()<end:
        tasks=diag()['state']['Schedule']['Tasks'];states={t['spec']['id']:t['state'] for t in tasks}
        overlap |= states.get('npc-mine')=='running' and states.get('player-wood')=='running'
        if all(s in ('succeeded','failed','blocked') for s in states.values()):break
        time.sleep(.1)
    check(overlap and all(s=='succeeded' for s in states.values()),'NPC mines while Farmer gathers twelve wood, both with verified receipts',tasks)
finally:scenario('agent_pause')
print('COMPLETED',len(results),'scheduler checks',flush=True)
