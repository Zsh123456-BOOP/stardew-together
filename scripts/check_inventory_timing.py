"""Grouped storage and native stow/plant regressions, AgentLab fixtures only."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path('work/inventory-timing');out.mkdir(exist_ok=True);checks=[];receipts=[]
def scenario(code,**args):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**args))
def tool(code,**args):return scenario('agent_tool',tool=code,args=args)
def check(ok,label,detail=None):
 checks.append(dict(passed=bool(ok),label=label,detail=detail));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
 if not ok:raise SystemExit(label)
def fixture(full=0):
 scenario('inventory_timing_fixture',full=full)
 end=time.monotonic()+15
 while time.monotonic()<end:
  time.sleep(.2);snap=b.request('GET','/lab/together')['autoplay']['snapshot']
  if snap['can_move'] and not snap['using_tool'] and snap['menu'] is None and not snap['event_up']:
   time.sleep(.4);return
 raise SystemExit('fixture native transition did not finish')
def poll(r):
 end=time.monotonic()+150
 while r['status']=='running' and time.monotonic()<end:
  time.sleep(.15);r=tool('action.status',id=r['command_id'])
 receipts.append(r);(out/'receipts.json').write_text(json.dumps(receipts,ensure_ascii=False,indent=2));return r
fixture();check(scenario('inventory_timing_read')['free_slots']==1,'fixture has exactly one empty slot')
r=poll(tool('work.run',goal='wood',location='Farm',count=3));check(r['status']=='succeeded' and scenario('inventory_timing_read')['stored']==0,'one free slot continues gathering without a storage trip',r)
fixture(1);r=poll(tool('work.run',goal='wood',location='Farm',count=3));check(r['status']=='succeeded' and scenario('inventory_timing_read')['stored']==0,'full bag stacks native wood without unloading',r)
fixture(1);r=poll(tool('work.run',goal='forage',item='(O)16',location='Farm',count=1));check(r['status']=='succeeded' and scenario('inventory_timing_read')['stored']>0,'new output with no room unloads and resumes gathering',r)
fixture();r=tool('player.work',skill='plant',slot=5,tiles=[dict(x=50,y=29)]);saw_stowed=False;raised=False;end=time.monotonic()+90
while r['status']=='running' and time.monotonic()<end:
 time.sleep(.1);r=tool('action.status',id=r['command_id']);nav=r.get('navigation') or {}
 if r['phase']=='work_walk' and nav.get('controller_owned') and nav.get('path_remaining',0)>0:
  saw_stowed=True;raised=raised or nav.get('carrying_object',True)
check(saw_stowed and not raised,'walking carries no object while preserving the planting slot')
s=scenario('inventory_timing_read');check(r['status']=='succeeded' and s['planted'] and s['seeds']==14,'arrival restores seed and native planting consumes exactly one',r)
scenario('agent_pause')
