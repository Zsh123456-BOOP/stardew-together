"""Grouped real-action bed preparation check on an explicit AgentLab fixture."""
import json
import sys
import time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path('work/farm-bed-native');out.mkdir(exist_ok=True)
def scenario(name,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=name,**kw))
def tool(name,**kw):return scenario('agent_tool',tool=name,args=kw)
checks=[]
def check(ok,label,detail=None):
    checks.append(dict(passed=bool(ok),label=label,detail=detail))
    (out/'results.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2))
    print(('PASS ' if ok else 'FAIL ')+label,flush=True)
scenario('agent_fixture');time.sleep(1);scenario('plant_bed_fixture');time.sleep(1)
start=tool('inventory.read')
economy=tool('farm.economy',location='Farm',budget=0,keep_gold=0,plots=15,max_daily_manual_water=15)
end=time.monotonic()+20
while time.monotonic()<end:
    economy_result=tool('farm.economy_status',plan_id=economy['plan_id'])
    if economy_result.get('status')=='planned':break
    time.sleep(.2)
(out/'economy.json').write_text(json.dumps(economy_result,ensure_ascii=False,indent=2))
plants=economy_result.get('result',{}).get('Plants',[])
check(len(plants)==15,'economic planner includes a whole debris-covered bed',economy_result)
plan=tool('farm.plan',seed='(O)472',count=15,max_daily_manual_water=15)
(out/'plan.json').write_text(json.dumps(plan,ensure_ascii=False,indent=2))
option=plan['options'][0];tiles=option['tiles'];selected={(t['X'],t['Y']) for t in tiles}
check(any(z['reason']=='home_courtyard' for z in plan.get('zoning',[])),'native farm plan reserves the home courtyard and roads',plan.get('zoning'))
check(len(tiles)==15 and len({t['X'] for t in tiles})*len({t['Y'] for t in tiles})==15,'manual planner uses the same compact rectangular bed',option)
value=tool('work.run',goal='plant',plan_id=option['plan_id'],reserve_stamina=20)
end=time.monotonic()+300;saw_plant=False;ordered=True;clear_before_till=True;samples=[]
while value['status']=='running' and time.monotonic()<end:
    state=scenario('plant_bed_read');cells=[c for c in state['cells'] if (c['x'],c['y']) in selected]
    planted=sum(c['planted'] for c in cells)
    if any(c['tilled'] for c in cells):clear_before_till=clear_before_till and not any(c['obstacle'] for c in cells)
    if planted:
        saw_plant=True;ordered=ordered and all(c['tilled'] and not c['obstacle'] for c in cells)
    samples.append(dict(phase=value['phase'],planted=planted,obstacles=sum(c['obstacle'] for c in cells),tilled=sum(c['tilled'] for c in cells)))
    time.sleep(.25);value=tool('action.status',id=value['command_id'])
if value['status']=='running':tool('action.cancel',id=value['command_id'])
(out/'receipt.json').write_text(json.dumps(value,ensure_ascii=False,indent=2));(out/'samples.json').write_text(json.dumps(samples,ensure_ascii=False))
state=scenario('plant_bed_read')
check(value['status']=='succeeded','clear, till, sow, refill and water complete without LLM child decisions',value)
check(clear_before_till,'all planned obstacles cleared before any tilling')
check(saw_plant and ordered,'no seed consumed before the entire bed is cleared and tilled')
check(all(c['planted'] and c['watered'] and not c['obstacle'] for c in state['cells'] if (c['x'],c['y']) in selected),'all 15 native crops exist and are watered',state)
check(state['tree'] and state['chest'],'surrounding tree and chest preserved')
check(value.get('refills',0)>0,'empty watering can recovered through native refill')
remaining=sum(i['item'].get('count',0) for i in tool('inventory.read')['items'] if i['item'].get('id')=='(O)472')
check(remaining==0,'exactly 15 seeds consumed')
receipts=[]
for e in value.get('evidence',[]):
    try:receipts.append(tool('action.status',id=e['command_id']))
    except Exception:pass
(out/'child-actions.json').write_text(json.dumps(receipts,ensure_ascii=False,indent=2))
check(all(r.get('status')=='succeeded' for r in receipts),'movement and tool child actions all finish without path failure')
scenario('agent_pause')
raise SystemExit(0 if all(c['passed'] for c in checks) else 1)
