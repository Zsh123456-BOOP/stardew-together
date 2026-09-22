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
