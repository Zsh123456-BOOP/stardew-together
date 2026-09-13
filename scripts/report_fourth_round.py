"""Fourth-round receipt, selection, planting and intent evidence; no gate verdicts."""
import argparse,collections,json
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('--logroot',type=Path,required=True);p.add_argument('--run',required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
events=[]
for path in sorted(a.logroot.rglob('day-*.jsonl')):
 for n,line in enumerate(path.read_text().splitlines(),1):
  try:e=json.loads(line)
  except ValueError:continue
  if e.get('run')==a.run:e['source']={'file':str(path),'line':n};events.append(e)
events.sort(key=lambda e:e['utc'])
def payload(e):return e['payload'] if isinstance(e.get('payload'),dict) else {}
def brief(e):return {k:e[k] for k in ('day','time','utc','kind','payload','source') if k in e}
days={}
for day in sorted({e['day'] for e in events}):
 rows=[e for e in events if e['day']==day];receipts={};calls=collections.Counter();retries=collections.Counter();last={}
 for e in rows:
  d=payload(e)
  if e['kind']=='tool_result':calls[d.get('tool','unknown')]+=1
  if e['kind']=='action_result' and d.get('command_id'):receipts[d['command_id']]=e
 for e in receipts.values():
  d=payload(e);key=(d.get('actor','player'),d.get('skill',d.get('goal','')),d.get('location',''))
  old=last.get(key)
  if old in ('already_satisfied','no_candidates_found','failed'):retries[old]+=1
  last[key]=d.get('disposition')
 selections=[]
 for e in rows:
  if e['kind']!='seed_selection':continue
  quotes=[q for q in rows if q['kind']=='shop_quote_observed' and q['utc']<=e['utc']]
  quote=payload(quotes[-1]).get('quote',{}).get('seed_decision',{}) if quotes else {}
  d=payload(e);recommendation=quote.get('algorithm_recommendation')
  selections.append(dict(**brief(e),candidate_count=len(d.get('candidates',[])),algorithm_recommendation=recommendation,model_selected_recommendation=recommendation in d.get('chosen',{}) if recommendation else None))
 coverage=[payload(e) for e in rows if e['kind']=='overlay_intent_sample']
 days[day]=dict(tool_calls=dict(calls),selections=selections,
  receipt_dispositions=dict(collections.Counter(payload(e).get('disposition','missing') for e in receipts.values())),following_attempts=dict(retries),
  receipt_retry_note='按角色/技能/地图归组的后续尝试数；需查看原始参数与条件变化判断是否无效重试，不能等同盲重试',
  planting_events=[brief(e) for e in rows if e['kind'] in ('farm_purchase_partial_recovery','farm_investment_blocked','farm_investment_plan','farm_investment_complete')],
  labor_reservations=[brief(e) for e in rows if e['kind']=='labor_reservation'],
  intent=dict(samples=len(coverage),covered=sum(bool(d.get('covered')) for d in coverage),coverage=sum(bool(d.get('covered')) for d in coverage)/len(coverage) if coverage else None,unit='action/intent transitions, not frames'),
  ledger=[brief(e) for e in rows if e['kind']=='purchase_ledger'])
a.out.write_text(json.dumps(dict(run=a.run,days=days,note='与A–E及第三轮报告并用；原生购买/播种/浇水需要按任务与真实effects核验。'),ensure_ascii=False,indent=2))
