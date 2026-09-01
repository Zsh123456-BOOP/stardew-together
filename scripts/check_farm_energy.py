"""Prepared trellis aisles, first-day energy and optional-work reservation. AgentLab only."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path(sys.argv[1] if len(sys.argv)>1 else 'work/farm-energy-native');out.mkdir(exist_ok=True);checks=[]
def sc(code,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**kw))
def tool(code,**kw):return sc('agent_tool',tool=code,args=kw)
def check(ok,label):
 checks.append(dict(passed=bool(ok),label=label));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
 if not ok:raise AssertionError(label)
def action(code,**kw):
 r=tool(code,**kw);end=time.monotonic()+150
 while r['status']=='running' and time.monotonic()<end:time.sleep(.2);r=tool('action.status',id=r['command_id'])
 return r
try:
 for energy in (60,270):
  sc('farm_energy_fixture',stamina=energy);time.sleep(3);sc('agent_schedule_probe');tool('day.routine',enabled=False)
  req=tool('farm.economy',location='Farm',budget=0,keep_gold=0,plots=8,max_daily_manual_water=8)
  end=time.monotonic()+30
  while time.monotonic()<end:
   result=tool('farm.economy_status',plan_id=req['plan_id'])
   if result['status']=='planned':break
   time.sleep(.2)
  (out/f'plan-{energy}.json').write_text(json.dumps(result,ensure_ascii=False,indent=2));plan=result['result'];prep={(t['X'],t['Y']) for t in plan['PreparationTiles']}
  check(0<len(plan['Plants'])<=8 and plan['FirstDayEnergy']<=energy-20,f'{energy} stamina portfolio fits clearing, tilling and watering while preserving reserve')
  submitted=tool('farm.execute',plan_id=req['plan_id']);ids=set(submitted['tasks']);end=time.monotonic()+300;clear_before_seed=True
  while time.monotonic()<end:
   view=sc('farm_energy_read');objects={(int(p['x']),int(p['y'])) for p in view['objects']}
   if view['crops']:clear_before_seed &= not bool(prep&objects)
   tasks=[t for t in b.request('GET','/lab/together')['autoplay']['state']['Schedule']['Tasks'] if t['spec']['id'] in ids]
   if tasks and all(t['state'] in ['failed','blocked','succeeded','cancelled'] for t in tasks):break
   time.sleep(.25)
  (out/f'tasks-{energy}.json').write_text(json.dumps(tasks,ensure_ascii=False,indent=2));(out/f'final-{energy}.json').write_text(json.dumps(view,ensure_ascii=False,indent=2))
  check(all(t['state']=='succeeded' for t in tasks),f'{energy} stamina mixed planting completes without a model or path failure')
  check(clear_before_seed,f'{energy} stamina complete prepared footprint is cleared before any seed is consumed')
  check(len(view['crops'])==len(plan['Plants']) and all(p['watered'] for p in view['crops']) and view['stamina']>=20,f'{energy} stamina actual crops are all watered with reserve intact')
  r=action('player.travel',location='FarmHouse');(out/f'exit-{energy}.json').write_text(json.dumps(r,ensure_ascii=False,indent=2))
  check(r['status']=='succeeded',f'{energy} stamina Farmer can leave the final trellis layout and return home')
 sc('farm_energy_fixture',stamina=120,hold=1);time.sleep(3)
 before=sc('farm_energy_read');r=action('work.run',actor_id='player',goal='wood',count=1);after=sc('farm_energy_read')
 (out/'optional-work.json').write_text(json.dumps(dict(before=before,receipt=r,after=after),ensure_ascii=False,indent=2))
 check(before['pending']>0 and r.get('error')=='energy_reserve_reached' and after['stamina']==before['stamina'],'optional wood collection cannot spend the held farming energy')
finally:
 sc('agent_pause');sc('agent_ui')
