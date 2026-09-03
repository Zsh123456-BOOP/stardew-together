"""Combined native regression, AgentLab fixture only; never a seven-day result."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path(sys.argv[1] if len(sys.argv)>1 else 'work/operating-native');out.mkdir(exist_ok=True);checks=[]
def sc(code,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**kw))
def tool(code,**kw):return sc('agent_tool',tool=code,args=kw)
def check(ok,label):
 checks.append(dict(passed=bool(ok),label=label));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
 if not ok:raise AssertionError(label)
def action(code,**kw):
 r=tool(code,**kw);end=time.monotonic()+180
 while r['status']=='running' and time.monotonic()<end:time.sleep(.25);r=tool('action.status',id=r['command_id'])
 (out/(code.replace('.','-')+'-'+str(len(checks))+'.json')).write_text(json.dumps(r,ensure_ascii=False,indent=2))
 return r
def planting():
 sc('agent_schedule_probe');tool('day.routine',enabled=False)
 req=tool('farm.economy',location='Farm',budget=0,keep_gold=0,plots=4,max_daily_manual_water=48)
 end=time.monotonic()+30
 while time.monotonic()<end:
  r=tool('farm.economy_status',plan_id=req['plan_id'])
  if r['status']=='planned':break
  time.sleep(.3)
 check(bool(r['result']['Plants']),'next planting batch has a feasible local plan')
 q=tool('farm.execute',plan_id=req['plan_id']);ids=set(q['tasks']);end=time.monotonic()+240
 while time.monotonic()<end:
  tasks=[t for t in b.request('GET','/lab/together')['autoplay']['state']['Schedule']['Tasks'] if t['spec']['id'] in ids]
  if tasks and all(t['state'] in ['failed','blocked','succeeded','cancelled'] for t in tasks):break
  time.sleep(.25)
 (out/('plant-'+str(len(checks))+'.json')).write_text(json.dumps(dict(plan=r,tasks=tasks),ensure_ascii=False,indent=2))
 check(all(t['state']=='succeeded' for t in tasks),'native local planting and watering batch completes')
 return {(p['Tile']['X'],p['Tile']['Y']) for p in r['result']['Plants']}
try:
 sc('farm_energy_fixture',stamina=270);time.sleep(2)
 first=planting()
 sc('operating_next_batch');second=planting()
 check(not(first&second) and all(min(abs(x-a)+abs(y-c) for a,c in first)<=3 for x,y in second),'second batch extends first approved field without overwriting crops or jumping across farm')
 district=sc('operating_read');(out/'district.json').write_text(json.dumps(district,ensure_ascii=False,indent=2))
 sc('business_recovery_fixture');time.sleep(2)
 before=sc('operating_read');r=action('work.run',goal='wood',count='40',stock_target='40')
 check(r['status']=='succeeded' and r['stop_reason']=='shared_stock_target_already_met' and sc('operating_read')['stock']==before['stock'],'numeric string target counts player and partner wood and skips surplus gathering')
 r=action('work.run',goal='storage_expand');after=sc('operating_read')
 check(r['status']=='succeeded' and len(after['boxes'])==1 and after['stock']==before['stock']-50,'native chest crafting and partner delivery conserve the exact 50 wood')
 hx,hy=after['home']['X'],after['home']['Y'];x,y=after['boxes'][0]['tile']
 check(abs(x-hx)+abs(y-hy)<=26,'new main chest is near home even though Farmer started elsewhere')
 # Evening review must see optional debris but permit a genuine continuous night.
 sc('bedtime_review_fixture');time.sleep(2);sc('agent_schedule_probe');tool('day.routine',enabled=False)
 day=tool('day.read');start=day['day'];check(day['time']>=1600 and day['budget']['stamina']<40,'bedtime scenario has actual low stamina and an evening clock')
 r=action('player.sleep',reason='当天必要农务已完成，体力不足，收工',review='核对了作物、剩余体力与可选采集；附近杂草没有紧急用途，绕行收益不值得，明天再处理。')
 now=b.request('GET','/lab/together')['autoplay'];(out/'after-night.json').write_text(json.dumps(now['snapshot'],ensure_ascii=False,indent=2))
 check(r['status']=='succeeded' and now['snapshot']['day']==start+1 and now['state']['SleepDays']==1,'one high-level sleep action returns home, enters bed, settles, saves and reaches next day without model rescue')
finally:
 sc('agent_pause');sc('agent_ui')
