"""Grouped native regression: discovery, spatial retry, gift, owned-seed business.
Uses a pre-existing AgentLab fixture save; no paid model calls.
"""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();s=b.state();assert s['player']['name']=='AgentLab'
out=Path(sys.argv[1] if len(sys.argv)>1 else 'work/planning-loop-native');out.mkdir(exist_ok=True)
def sc(c,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=c,**kw))
def tool(c,**kw):return sc('agent_tool',tool=c,args=kw)
def read():return b.request('GET','/lab/together')['autoplay']
checks=[]
def check(ok,label):
 checks.append(dict(passed=bool(ok),label=label));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
 if not ok:raise AssertionError(label)
def queue(ident,code,args):
 p=tool('plan.read');tool('plan.submit',submission_id=ident,expected_revision=p['revision'],tasks=[dict(id=ident,actor='player',tool=code,args=args,day=b.state()['day'])])
 end=time.monotonic()+90
 while time.monotonic()<end:
  d=read();t=next(t for t in d['state']['Schedule']['Tasks'] if t['spec']['id']==ident)
  if t['state'] in ['succeeded','failed','blocked','needs_review']:return t
  time.sleep(.2)
 raise TimeoutError(ident)
sc('auto',enabled=0);sc('agent_schedule_probe');tool('farm.business',enabled=False);tool('day.routine',enabled=False)
lookup=tool('tools.lookup',names=['player.build','player.machine']);check('budget' in lookup['definitions']['player.build'] and 'player.machine' in lookup['definitions'],'on-demand construction and production schemas are complete')
world=tool('world.read');check(not any(x['Item']=='(O)472' for x in world['farm']['stock']),'unopened starter gift is not counted as owned stock')
check(queue('go-out','player.travel',dict(location='Farm'))['state']=='succeeded','native exit before spatial retry')
check(queue('come-home','player.travel',dict(location='FarmHouse'))['state']=='succeeded','native home entry')
bad=queue('too-far','player.interact',dict(x=3,y=7));check(bad['state']=='failed' and bad['error']=='move_adjacent_first','distant interaction produces a real spatial failure')
check(queue('stand-beside','player.move',dict(x=3,y=8))['state']=='succeeded','native move changes actual failure conditions')
good=queue('retry-near','player.interact',dict(x=3,y=7));check(good['state']=='succeeded','same interaction succeeds after moving, old failure does not veto it')
end=time.monotonic()+8
while read()['snapshot']['menu'] is not None and time.monotonic()<end:time.sleep(.2)
check(b.state()['player']['inventory'].get('(O)472:0',0)==15,'native reward actually in bag')
tool('farm.business',enabled=True,expand=False,budget_per_day=0,keep_gold=100);tool('day.routine',enabled=False)
end=time.monotonic()+210;finished=None
try:
 while time.monotonic()<end:
  d=read();(out/'latest.json').write_text(json.dumps(d,ensure_ascii=False,indent=2))
  plant=[t for t in d['state']['Schedule']['Tasks'] if t['spec']['tool']=='work.run' and t['spec']['args'].get('goal')=='plant']
  if plant and all(t['state'] in ['succeeded','failed','blocked','needs_review'] for t in plant):finished=plant;break
  if any(e['Kind']=='farm_investment_blocked' for e in d['state']['Journal']):break
  time.sleep(.4)
 check(bool(finished) and all(t['state']=='succeeded' for t in finished),'owned seeds flow into a complete planned planting job without shop or model')
 check(not any(t['spec']['tool']=='player.buy' for t in d['state']['Schedule']['Tasks']),'zero cash budget does not create seed purchases')
 check(not any(t['spec']['tool']=='player.read_mail' and t['state']=='failed' for t in d['state']['Schedule']['Tasks']),'mail interaction timer does not prevent native door travel')
 check(d['state']['Decisions']==0,'supply and planting contract uses zero model decisions')
 print('model_context',d.get('model_context'),flush=True)
finally:
 sc('agent_pause');tool('farm.business',enabled=False);sc('agent_ui')
