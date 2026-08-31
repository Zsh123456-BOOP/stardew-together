"""Grouped native regressions; explicit AgentLab fixture, never formal trial data."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path(sys.argv[1] if len(sys.argv)>1 else 'work/business-recovery-native');out.mkdir(exist_ok=True);checks=[];receipts=[]
def sc(code,**args):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**args))
def tool(code,**args):return sc('agent_tool',tool=code,args=args)
def check(ok,label):
 checks.append(dict(passed=bool(ok),label=label));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
 if not ok:raise AssertionError(label)
def poll(r):
 end=time.monotonic()+160
 while r['status']=='running' and time.monotonic()<end:
  time.sleep(.08);r=tool('action.status',id=r['command_id'])
 receipts.append(r);(out/'receipts.json').write_text(json.dumps(receipts,ensure_ascii=False,indent=2));return r
try:
 sc('business_recovery_fixture')
 end=time.monotonic()+15
 while time.monotonic()<end:
  snap=b.request('GET','/lab/together')['autoplay']['snapshot']
  if snap['location']=='Farm' and snap['can_move'] and not snap['using_tool'] and snap['menu'] is None:
   time.sleep(2);break
  time.sleep(.2)
 check(sc('business_recovery_read')==dict(wood=5,cargo=45,boxes=0,free_slots=6),'fixture separates real player and partner wood, no warehouse')
 # At least two evictions are required to reproduce the dictionary slot reuse bug.
 for n in range(130):
  r=poll(tool('player.move',x=45+n%2,y=27))
  if r['status']!='succeeded':raise AssertionError(str(r))
 check(True,'130 consecutive native moves retain and return their own receipts')
 r=poll(tool('work.run',goal='withdraw',item='(O)388',count=10));s=sc('business_recovery_read')
 check(r['status']=='succeeded' and s['wood']==15 and s['cargo']==35,'nearby partner handoff conserves stock and transfers only requested quantity')
 check(any(e.get('kind')=='adjacent_partner_handoff' and e.get('moved')==10 for e in r['evidence']),'handoff records positions and before/after evidence')
 r=poll(tool('work.run',goal='storage_expand'));s=sc('business_recovery_read')
 check(r['status']=='succeeded' and s['wood']==0 and s['cargo']==0 and s['boxes']==1,'remaining partner cargo becomes one native crafted and placed shared chest')
 check(any(e.get('kind')=='adjacent_partner_handoff' and e.get('moved')==35 for e in r['evidence']),'storage bootstrap receives existing wood instead of recollecting it')
 day=b.state()['day'];r=poll(tool('player.sleep'))
 check(r['status']=='succeeded' and b.state()['day']==day+1,'receipt retention survives a real native overnight and saving')
 r=poll(tool('player.travel',location='Farm'))
 check(r['status']=='succeeded','next-day native action receipt remains available')
finally:
 sc('agent_pause');sc('agent_ui')
