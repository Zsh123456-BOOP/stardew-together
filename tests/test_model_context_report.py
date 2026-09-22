import json
from pathlib import Path
import tempfile
import unittest
from scripts.report_model_context import summarize


class ModelContextReportTests(unittest.TestCase):
    def test_retries_and_missing_usage_are_not_reported_as_free_success(self):
        rows=[
            ('one','context_budget',{'tools':['work.run'],'tool_count':1}),
            ('one','request',{'body':json.dumps({'messages':[{'content':'规则'},{'content':'{}'}]})}),
            ('one','response',{'status':200,'body':json.dumps({'usage':{'prompt_tokens':100,'completion_tokens':20,'prompt_cache_hit_tokens':50},'choices':[{'finish_reason':'stop','message':{'content':'{"calls":[{"tool":"world.read"}]}"}'}}]})}),
            ('two','request',{'body':json.dumps({'messages':[{'content':'规则'},{'content':'{}'}]})}),
            ('two','transport_failure',{'type':'HttpRequestException'})]
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'trace.jsonl';path.write_text('\n'.join(json.dumps(dict(call_id=i,kind=k,payload=p,utc='2026-09-22T00:00:00Z')) for i,k,p in rows))
            r=summarize(path)
        self.assertEqual(r['requests'],2)
        self.assertEqual(r['missing_usage'],1)
        self.assertEqual(r['input_tokens']['total'],100)
        self.assertEqual(r['cache_hit_ratio'],.5)
        self.assertEqual(r['formats'],{'known_suffix':1,'no_reply':1})
        self.assertEqual(r['undisclosed_calls'],[{'call_id':'one','tools':['world.read']}])


if __name__=='__main__':unittest.main()
