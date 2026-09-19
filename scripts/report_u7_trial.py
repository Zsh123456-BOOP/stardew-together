"""Read-only clearance/pickup audit of one autonomous run; no inferred success."""
import argparse,collections,json
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--logroot',type=Path,required=True);p.add_argument('--run',required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
events=[]
for file in a.logroot.glob('*/day-*.jsonl'):
 for line_no,line in enumerate(file.open(),1):
  try:e=json.loads(line)
  except ValueError:continue
  if e.get('run')==a.run:events.append(dict(e,source=f'{file}:{line_no}'))
events.sort(key=lambda e:e['utc'])
actions={e['payload']['command_id']:e for e in events if e['kind']=='native_diary_receipt' and e.get('payload',{}).get('command_id')}
counts=collections.Counter();rows=[]
for cid,e in actions.items():
 r=e['payload'];effects=[x for x in r.get('effects',[]) if x.get('kind','').startswith(('clearance_','native_route_clear','pickup_'))]
 if effects:
  counts.update(x['kind'] for x in effects)
  rows.append(dict(command_id=cid,day=e['day'],time=e['time'],skill=r.get('skill'),status=r.get('status'),error=r.get('error'),completed=r.get('completed'),effects=effects,source=e['source']))
by_day=collections.defaultdict(collections.Counter)
for row in rows:by_day[str(row['day'])].update(e['kind'] for e in row['effects'])
result=dict(run=a.run,native_actions=len(actions),event_counts=dict(counts),by_day={k:dict(v) for k,v in by_day.items()},evidence=rows,
 failures=[dict(command_id=cid,day=e['day'],time=e['time'],skill=e['payload'].get('skill'),error=e['payload'].get('error'),source=e['source']) for cid,e in actions.items() if e['payload'].get('status')=='failed'],
 scope='One native_diary_receipt per command; failed and partial actions retained. A selected route is not proof of a cleared obstacle. No 14-day gate verdict.')
a.out.write_text(json.dumps(result,ensure_ascii=False,indent=2));print(json.dumps(dict(actions=len(actions),counts=counts,failed=len(result['failures'])),ensure_ascii=False))
