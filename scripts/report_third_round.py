"""Third-round additions. Raw event counts never stand in for business success."""
import argparse,collections,json
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--logroot',type=Path,required=True);p.add_argument('--run',required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
events=[]
for path in sorted(a.logroot.rglob('day-*.jsonl')):
 for line,n in zip(path.read_text().splitlines(),range(1,10000000)):
  try:e=json.loads(line)
  except ValueError:continue
  if e.get('run')!=a.run:continue
  e['source']={'file':str(path),'line':n};events.append(e)
events.sort(key=lambda e:e['utc']);days={}
def payload(e):return e['payload'] if isinstance(e.get('payload'),dict) else {}
def brief(e):return {k:e[k] for k in ('day','time','utc','kind','payload','source') if k in e}
for day in sorted({e['day'] for e in events}):
 rows=[e for e in events if e['day']==day];calls=collections.Counter();purchases=[];seen=set()
 for e in rows:
  d=payload(e)
  if e['kind']=='tool_result':calls[d.get('tool','unknown')]+=1
  # Count verified native purchases once even if their receipt is repeated.
  if e['kind']=='action_result' and d.get('command_id') not in seen:
   for x in d.get('effects',[]):
    if x.get('kind')=='native_purchase':purchases.append(dict(command_id=d.get('command_id'),utc=e['utc'],time=e['time'],effect=x,source=e['source']))
   seen.add(d.get('command_id'))
 quotes=[brief(e) for e in rows if e['kind']=='shop_quote_observed'];dispatch=[e for e in rows if e['kind']=='spatial_dispatch']
 policy=[brief(e) for e in rows if e['kind'] in ('business_policy','daily_routine_policy') or e['kind']=='tool_result' and payload(e).get('tool') in ('day.routine','farm.business')]
 menu=[e for e in rows if e['kind']=='menu_choice_effect'];reject=[e for e in rows if e['kind']=='menu_choice_rejected']
 storage=[brief(e) for e in rows if e['kind']=='goal_facility_verified' and payload(e).get('capability')=='shared_storage']
 days[day]=dict(tool_calls=dict(calls),shop=dict(read_calls=calls['shop.read'],native_quote_observations=len(quotes),purchases=purchases,verified_amount=sum(x['effect']['cost'] for x in purchases),quotes=quotes),policy_changes=policy,
  spatial=dict(dispatches=len(dispatch),region_switches=sum(bool(payload(e).get('region_switch')) for e in dispatch),region_returns=sum(bool(payload(e).get('region_return')) for e in dispatch),records=[brief(e) for e in dispatch],note='12-tile regional proxy; raw route_segment remains the distance evidence'),
  menu=dict(choose_calls=calls['menu.choose'],no_effect=sum(not payload(e).get('changed') and payload(e).get('dispatched') is None for e in menu),rejected=len(reject),reasons=dict(collections.Counter(payload(e).get('reason') for e in reject)),control_stops=[brief(e) for e in rows if e['kind']=='menu_execution_stop']),
  first_storage=storage[0] if storage else None)
a.out.write_text(json.dumps(dict(run=a.run,days=days,note='Use alongside A–E baseline report; incomplete days are not G3 samples.'),ensure_ascii=False,indent=2))
print(json.dumps({d:{'purchases':len(v['shop']['purchases']),'switches':v['spatial']['region_switches'],'no_effect':v['menu']['no_effect']} for d,v in days.items()}))
