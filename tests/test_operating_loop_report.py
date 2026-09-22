import json
from pathlib import Path
import tempfile
import unittest
from scripts.report_operating_loop import summarize

class OperatingLoopReport(unittest.TestCase):
    def test_evidence_and_delivery_do_not_imply_acceptance(self):
        with tempfile.TemporaryDirectory() as folder:
            rows=[dict(run='r',day=0,time=600,utc='2026-01-01T00:00:00Z',kind='query_completed',payload=dict(Id='q',Kind='outcome',Tool='work.run')),dict(run='r',day=0,time=610,utc='2026-01-01T00:00:12Z',kind='query_results_delivered',payload=dict(result_ids=['q'])),dict(run='r',day=0,time=620,utc='2026-01-01T00:00:15Z',kind='query_results_delivered',payload=dict(result_ids=['q'])),dict(run='r',day=0,time=600,utc='2026-01-01T00:00:00Z',kind='business_snapshot',payload=dict(cash=500,pending_shipping=[dict(estimated_sale=525)]))]
            (Path(folder)/'day-0.jsonl').write_text('\n'.join(json.dumps(r) for r in rows))
            report=summarize(folder,'r');d=report['days'][0]
            self.assertEqual(d['delivery_delays'][0]['seconds'],12)
            self.assertEqual(len(d['delivery_delays']),1)
            self.assertEqual(d['cash_last'],500)
            self.assertEqual(d['pending_shipping_estimate'],525)
            self.assertFalse(report['gates']['fourteen_day_acceptance'])
            self.assertEqual(summarize(folder,'other')['events'],0)

    def test_overnight_finalization_belongs_to_measured_day(self):
        with tempfile.TemporaryDirectory() as folder:
            def row(day,kind,payload):return dict(run='r',day=day,time=600,utc=f'2026-01-0{day+1}T00:00:00Z',kind=kind,payload=payload)
            rows=[row(0,'quality_bedtime',dict(SleepTime=2200,ActualAwakeMinutes=960,ledger=dict(cash=400,inventory=[dict(Item='(O)472',Count=2)],crops=dict(total=17),pending_shipping=[dict(estimated_sale=525)]))),row(1,'survival_day_quality',dict(day=0,awake_labor_ratio=.3,cash_delta=425)),row(1,'native_saved',dict(day=1,NativeSleepRequestedDay=0)),row(1,'quality_day_start',dict(ledger=dict(cash=925,pending_shipping=[])))]
            (Path(folder)/'day-1.jsonl').write_text('\n'.join(json.dumps(r) for r in rows))
            report=summarize(folder,'r');previous,today=report['days']
            self.assertEqual(report['completed_days'],[0])
            self.assertEqual(previous['cash_last'],400)
            self.assertEqual(today['cash_first'],925)
            self.assertEqual(previous['ledger_last']['evidence']['crops']['total'],17)
            self.assertEqual(previous['finalized_quality']['evidence']['awake_labor_ratio'],.3)
            self.assertEqual(len(previous['native_saves']),1)
            self.assertEqual(today['native_saves'],[])
            self.assertEqual(previous['ledger_last']['source']['line'],1)
