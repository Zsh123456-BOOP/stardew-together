"""Read model JSON-body traces without credentials; API usage includes every retry."""
import argparse
from collections import Counter
from datetime import datetime
import json
from pathlib import Path
from statistics import median


def summarize(path):
    calls={}
    for line in Path(path).read_text().splitlines():
        event=json.loads(line);c=calls.setdefault(event['call_id'],{'call_id':event['call_id']})
        kind=event['kind'];p=event['payload']
        if kind=='context_budget':c['packing']=p
        elif kind=='request':
            body=json.loads(p['body']);messages=body['messages']
            c.update(start=event['utc'],input_characters=sum(len(m['content']) for m in messages),system_characters=len(messages[0]['content']),user_characters=len(messages[-1]['content']))
            try:
                context=json.loads(messages[-1]['content']);now=context.get('now',{})
                c.update(observed_day=now.get('day'),observed_time=now.get('time'),location=now.get('location'))
            except (ValueError,AttributeError):pass
        elif kind=='response':
            body=json.loads(p['body']);c.update(end=event['utc'],http_status=p['status'],usage=body.get('usage'))
            if body.get('choices'):
                choice=body['choices'][0];c['finish_reason']=choice.get('finish_reason');raw=choice['message'].get('content') or ''
                try:
                    turn,end=json.JSONDecoder().raw_decode(raw.lstrip());suffix=raw.lstrip()[end:].strip()
                    c['format']='strict_json' if not suffix else 'known_suffix' if suffix in ('</result>','"}') else 'unexpected_suffix'
                    c['tools']=[x.get('tool') for x in turn.get('calls',[])]
                    selected=c.get('packing',{}).get('tools')
                    if selected is not None:c['not_in_current_definitions']=[t for t in c['tools'] if t not in selected]
                except (ValueError,TypeError,AttributeError):c['format']='invalid_json'
        elif kind=='transport_failure':c['transport_failure']=p
    rows=list(calls.values())
    for c in rows:
        if 'start' in c and 'end' in c:c['http_seconds']=(datetime.fromisoformat(c['end'].replace('Z','+00:00'))-datetime.fromisoformat(c['start'].replace('Z','+00:00'))).total_seconds()
    def stats(values):
        values=sorted(v for v in values if isinstance(v,(int,float)))
        if not values:return None
        return dict(count=len(values),total=sum(values),median=median(values),p95=values[min(len(values)-1,int(.95*(len(values)-1)))],max=max(values))
    usage=[c['usage'] for c in rows if c.get('usage')]
    total=sum(u.get('prompt_tokens',0) for u in usage)
    return dict(source=str(Path(path).resolve()),calls=len(rows),requests=sum('start' in c for c in rows),responses=sum('end' in c for c in rows),missing_usage=sum('start' in c and not c.get('usage') for c in rows),
        input_tokens=stats(u.get('prompt_tokens') for u in usage),output_tokens=stats(u.get('completion_tokens') for u in usage),cache_hit_ratio=sum(u.get('prompt_cache_hit_tokens',0) for u in usage)/total if total else None,
        input_characters=stats(c.get('input_characters') for c in rows),http_seconds=stats(c.get('http_seconds') for c in rows),tool_count=stats(c.get('packing',{}).get('tool_count') for c in rows),
        formats=dict(Counter(c.get('format','no_reply') for c in rows)),tool_calls=dict(Counter(t for c in rows for t in c.get('tools',[]))),undisclosed_calls=[dict(call_id=c['call_id'],tools=c['not_in_current_definitions']) for c in rows if c.get('not_in_current_definitions')],per_call=rows)


def main():
    p=argparse.ArgumentParser();p.add_argument('--trace',type=Path,required=True);p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    report=summarize(a.trace);a.output.write_text(json.dumps(report,ensure_ascii=False,indent=2))
    print(json.dumps({k:v for k,v in report.items() if k not in ('per_call','undisclosed_calls')},ensure_ascii=False))
if __name__=='__main__':main()
