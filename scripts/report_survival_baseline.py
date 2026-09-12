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
  days.append(dict(day=day,complete=bool(end and bed),A=dict(transport_minutes=bed.get("TransportMinutes",{}) if bed else {},transport_wall_seconds=bed.get("TransportWallSeconds",{}) if bed else {},sleep_time=bed['SleepTime'] if bed else None,available_minutes=available,minutes=minutes,percent={k:100*v/available for k,v in minutes.items()} if available else {},sum_minutes=sum(minutes.values()),wall_seconds=bed['WallSecondsByState'] if bed else {},wall_detail_seconds=bed.get('WallDetailSeconds',{}) if bed else {},awake_labor_ratio=end.get('awake_labor_ratio') if end else None),B=dict(declaration_failure_counts=dict(declarations),native_storage_visits=len(deposits),native_items_stored=sum(e['payload'].get('moved',0) for e in deposits),failed_tasks=sum(roots.values()),root_counts=dict(roots),repeated_roots={k:v for k,v in roots.items() if v>1},store_tasks=len(stores),loadout_transfers=len(moves),early_sleep_before_1800=bool(bed and bed['SleepTime']<1800),degradations=[e['payload'] for e in of('survival_transition')],note='early sleep flag is observational, not an invented G3 threshold; repeats are counts, not assumed identical causes'),C=dict(start=starts[0]['payload']['ledger'] if starts else None,before_sleep=bed.get('ledger') if bed else None,settled=end),D=dict(responses=len(calls),input_tokens=inp,output_tokens=sum(e.get('output_tokens',0) for e in calls),cache_hit_tokens=hit,cache_hit_rate=hit/inp if inp else None,model_failures=len(of('model_unavailable')),model_latency_ms=stats([e['latency_ms'] for e in calls]),recoverable_transfer_interruptions=sum(e['payload'].get('disposition')=='abandon_replan_actual_stock' for e in failures)),E=dict(shape_rejections=dict(shapes),same_version_rejections=sum(e['payload'].get('repeat',False) for e in of('loadout_shape_rejected')))))
 for row in days:
  row['tool_calls']=dict(collections.Counter(e['payload'].get('tool','unknown') for e in events if e['day']==row['day'] and e['kind']=='tool_result'))
 tools=collections.Counter(e['payload'].get('tool','unknown') for e in events if e['kind']=='tool_result')
 groups={name:{tool:tools.get(tool,0) for tool in names} for name,names in {
  'purchase_business':['shop.read','player.buy','player.procure','farm.economy','farm.autonomy','farm.business','day.routine'],
  'knowledge_tasks':['knowledge.search','knowledge.get','goal.requirements','progress.read','progress.dependencies','progress.roadmap','quest_board.read','player.accept_quest']}.items()}
 idle=[e for e in events if e['kind']=='tool_result' and isinstance(e['payload'].get('result'),dict) and str(e['payload']['result'].get('error','')).startswith('known_failure_conditions_unchanged:')]
 routes=[e for e in events if e['kind']=='actor_route' and e['payload'].get('actor')=='player']
 transport=collections.Counter();crossings=[];revisits=[];last_visit={};last=None
 for e in routes:
  r=e['payload'];context=r.get('route_context',{});moving=r.get('observed_delta',0)>0 or r.get('event_type')=='map_transition'
  if not moving:last=e;continue
  seconds=r.get('elapsed_since_sample') or 0
  kind='storage_roundtrip' if context.get('storage') or context.get('preparation') else 'cross_map' if context.get('native_skill')=='player.travel' or r.get('event_type')=='map_transition' else 'within_farm' if r.get('location')=='Farm' else 'other_local'
  transport[kind]+=seconds
  if r.get('event_type')=='map_transition':
   crossing=dict(day=e['day'],time=e.get('time'),source=e['source'],origin=context.get('previous_location'),destination=r['location'],task=r.get('task_id'),purpose=r.get('purpose'),work=r.get('work'));crossings.append(crossing)
   key=(e['day'],r['location'])
   if key in last_visit:revisits.append(dict(previous=last_visit[key],current=crossing,verdict='revisit_observed_not_proof_of_waste'))
   last_visit[key]=crossing
  last=e
 segments=[e for e in events if e['kind']=='route_segment']
 route_report=dict(sampled_transport_wall_seconds=dict(transport),accounting='observed movement intervals; storage takes precedence; not clock-time G3. Unproductive reversal requires manual cause review.',invalid_reversal_seconds=None,crossings=crossings,revisits=revisits,cleanup_segments=[e for e in segments if any(w.get('goal')=='cleanup' for w in e['payload'].get('work',[]))],cleanup_target_sequences=[e for e in events if e['kind']=='cleanup_route'],segments=segments)
 owners={}
 for e in events:
  if e['kind']=='action_result':
   parent=e['payload'].get('command_id')
   for child in e['payload'].get('evidence',[]):
    if isinstance(child,dict) and child.get('command_id'):owners[child['command_id']]=parent
  if e['kind']=='route_segment' and len(e['payload'].get('work',[]))==1:
   owners[e['payload']['route']['command_id']]=e['payload']['work'][0]['command_id']
 handoff_groups=collections.defaultdict(list)
 for e in events:
  if e['kind']=='work_handoff':
   h=e['payload'];owner=owners.get(h['from']);group='unclassified' if owner is None else 'within_same_parent' if owner==h['command_id'] else 'between_parent_tasks'
   handoff_groups[group].append(h['delay_ms'])
 handoffs=[e['payload']['delay_ms'] for e in events if e['kind']=='work_handoff'];nav=[]
 for e in events:
  if e['kind']=='native_action_timing':nav.extend(e['payload']['navigation'])
 return dict(run=run,events=len(events),tool_calls=dict(tools),tool_groups=groups,idle_rejections=len(idle),stale_responses=sum(e["kind"]=="stale_decision" for e in events),transport=route_report,days=days,performance=dict(action_handoff_ms=stats(handoffs),action_handoff_by_parent_ms={k:stats(v) for k,v in handoff_groups.items()},model_wait_ms=stats([e['payload']['latency_ms'] for e in events if e['kind']=='model_usage']),path_search_ms_per_action=stats([n['path_search_ms'] for n in nav]),path_searches=sum(n['path_searches'] for n in nav),frame_measurements=[dict(day=e['day'],**e['payload']['performance']) for e in events if e['kind']=='quality_bedtime'],slow_frames=[e for e in events if e['kind']=='slow_frame']),evidence_files=sorted(set(e['source'].rsplit(':',1)[0] for e in events)))

if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--logroot',type=Path,required=True);p.add_argument('--run',required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
 a.out.write_text(json.dumps(report(a.logroot,a.run),ensure_ascii=False,indent=2))
