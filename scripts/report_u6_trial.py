"""Read-only U6 audit; observed evidence only, never a gate verdict."""
import argparse,collections,json
from pathlib import Path
from report_survival_baseline import report as baseline,stats
from audit_baseline_model import audit
p=argparse.ArgumentParser();p.add_argument('--logroot',type=Path,required=True);p.add_argument('--run',required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
events=[]
for f in a.logroot.glob('*/day-*.jsonl'):
 for n,line in enumerate(f.open(),1):
  try:e=json.loads(line)
  except ValueError:continue
  if e.get('run')==a.run:events.append(dict(e,source=f'{f}:{n}'))
events.sort(key=lambda e:e['utc'])
def of(k):return [e for e in events if e['kind']==k]
def brief(e):return {k:e[k] for k in ('day','time','kind','payload','source')}
requests=[]
for f in a.logroot.glob('*/model-*.jsonl'):
 for n,line in enumerate(f.open(),1):
  e=json.loads(line)
  if e['kind']!='request':continue
  body=json.loads(e['payload']['body']);context=json.loads(body['messages'][-1]['content'])
  if context.get('run_id')!=a.run:continue
  inv=context.get('inventory_plan',{});own=inv.get('ownership',{});bag=context.get('inventory',{}).get('items',[])
  native=sorted((x['slot'],x['item']['id'],x['item']['count'],x['item'].get('quality',0)) for x in bag if x.get('item',{}).get('id'))
  carried=sorted((x['slot'],x['id'],x['count'],x.get('quality',0)) for x in own.get('carried',[]))
  requests.append(dict(call_id=e['call_id'],source=f'{f}:{n}',now=context.get('now'),ownership_present=bool(own),carried_matches_native=native==carried,storable=own.get('transferable_to_warehouse'),free_slots=inv.get('free_slots'),sleep_review=context.get('sleep_review')))
stages=collections.defaultdict(list)
for e in of('slow_frame'):
 for k,v in e['payload'].get('stages',{}).items():stages[k].append(v)
result=dict(run=a.run,baseline=baseline(a.logroot,a.run),model=audit(a.logroot,a.run),ownership_requests=requests,
 ownership_missing=sum(not r['ownership_present'] for r in requests),ownership_mismatches=sum(not r['carried_matches_native'] for r in requests),
 sleep_reviews=[brief(e) for e in of('sleep_reassessment')],failure_counters=[brief(e) for e in of('execution_failure_count')],failure_stops=[brief(e) for e in of('execution_failure_stop')],
 declaration_rejections=[brief(e) for e in of('task_declaration_rejected')],independent_tail_reviews=[brief(e) for e in of('decision_independent_tail_review')],dependent_steps_not_applied=[brief(e) for e in of('decision_dependency_not_applied')],
 native_deposits=[brief(e) for e in of('native_storage')],
 slow_frame_stage_ms={k:stats(v) for k,v in stages.items()},
 timing_note='slow_frame的阶段样本仅代表已记录慢帧，不是所有帧分布；partner字段包含前置质量/生存检查，不能据此归因NPC。完整动作、模型等待、寻路与主线程统计见baseline.performance。')
a.out.write_text(json.dumps(result,ensure_ascii=False,indent=2))
print(json.dumps(dict(events=len(events),requests=len(requests),missing=result['ownership_missing'],mismatches=result['ownership_mismatches'],sleep_reviews=len(result['sleep_reviews']),stops=len(result['failure_stops'])),ensure_ascii=False))
