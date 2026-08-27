"""Read native crops after the grouped bed check; verify cleanup's farm allowance.
No new materials, warps, clock changes or LLM calls. Leaves AI paused.
"""
import json
import sys
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge(); assert b.state()['player']['name']=='AgentLab'
def tool(name, **args):
    return b.request('POST','/lab/together',dict(session_id=b.session,scenario='agent_tool',tool=name,args=args))
checks=[]
def check(ok,label):
    checks.append(dict(passed=bool(ok),label=label)); print(('PASS ' if ok else 'FAIL ')+label,flush=True)
def allowance(id):
    return next(o['allowance'] for o in tool('farm.maintenance')['summary']['orders'] if o['Id']==id)
tool('farm.cleanup',request_id='budget-proof',daily_limit=30)
initial=allowance('budget-proof')
world=tool('world.read')
check(initial['FarmEnergy']==world['farm']['DryCrops']*2,'actual remaining dry crops reserve energy; already watered bed is not charged twice')
tool('farm.autonomy',enabled=True,plots=15,max_daily_manual_water=15)
planned=allowance('budget-proof')
check(planned['FarmEnergy']==initial['FarmEnergy']+60,'agreed future planting reserves energy before the shop or planner runs')
check(planned['Reserve']==initial['Reserve']+60 and planned['Available']<=initial['Available'],'planting reserve reduces optional cleanup allowance')
tool('farm.cleanup',request_id='budget-proof-second',daily_limit=30)
check(allowance('budget-proof-second')==planned,'new cleanup order shares the same farm and daily allowance')
tool('farm.autonomy',enabled=False)
check(allowance('budget-proof')==initial,'disabling unstarted investment releases only its future planting reserve')
for id in ('budget-proof','budget-proof-second'): tool('farm.cleanup',request_id=id,mode='pause')
Path('work/cleanup-business-budget-results.json').write_text(json.dumps(dict(checks=checks,initial=initial,planned=planned),ensure_ascii=False,indent=2))
raise SystemExit(0 if all(c['passed'] for c in checks) else 1)
