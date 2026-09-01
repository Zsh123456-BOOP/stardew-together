"""Native door permission and scattered tree pickup, explicit AgentLab fixture only."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path(sys.argv[1] if len(sys.argv)>1 else 'work/route-pickup-native');out.mkdir(exist_ok=True);checks=[];receipts=[]
def sc(code,**args):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**args))
def tool(code,**args):return sc('agent_tool',tool=code,args=args)
def check(ok,label):
 checks.append(dict(passed=bool(ok),label=label));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
 if not ok:raise AssertionError(label)
def poll(r,seconds=150):
 end=time.monotonic()+seconds
 while r['status']=='running' and time.monotonic()<end:
  time.sleep(.1);r=tool('action.status',id=r['command_id'])
 receipts.append(r);(out/'receipts.json').write_text(json.dumps(receipts,ensure_ascii=False,indent=2));return r
try:
 sc('door_interaction_fixture',time=800);time.sleep(3)
 r=poll(tool('player.travel',location='SeedShop'))
 check(r['status']=='failed' and r['error']=='route_access_denied_check_opening_hours_or_friendship','closed native shop still denies entry despite explicit door targeting')
 check(not any('Gus' in str(e) for e in r['effects']),'door validation does not accidentally greet the nearby villager')
 sc('door_interaction_fixture',time=1000);time.sleep(3)
 r=poll(tool('player.travel',location='SeedShop'))
 check(r['status']=='succeeded' and b.request('GET','/lab/together')['autoplay']['snapshot']['location']=='SeedShop','open native door enters the shop with a villager at the doorway')
 r=poll(tool('player.service',location='SeedShop',service='shop',shop='SeedShop'))
 check(r['status']=='succeeded','arrival connects to the real shop service')
 tool('menu.close')
 sc('cleanup_fixture');time.sleep(3);sc('inventory_timing_fixture');time.sleep(3)
 before=b.state()['player']['inventory'].get('(O)388:0',0)
 r=poll(tool('player.work',skill='chop',slot=0,tiles=[dict(x=52,y=32)]))
 (out/'drops.json').write_text(json.dumps(sc('loose_drop_read'),ensure_ascii=False,indent=2))
 check(r['status']=='succeeded','native felling, stump removal and scattered pickup complete continuously')
 check(b.state()['player']['inventory'].get('(O)388:0',0)>before,'real tree output is present in inventory')
 check(any(e.get('kind')=='pickup_verified' and e.get('remaining')==0 for e in r['effects']),'completion contains verified empty tracked debris, not only a felled tree')
 sc('agent_schedule_probe')
 plan=tool('plan.read');day=b.state()['day']
 tool('plan.submit',submission_id='self-routing-home',expected_revision=plan['revision'],tasks=[dict(id='self-routing-home',actor='player',tool='player.travel',args=dict(location='FarmHouse'),location='FarmHouse',day=day)])
 end=time.monotonic()+90
 while time.monotonic()<end:
  tasks=b.request('GET','/lab/together')['autoplay']['state']['Schedule']['Tasks'];t=next(t for t in tasks if t['spec']['id']=='self-routing-home')
  if t['state'] in ['failed','succeeded','blocked']:break
  time.sleep(.2)
 check(t['state']=='succeeded','self-routing travel accepts destination metadata without requiring already being there')
finally:
 sc('agent_pause');sc('agent_ui')
