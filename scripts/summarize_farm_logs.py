"""Summarize one save/epoch of JSONL traces without replaying game actions."""
import argparse
from collections import Counter
import json
from pathlib import Path


def summarize(folder):
    rows=[]; broken=0
    for file in sorted(Path(folder).glob('day-*.jsonl')):
        for line in file.read_text(encoding='utf-8').splitlines():
            try: rows.append(json.loads(line))
            except (ValueError, TypeError): broken+=1
    tasks={}; usage=[]; snapshots=[]; kinds=Counter()
    for row in rows:
        kind=row.get('kind'); kinds[kind]+=1; p=row.get('payload', {})
        if not isinstance(p,dict): continue
        if kind=='task_finished': tasks[(row.get('epoch'),row.get('run'),p.get('id'))]=p
        elif kind=='model_usage': usage.append(p)
        elif kind=='business_snapshot': snapshots.append((row,p))
    states=Counter(t.get('state','unknown') for t in tasks.values())
    failures=Counter(t.get('error') or t.get('state') for t in tasks.values() if t.get('state') not in ('succeeded','cancelled'))
    sums={k:sum(u.get(k,0) or 0 for u in usage) for k in ('input_tokens','output_tokens','cache_hit_tokens','total_tokens')}
    latency=sorted(u['latency_ms'] for u in usage if isinstance(u.get('latency_ms'),(int,float)))
    daily={}
    for r,p in snapshots:
        key=(r.get('epoch'),r.get('day'))
        daily.setdefault(key,{'epoch':key[0],'day':key[1],'first_cash':p.get('cash'),'first_earned':p.get('total_earned')})
        daily[key].update(last_time=r.get('time'),last_cash=p.get('cash'),last_earned=p.get('total_earned'),crops=p.get('crops'),animals=p.get('animals'),machines=len(p.get('machines',[])),pending_shipping_value=sum(i.get('estimated_sale',0) for i in p.get('pending_shipping',[])))
    return {'rows':len(rows),'malformed_lines':broken,'events':dict(kinds),'unique_tasks':len(tasks),'task_states':dict(states),'failure_reasons':dict(failures.most_common()),'model_calls_logged':len(usage),'tokens':sums,'model_latency_p95_ms':latency[int((len(latency)-1)*.95)] if latency else None,'days':list(daily.values()),'note':'现金/累计收入来自真实快照；待结算不能算现金。日志缺失不是零调用。任务按epoch/run/id去重，不重复累加effects。此汇总不宣称跨季或后期验收通过。'}


def main():
    p=argparse.ArgumentParser();p.add_argument('folder',help='Together/logs/<save>/<epoch>');p.add_argument('--out',required=True);args=p.parse_args()
    result=summarize(args.folder);out=Path(args.out);out.parent.mkdir(parents=True,exist_ok=True);out.write_text(json.dumps(result,ensure_ascii=False,indent=2),encoding='utf-8')
    print(json.dumps({k:result[k] for k in ('rows','unique_tasks','task_states','failure_reasons','model_calls_logged','tokens')},ensure_ascii=False))


if __name__=='__main__':main()
