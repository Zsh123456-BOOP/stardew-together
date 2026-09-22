"""Read-only operating-loop evidence report. Never treats fixtures/nights as G3/G4 acceptance."""
import argparse
from collections import Counter
from datetime import datetime
import json
from pathlib import Path


def summarize(root, run_id=None):
    rows=[]
    for path in sorted(Path(root).glob('**/day-*.jsonl')):
        for line_no,line in enumerate(path.read_text().splitlines(),1):
            row=json.loads(line)
            if run_id and row.get('run')!=run_id:continue
            rows.append((row,dict(file=str(path.resolve()),line=line_no)))
    rows.sort(key=lambda entry:entry[0].get('utc',''))
    days={};observations={};deliveries={};chain=[]
    def state(day):
        return days.setdefault(day,dict(day=day,cash_first=None,cash_last=None,actual_spend_gold=0,pending_shipping_estimate=None,ledger_first=None,ledger_last=None,native_saves=[],sleep=[],failures=[],tool_rejections=[],failure_releases=[],delivery_delays=[],slow_frames=[],performance=None,quality=None,finalized_quality=None,candidate_decisions=[]))
    for row,source in rows:
        day=row.get('day',-1);d=state(day);kind=row['kind'];p=row.get('payload',{})
        if not isinstance(p,dict):continue
        event=dict(time=row.get('time'),utc=row.get('utc'),source=source)
        if kind=='query_completed':observations[p['Id']]=(row,p,source)
        if kind=='query_results_delivered':
            for identity in p.get('result_ids',[]):deliveries.setdefault(identity,(row,source))
        if kind=='purchase_ledger':d['actual_spend_gold']=max(d['actual_spend_gold'],p.get('Spent',0))
        ledger=p if kind=='business_snapshot' else p.get('ledger') if kind in ('quality_bedtime','quality_day_start') else None
        if ledger:
            cash=ledger.get('cash')
            if d['cash_first'] is None:d['cash_first']=cash
            d['cash_last']=cash;d['pending_shipping_estimate']=sum(x.get('estimated_sale',0) for x in ledger.get('pending_shipping',[]))
            boundary=dict(**event,evidence={k:ledger.get(k) for k in ('cash','total_earned','pending_shipping','inventory','crops','animals','buildings','machines','farm_investment')})
            if d['ledger_first'] is None:d['ledger_first']=boundary
            d['ledger_last']=boundary
        if kind=='quality_bedtime':d['quality']=dict(**event,**{k:p.get(k) for k in ('SleepTime','ActualAwakeMinutes','TimeBreakdownMinutes','WallSecondsByState','WallDetailSeconds','TransportMinutes','TransportWallSeconds')});d['performance']=p.get('performance')
        # These events are written after the date advances: attribute them to the
        # explicit measured/slept day, never to the log file's new morning.
        if kind=='survival_day_quality':state(p['day'])['finalized_quality']=dict(**event,evidence=p)
        if kind=='native_saved':
            slept_day=p.get('NativeSleepRequestedDay',day)
            state(slept_day if isinstance(slept_day,int) and slept_day>=0 else day)['native_saves'].append(dict(**event,evidence=p))
        if kind in ('survival_sleep_started','sleep_reassessment','survival_transition'):d['sleep'].append(dict(**event,kind=kind,evidence=p))
        if kind=='task_started' and p.get('tool')=='player.sleep':d['sleep'].append(dict(**event,kind='scheduled_sleep',initiator=p.get('source'),purpose=p.get('purpose')))
        if kind in ('decision_failure','model_unavailable','task_finished','service_window_blocked','execution_failure_stop') and (p.get('error') or kind!='task_finished'):d['failures'].append(dict(**event,kind=kind,evidence=p))
        if kind=='tool_result' and isinstance(p.get('result'),dict) and (p['result'].get('error') or p['result'].get('status') in ('failed','blocked','no_feasible_plan')):d['tool_rejections'].append(dict(**event,evidence=p))
        if kind in ('failure_condition_released','constraint_released'):d['failure_releases'].append(dict(**event,evidence=p))
        if kind=='slow_frame':d['slow_frames'].append(dict(**event,**p))
        if kind=='candidate_adoption':d['candidate_decisions'].append(dict(**event,evidence=p))
        if kind in ('shop_quote_observed','seed_selection','plant_native_outcomes','purchase_ledger','native_saved','production_cycle_verified'):chain.append(dict(day=day,**event,kind=kind,evidence=p))
        if kind=='native_diary_receipt':
            effects=[e for e in p.get('effects',[]) if e.get('kind') in ('native_purchase','native_shipment') or e.get('shipped') or e.get('work_skill') in ('plant','water','harvest')]
            if effects:chain.append(dict(day=day,**event,kind='native_receipt',command_id=p.get('command_id'),status=p.get('status'),effects=effects))
    pending=[]
    for identity,(created,p,source) in observations.items():
        delivered=deliveries.get(identity)
        if delivered:
            seconds=(datetime.fromisoformat(delivered[0]['utc'].replace('Z','+00:00'))-datetime.fromisoformat(created['utc'].replace('Z','+00:00'))).total_seconds()
            state(created['day'])['delivery_delays'].append(dict(result_id=identity,kind=p.get('Kind','query'),tool=p.get('Tool'),seconds=seconds,created=source,delivered=delivered[1]))
        else:pending.append(dict(result_id=identity,kind=p.get('Kind','query'),source=source))
    return dict(scope='source-backed observations, not acceptance; fixture date jumps invalidate quality percentages',run_ids=sorted({r.get('run','') for r,_ in rows}),events=len(rows),event_counts=dict(Counter(r['kind'] for r,_ in rows)),completed_days=[k for k in sorted(days) if days[k]['finalized_quality'] and days[k]['native_saves']],days=[days[k] for k in sorted(days)],pending_observations=pending,economic_chain_evidence=chain,gates=dict(G3='unset_requires_completed_baseline_and_user_threshold',G4='manual_join_required_no_inferred_reinvestment',fourteen_day_acceptance=False))


def main():
    p=argparse.ArgumentParser();p.add_argument('--logs',type=Path,required=True);p.add_argument('--run-id');p.add_argument('--output',type=Path,required=True);a=p.parse_args()
    report=summarize(a.logs,a.run_id);a.output.parent.mkdir(parents=True,exist_ok=True);a.output.write_text(json.dumps(report,ensure_ascii=False,indent=2))
    print(json.dumps(dict(events=report['events'],days=len(report['days']),pending=len(report['pending_observations']),output=str(a.output)),ensure_ascii=False))
if __name__=='__main__':main()
