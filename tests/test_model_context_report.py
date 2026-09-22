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

    def test_native_tools_and_schema_bytes_are_counted(self):
        call={'id':'native-1','type':'function','function':{'name':'work__run','arguments':'{"goal":"water"}'}}
        request={'messages':[{'role':'system','content':'规则'},{'role':'assistant','content':None,'tool_calls':[call]},{'role':'tool','tool_call_id':'native-1','content':'{"status":"queued"}'},{'role':'user','content':'{"now":{"day":0,"time":1000}}'}],'tools':[{'type':'function','function':{'name':'work__run','parameters':{'type':'object'}}}]}
        rows=[('context_budget',{'tools':['work.run'],'tool_count':1}),('request',{'body':json.dumps(request)}),('response',{'status':200,'body':json.dumps({'usage':{'prompt_tokens':150,'completion_tokens':30},'choices':[{'finish_reason':'tool_calls','message':{'content':None,'tool_calls':[call]}}]})})]
        with tempfile.TemporaryDirectory() as tmp:
            path=Path(tmp)/'trace.jsonl';path.write_text('\n'.join(json.dumps(dict(call_id='native',kind=k,payload=p,utc='2026-09-22T00:00:00Z')) for k,p in rows))
            report=summarize(path)
        self.assertEqual(report['formats'],{'native_tool_calls':1})
        self.assertEqual(report['tool_calls'],{'work.run':1})
        self.assertEqual(report['per_call'][0]['native_feedback_count'],1)
        self.assertGreater(report['per_call'][0]['tool_schema_characters'],0)
        self.assertGreater(report['input_characters']['total'],sum(len(m.get('content') or '') for m in request['messages']))
        self.assertEqual(report['undisclosed_calls'],[])


if __name__=='__main__':unittest.main()
