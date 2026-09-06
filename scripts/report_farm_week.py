"""Summarize one native trial from persisted evidence; does not contact the game."""
import argparse,json,statistics
from collections import Counter
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('run',type=Path);p.add_argument('--mods-dir',type=Path,default=Path('work/CompanionMods'));a=p.parse_args();root=Path(__file__).resolve().parents[1];mods=(root/a.mods_dir).resolve()
def read(path,default=None):return json.loads(path.read_text()) if path.exists() else default
def rows(path):
 if not path.exists():return []
 result=[]
 for line in path.read_text().splitlines():
  try:result.append(json.loads(line))
  except json.JSONDecodeError:continue # A running writer may have an incomplete last line.
 return result
baseline=read(a.run/'baseline.json',{});latest=read(a.run/'latest.json',{});result=read(a.run/'result.json');state=latest.get('state',{});snap=latest.get('snapshot',{})
run=state.get('RunId');usage=[r for r in rows(mods/'Together/usage/autoplay-model-usage.jsonl') if r.get('run_id')==run]
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
 for path in sorted((mods/'Together/logs'/str(save)).glob('*/day-*.jsonl')):
  native.extend(r for r in rows(path) if r.get('run')==run)
last_by_day={}
for r in sorted(native,key=lambda r:r.get('utc','')):
 if r.get('kind')=='business_snapshot' and isinstance(r.get('payload'),dict):last_by_day[r['day']]=r
business_days=[]
for day,r in sorted(last_by_day.items()):
 v=r['payload']
 business_days.append(dict(day=day,time=r['time'],cash=v.get('cash'),total_earned=v.get('total_earned'),pending_shipping=v.get('pending_shipping'),inventory=v.get('inventory'),crops=v.get('crops'),animals=v.get('animals'),buildings=v.get('buildings'),machines=v.get('machines'),operating=v.get('operating'),tasks=v.get('tasks'),stamina=v.get('Stamina'),energy=v.get('energy')))
# Use native action receipts, de-duplicated by command, for observed work.
receipts={}
for r in native:
 if r.get('kind')=='action_result' and isinstance(r.get('payload'),dict):
  v=r['payload'];key=v.get('command_id')
  if key:receipts[key]=r
harvests=[r for r in receipts.values() if r['payload'].get('status')=='succeeded' and r['payload'].get('goal')=='harvest' and r['payload'].get('completed',0)>0]
noops=[r for r in receipts.values() if r['payload'].get('stop_reason')=='shared_stock_target_already_met']
first_harvest=min((r.get('utc','') for r in harvests),default=None)
reinvestments=[r for r in native if r.get('kind')=='farm_investment_plan' and first_harvest and r.get('utc','')>first_harvest]
flows=[]
for r in receipts.values():
 v=r['payload'];before=v.get('before') or {};after=v.get('after') or {}
 if isinstance(before,dict) and isinstance(after,dict) and isinstance(before.get('money'),(int,float)) and isinstance(after.get('money'),(int,float)):
  spent=before['money']-after['money']
  if spent>0:flows.append(dict(day=r['day'],time=r['time'],command_id=v.get('command_id'),skill=v.get('skill'),spent=spent))
assessment=dict(continuity_passed=bool(result and result.get('passed')),profitability='requires_cashflow_and_asset_audit',verified_harvest_batches=len(harvests),harvested_targets=sum(r['payload']['completed'] for r in harvests),investment_plans_after_first_harvest=len(reinvestments),satisfied_stock_requests_without_labor=len(noops),observed_cash_outlays=flows,pending_shipping_estimate=sum(x.get('estimated_sale',0) for x in (business_days[-1].get('pending_shipping') or [])) if business_days else None,note='Investment plans are commitments, not completed productive assets. Outlays include only matching before/after receipts and may be incomplete; native total_earned measures settled gross revenue, not profit.')
report=dict(run_id=run,result=result or {'passed':False,'reason':'still_running_not_yet_verified'},day=snap.get('day'),time=snap.get('time'),normal_sleeps=state.get('SleepDays'),cash=snap.get('money'),stamina=snap.get('stamina'),decisions=state.get('Decisions'),received_model_responses=len(usage),input_tokens=sum(r.get('input_tokens',0) for r in usage),output_tokens=sum(r.get('output_tokens',0) for r in usage),median_input_tokens=statistics.median([r.get('input_tokens',0) for r in usage]) if usage else None,tools=dict(tools),failed_tasks=dict(failures),samples=len(samples),native_trace_records=len(native),business_days=business_days,economy_assessment=assessment,limits='Cash is not profit. Observer events can omit high frequency events; native memory/business logs are authoritative. A completed day count alone do not establish sustainable late-game operation.')
# Bounded diagnostics preserve causal categories, without equating movement with profit.
by_day={};stage_cost={};route_totals={};outcome_reasons=Counter()
for row in native:
 payload=row.get('payload');kind=row.get('kind');day=row.get('day')
 if not isinstance(payload,dict):continue
 if kind=='model_usage':
  v=by_day.setdefault(day,dict(responses=0,input_tokens=0,output_tokens=0,cache_hit_tokens=0,latencies=[]));v['responses']+=1
  for key in ('input_tokens','output_tokens','cache_hit_tokens'):v[key]+=payload.get(key,0)
  v['latencies'].append(payload.get('latency_ms',0))
 if kind=='slow_frame':
  for name,cost in payload.get('stages',{}).items():
   v=stage_cost.setdefault(name,dict(recorded_count=0,sum_ms=0,max_ms=0));v['recorded_count']+=1;v['sum_ms']+=cost;v['max_ms']=max(v['max_ms'],cost)
 if kind=='actor_route':
  key=(day,payload.get('actor'));v=route_totals.setdefault(key,dict(sample_intervals_seconds=0,by_phase_seconds={},observed_tile_steps=0,sampling_gaps=0,map_transitions=0))
  elapsed=payload.get('elapsed_since_sample') or 0
  if 0<elapsed<=20:
   phase=payload.get('phase','unknown');v['sample_intervals_seconds']+=elapsed;v['by_phase_seconds'][phase]=v['by_phase_seconds'].get(phase,0)+elapsed
  event=payload.get('event_type')
  if event=='move':v['observed_tile_steps']+=max(0,payload.get('observed_delta',0))
  if event=='sample_gap':v['sampling_gaps']+=1
  if event=='map_transition':v['map_transitions']+=1
 if kind=='goal_progress':outcome_reasons[payload.get('outcome',{}).get('StopReason','unknown')]+=1
for v in by_day.values():v['median_latency_ms']=statistics.median(v.pop('latencies'))
report['diagnostics']=dict(model_days=[dict(day=k,**v) for k,v in sorted(by_day.items())],slow_stage_cost=stage_cost,actor_days=[dict(day=k[0],actor=k[1],**v) for k,v in sorted(route_totals.items())],outcome_reasons=dict(outcome_reasons),supplies=[dict(day=r['day'],time=r['time'],**r['payload']) for r in native if r.get('kind')=='supply_verified'],notes='Route phase intervals are sampled estimates, not exact CPU or idle time. Gaps are not proof of teleportation. Slow-stage sums cover emitted slow frames only. Currency costs require a dated price source; token counts are actual API usage.')
(a.run/'report.json').write_text(json.dumps(report,ensure_ascii=False,indent=2));print(json.dumps(report,ensure_ascii=False,indent=2))
