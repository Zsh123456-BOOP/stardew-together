"""U1-U8 evidence attribution. Read-only; distinguish exact joins from inference."""
import argparse,json,collections,statistics
from pathlib import Path

def rows(path):
 for n,line in enumerate(path.open(),1):
  try:r=json.loads(line)
  except ValueError:continue
  yield dict(r,source=str(path)+':'+str(n))
def family(r):
 i=r['Id'];t=r['Tool']
 return 'monetize' if i.startswith('sell:') else 'expand' if i.startswith(('plan:owned','inspect:seed')) or i=='select:seeds' else 'quest' if i.startswith('accept:') or i in ('open:quest-board','inspect:quest-board') else 'knowledge' if t.startswith('knowledge.') else 'goal' if i.startswith('advance:') else i.split(':')[0]
def report(logroot,run):
 events=sorted([r for f in logroot.rglob('day-*.jsonl') for r in rows(f) if r.get('run')==run],key=lambda r:r['utc'])
 def of(kind):return [r for r in events if r.get('kind')==kind]
 def d(r):return r.get('payload',{}) if isinstance(r.get('payload'),dict) else {}
 produced=collections.Counter();adopted=collections.Counter();outcomes=collections.defaultdict(collections.Counter)
 # Task ids may be reused on later days. Join only after this submission and
 # before the next submission with the same id, never to an unrelated final.
 submissions=of('candidate_adoption')
 def task_event(e,kind):
  tid=d(e).get('task_id')
  if not tid:return None
  next_submit=next((r['utc'] for r in submissions if r['utc']>e['utc'] and d(r).get('task_id')==tid),None)
  return next((d(r) for r in of(kind) if d(r).get('id')==tid and r['utc']>=e['utc'] and (next_submit is None or r['utc']<next_submit)),None)
 for e in of('operating_candidates'):
  for r in e['payload']:produced[family(r)]+=1
 for e in of('candidate_adoption'):
  p=d(e)
  for c in p.get('candidates',[]):
   f=c['family'];adopted[f]+=1;result=task_event(e,'task_finished')
   outcomes[f]['task:'+result.get('state','unknown') if result else 'receipt:'+(p.get('status') or 'observation_returned')]+=1
 calls=collections.Counter(d(r).get('tool') for r in of('tool_result'))
 shipping=of('shipment_sequence');service=of('declaration_rejected');plots=of('plot_choice_evidence');native=[]
 for e in of('action_result'):
  for effect in d(e).get('effects',[]):
   if isinstance(effect,dict) and str(effect.get('kind','')).startswith('service_counter'):native.append(dict(day=e['day'],time=e['time'],effect=effect,source=e['source']))
 requests=[];responses=[]
 for f in logroot.rglob('model-'+run+'.jsonl'):
  for e in rows(f):
   p=e.get('payload',{})
   try:b=json.loads(p.get('body','{}'))
   except ValueError:continue
   if e.get('kind')=='request':
    context={};system=0
    for m in b.get('messages',[]):
     if m['role']=='system':system+=len(m['content'])
     if m['role']=='user':
      try:context=json.loads(m['content'])
      except ValueError:pass
    sections={k:len(json.dumps(v,ensure_ascii=False,separators=(',',':'))) for k,v in context.items()}
    requests.append(dict(call_id=e['call_id'],utc=e['utc'],source=e['source'],system_characters=system,sections=sections,total_user_characters=sum(sections.values())))
   if e.get('kind')=='response' and 'usage' in b:responses.append(dict(call_id=e['call_id'],usage=b['usage']))
 usage={r['call_id']:r['usage'] for r in responses};totals=collections.Counter();sectionmean={};allkeys={k for r in requests for k in r['sections']}
 for r in requests:
  r['usage']=usage.get(r['call_id']);totals.update({k:v for k,v in (r['usage'] or {}).items() if isinstance(v,(int,float))})
 for k in allkeys:sectionmean[k]=sum(r['sections'].get(k,0) for r in requests)/max(1,len(requests))
 social=[dict(day=e['day'],time=e['time'],**d(e),source=e['source'],task_started=task_event(e,'task_started'),task_outcome=task_event(e,'task_finished')) for e in of('candidate_adoption') if d(e).get('tool')=='player.social']
 return dict(run=run,candidate_coverage=dict(produced),candidate_adoption_exact=dict(adopted),candidate_outcomes_exact={k:dict(v) for k,v in outcomes.items()},attribution_limits='Exact argument-subset matching includes queued plan.submit children; modified arguments/quantities need raw-call audit. Candidate counts are repeated decision-time exposures, not unique opportunities. A returned observation is not an executed action.',tool_calls=dict(calls),shipping=shipping,shipping_time_distribution=dict(collections.Counter('before_1700' if e['time']<1700 else 'after_1700' for e in shipping if d(e).get('phase')=='start')),social=social,social_counts=dict(attempts=len(social),not_queued_failures=sum(x.get("status")=="failed" and not x.get("task_id") for x in social),task_started=sum(x.get("task_started") is not None for x in social),succeeded=sum((x.get("task_outcome") or {}).get("state")=="succeeded" for x in social),without_basis_attempts=sum(x.get("basis") is None for x in social),without_basis_started=sum(x.get("basis") is None and x.get("task_started") is not None for x in social),without_basis_succeeded=sum(x.get("basis") is None and (x.get("task_outcome") or {}).get("state")=="succeeded" for x in social)),U7=dict(plots=plots,shipment_subactions=shipping,storage_transfers=of('loadout_transferred')+of('supply_verified'),service_declarations=service,service_native=native),U8=dict(requests=requests,mean_section_characters=dict(sorted(sectionmean.items(),key=lambda p:-p[1])),input_tokens=totals['prompt_tokens'],output_tokens=totals['completion_tokens'],cache_hit_tokens=totals['prompt_cache_hit_tokens'],cache_hit_rate=totals['prompt_cache_hit_tokens']/totals['prompt_tokens'] if totals['prompt_tokens'] else None,mean_input_tokens=totals['prompt_tokens']/len(responses) if responses else None,note='Section attribution uses JSON characters, NOT estimated tokens; total token/cache numbers are actual API usage. No compression settings changed.'),protection=of('protection_visible'),warnings=of('night_warning'))
if __name__=='__main__':
 p=argparse.ArgumentParser();p.add_argument('--logroot',type=Path,required=True);p.add_argument('--run',required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args();a.out.write_text(json.dumps(report(a.logroot,a.run),ensure_ascii=False,indent=2))
