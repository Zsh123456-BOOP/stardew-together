"""Summarize one native trial from persisted evidence; does not contact the game."""
import argparse,json,statistics
from collections import Counter
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('run',type=Path);a=p.parse_args();root=Path(__file__).resolve().parents[1]
def read(path,default=None):return json.loads(path.read_text()) if path.exists() else default
def rows(path):
 if not path.exists():return []
 result=[]
 for line in path.read_text().splitlines():
  try:result.append(json.loads(line))
  except json.JSONDecodeError:continue # A running writer may have an incomplete last line.
 return result
baseline=read(a.run/'baseline.json',{});latest=read(a.run/'latest.json',{});result=read(a.run/'result.json');state=latest.get('state',{});snap=latest.get('snapshot',{})
run=state.get('RunId');usage=[r for r in rows(root/'work/CompanionMods/Together/usage/autoplay-model-usage.jsonl') if r.get('run_id')==run]
samples=rows(a.run/'samples.jsonl');events=rows(a.run/'events.jsonl');tools=Counter();failures=Counter()
for e in events:
 try:d=json.loads(e['Text'])
 except (ValueError,KeyError):continue
 if e['Kind']=='decision':tools.update(c['tool'] for c in d.get('calls',[]))
 if e['Kind']=='task_finished' and d.get('state')!='succeeded':failures[d.get('error','unknown')]+=1
# Inspect only the save associated with this trial and filter its exact run.
# Native half-hour business snapshots include stock and assets absent from the
# lightweight observer; never infer earnings from cash alone.
save=baseline.get('state',{}).get('save_id')
native=[]
if save and str(save).isdigit():
 for path in sorted((root/'work/CompanionMods/Together/logs'/str(save)).glob('*/day-*.jsonl')):
  native.extend(r for r in rows(path) if r.get('run')==run)
last_by_day={}
for r in sorted(native,key=lambda r:r.get('utc','')):
 if r.get('kind')=='business_snapshot' and isinstance(r.get('payload'),dict):last_by_day[r['day']]=r
business_days=[]
for day,r in sorted(last_by_day.items()):
 v=r['payload']
 business_days.append(dict(day=day,time=r['time'],cash=v.get('cash'),total_earned=v.get('total_earned'),pending_shipping=v.get('pending_shipping'),inventory=v.get('inventory'),crops=v.get('crops'),animals=v.get('animals'),buildings=v.get('buildings'),machines=v.get('machines'),operating=v.get('operating'),tasks=v.get('tasks'),stamina=v.get('Stamina'),energy=v.get('energy')))
report=dict(run_id=run,result=result or {'passed':False,'reason':'still_running_not_yet_verified'},day=snap.get('day'),time=snap.get('time'),normal_sleeps=state.get('SleepDays'),cash=snap.get('money'),stamina=snap.get('stamina'),decisions=state.get('Decisions'),received_model_responses=len(usage),input_tokens=sum(r.get('input_tokens',0) for r in usage),output_tokens=sum(r.get('output_tokens',0) for r in usage),median_input_tokens=statistics.median([r.get('input_tokens',0) for r in usage]) if usage else None,tools=dict(tools),failed_tasks=dict(failures),samples=len(samples),native_trace_records=len(native),business_days=business_days,economy_assessment=dict(continuity_passed=bool(result and result.get('passed')),profitability='requires_cashflow_harvest_and_reinvestment_audit',pending_shipping_estimate=sum(x.get('estimated_sale',0) for x in (business_days[-1].get('pending_shipping') or [])) if business_days else None),limits='Cash is not profit. Observer events can omit high frequency events; native memory/business logs are authoritative. Seven sleeps alone do not establish sustainable late-game operation.')
(a.run/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2));print(json.dumps(report,ensure_ascii=False,indent=2))
