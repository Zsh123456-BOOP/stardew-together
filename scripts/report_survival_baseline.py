"""A–E evidence report, filtered by a single native run ID; never edits evidence."""
import argparse,json,collections,statistics
from pathlib import Path

def stats(values):
 v=sorted(values)
 return dict(count=len(v),mean=sum(v)/len(v) if v else None,p50=v[int((len(v)-1)*.5)] if v else None,p95=v[int((len(v)-1)*.95)] if v else None,max=v[-1] if v else None)

def report(logroot,run):
 events=[]
 for path in logroot.glob('*/day-*.jsonl'):
  for n,line in enumerate(path.open(),1):
   e=json.loads(line)
   if e.get('run')==run:events.append(dict(e,source=str(path)+':'+str(n)))
 events.sort(key=lambda e:e['utc']);days=[]
 for day in sorted(set(e['day'] for e in events)):
  rows=[e for e in events if e['day']==day]
  def of(kind):return [e for e in rows if e['kind']==kind]
  starts=of('quality_day_start');beds=of('quality_bedtime')
  # Finalized records are written on the NEXT native day, after shipping settles.
  finishes=[e for e in events if e['kind']=='survival_day_quality' and e['payload'].get('day')==day]
  end=finishes[-1]['payload'] if finishes else None;bed=beds[-1]['payload'] if beds else None
  minutes=bed['TimeBreakdownMinutes'] if bed else {};available=bed['ActualAwakeMinutes'] if bed else None
  roots=collections.Counter(e['payload'].get('error') for e in of('task_finished') if e['payload'].get('state')=='failed')
  shapes=collections.Counter(e['payload']['shape'] for e in of('loadout_shape_rejected'))
  declarations=collections.Counter(e['payload']['result']['error'] for e in of('tool_result') if isinstance(e['payload'].get('result'),dict) and e['payload']['result'].get('error') and not e['payload']['result'].get('command_id') and e['payload'].get('tool') not in ('action.status','plan.read'))
  deposits=of('supply_verified')
  failures=[e for e in of('loadout_failed')]
  calls=[e['payload'] for e in of('model_usage')]
  inp=sum(e.get('input_tokens',0) for e in calls);hit=sum(e.get('cache_hit_tokens',0) for e in calls)
  moves=[e for e in of('loadout_transferred')]
  stores=[e for e in of('task_started') if e['payload'].get('result',{}).get('goal')=='store']
  days.append(dict(day=day,complete=bool(end and bed),A=dict(sleep_time=bed['SleepTime'] if bed else None,available_minutes=available,minutes=minutes,percent={k:100*v/available for k,v in minutes.items()} if available else {},sum_minutes=sum(minutes.values()),wall_seconds=bed['WallSecondsByState'] if bed else {},wall_detail_seconds=bed.get('WallDetailSeconds',{}) if bed else {},awake_labor_ratio=end.get('awake_labor_ratio') if end else None),B=dict(declaration_failure_counts=dict(declarations),native_storage_visits=len(deposits),native_items_stored=sum(e['payload'].get('moved',0) for e in deposits),failed_tasks=sum(roots.values()),root_counts=dict(roots),repeated_roots={k:v for k,v in roots.items() if v>1},store_tasks=len(stores),loadout_transfers=len(moves),early_sleep_before_1800=bool(bed and bed['SleepTime']<1800),degradations=[e['payload'] for e in of('survival_transition')],note='early sleep flag is observational, not an invented G3 threshold; repeats are counts, not assumed identical causes'),C=dict(start=starts[0]['payload']['ledger'] if starts else None,before_sleep=bed.get('ledger') if bed else None,settled=end),D=dict(responses=len(calls),input_tokens=inp,output_tokens=sum(e.get('output_tokens',0) for e in calls),cache_hit_tokens=hit,cache_hit_rate=hit/inp if inp else None,model_failures=len(of('model_unavailable')),model_latency_ms=stats([e['latency_ms'] for e in calls]),recoverable_transfer_interruptions=sum(e['payload'].get('disposition')=='abandon_replan_actual_stock' for e in failures)),E=dict(shape_rejections=dict(shapes),same_version_rejections=sum(e['payload'].get('repeat',False) for e in of('loadout_shape_rejected')))))
 handoffs=[e['payload']['delay_ms'] for e in events if e['kind']=='work_handoff'];nav=[]
 for e in events:
  if e['kind']=='native_action_timing':nav.extend(e['payload']['navigation'])
 return dict(run=run,events=len(events),days=days,performance=dict(action_handoff_ms=stats(handoffs),model_wait_ms=stats([e['payload']['latency_ms'] for e in events if e['kind']=='model_usage']),path_search_ms_per_action=stats([n['path_search_ms'] for n in nav]),path_searches=sum(n['path_searches'] for n in nav),frame_measurements=[dict(day=e['day'],**e['payload']['performance']) for e in events if e['kind']=='quality_bedtime'],slow_frames=[e for e in events if e['kind']=='slow_frame']),evidence_files=sorted(set(e['source'].rsplit(':',1)[0] for e in events)))

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--logroot',type=Path,required=True);p.add_argument('--run',required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
 a.out.write_text(json.dumps(report(a.logroot,a.run),ensure_ascii=False,indent=2))
