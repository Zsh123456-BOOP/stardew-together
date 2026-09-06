"""One-slot mixed-quality warehouse -> native shipping -> overnight evidence."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab';out=Path('work/shipping-capacity');out.mkdir(exist_ok=True);checks=[]
def sc(code,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**kw))
def tool(code,**kw):return sc('agent_tool',tool=code,args=kw)
def check(ok,label):
 checks.append(dict(passed=bool(ok),label=label));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True);assert ok,label
try:
 sc('agent_schedule_probe');day=b.state()['day'];rev=tool('plan.read')['revision']
 tool('plan.submit',submission_id='old-future',expected_revision=rev,tasks=[dict(id='old-future',tool='player.travel',args=dict(location='Town'),day=day,not_before=2300)])
 rev=tool('plan.read')['revision'];reply=dict(plan='synthetic same-turn revision regression',calls=[dict(tool='plan.cancel',args=dict(ids=['old-future'])),dict(tool='plan.submit',args=dict(submission_id='own-a',expected_revision=rev,tasks=[dict(id='own-a',tool='player.travel',args=dict(location='Farm'),day=day,not_before=2400)])),dict(tool='plan.submit',args=dict(submission_id='own-b',expected_revision=rev,tasks=[dict(id='own-b',tool='player.travel',args=dict(location='Town'),day=day,not_before=2400)]))])
 sc('agent_reply_probe',reply=json.dumps(reply));time.sleep(1)
 d=b.request('GET','/lab/together')['autoplay'];tasks={t['spec']['id']:t for t in d['state']['Schedule']['Tasks']};(out/'revision-evidence.json').write_text(json.dumps(d,ensure_ascii=False,indent=2))
 check(tasks.get('own-a',{}).get('state')=='queued' and tasks.get('own-b',{}).get('state')=='queued','one reply cancels then submits two plans without spurious revision failure')
 sc('shipping_capacity_fixture');time.sleep(1);before=sc('shipping_capacity_read');(out/'before.json').write_text(json.dumps(before,ensure_ascii=False,indent=2));check(before['free_slots']==1,'mixed-quality warehouse begins with only one free player slot')
 sc('agent_schedule_probe');tool('day.routine',enabled=False)
 tool('plan.submit',submission_id='shipping-night',expected_revision=tool('plan.read')['revision'],tasks=[dict(id='shipping-night',tool='player.sleep',args=dict(reason='分批交付完成后正常过夜',review='已经晚间，没有必要农务；先交付实际可售鱼，预留回家和睡觉时间'),day=before['snapshot']['day'])])
 end=time.monotonic()+240
 while time.monotonic()<end:
  d=b.request('GET','/lab/together')['autoplay'];t=next(t for t in d['state']['Schedule']['Tasks'] if t['spec']['id']=='shipping-night')
  if t['state'] in ['succeeded','failed','blocked']:break
  time.sleep(.25)
 after=sc('shipping_capacity_read');(out/'after.json').write_text(json.dumps(after,ensure_ascii=False,indent=2));(out/'tasks.json').write_text(json.dumps(d['state']['Schedule']['Tasks'],ensure_ascii=False,indent=2))
 check(t['state']=='succeeded' and d['state']['SleepDays']==1,'late-night batched shipment finishes and sleeps natively')
 check(after['earned']>before['earned'],'warehouse fish produce actual overnight revenue')
 for qid in ['(O)129','(O)137']:
  sold=after['shipped'].get(qid[3:],0)-before['shipped'].get(qid[3:],0)
  check(after['stock'].get(qid,0)+sold==before['stock'][qid],qid+' exact inventory plus shipped conservation across qualities')
 check(not any(t.get('error')=='inventory_full_or_shared_stock_changed' for t in d['state']['Schedule']['Tasks']),'one slot does not cause whole-shipment capacity failure')
 check(d['state']['Decisions']==0,'shipment planning and overnight need no model callbacks')
finally:
 sc('agent_pause');sc('agent_ui')
