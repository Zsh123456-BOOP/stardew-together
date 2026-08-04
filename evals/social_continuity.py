"""Real model boundaries and saved social continuity in AgentLab."""
from full_companion import Suite
from pathlib import Path
import json,time,sys

class SocialSuite(Suite):
    def save(self):
        Path('outputs/social-runtime-report.json').write_text(json.dumps(dict(cases=self.cases,passed=all(c['passed'] for c in self.cases)),ensure_ascii=False,indent=2))
    def late_reply(self):
        self.fixture();before=self.state()['calls'];self.scenario('model',message='帮我浇两格水，做完告诉我。')
        self.scenario('cancel');time.sleep(32)
        state=self.state();assert not state['thinking']
        assert state['person'].get('Job',{}).get('Status') not in ('active','fulfilled'),state['person'].get('Job')
        assert len([c for c in self.actor()['candidates'] if c['skill']=='water'])==2
        assert state['calls']==before+1
        return dict(cancelled_reply_never_dispatched=True,request_still_counted=True)
    def recall(self):
        self.fixture();self.scenario('model',message='记住：我给南边两格菜地起名叫小月亮，不喜欢别人擅自卖那里的收获。')
        self.scenario('model',message='我刚给南边那两格地起的名字是什么？只聊这个，先不用干活。')
        self.until(lambda:not self.state()['thinking'],45);state=self.state()
        reply=state['person']['Chat'][-1];assert reply['Who']!='提示' and '小月亮' in reply['Text'],reply
        return dict(reply=reply,preferences=state['person']['Social']['Preferences'])
    def unwilling(self):
        self.fixture();self.scenario('preset',name='钓鱼搭子')
        self.scenario('profile',likes='安静钓鱼',dislikes='非常害怕矿洞，今天不想挖矿，想商量换成钓鱼')
        self.scenario('model',message='我们去挖三块石头吧。如果你今天不想做，请直接说你的想法，不用勉强。')
        self.until(lambda:not self.state()['thinking'],45);state=self.state();p=state['person']
        proposal=p.get('Proposal');assert proposal and proposal['decision'] in ('refuse','negotiate'),p['Chat'][-1]
        assert not p.get('Job') or p['Job']['Status'] not in ('active','waiting')
        return dict(reply=p['Chat'][-1],decision=proposal['decision'])
    def budget(self):
        self.fixture();self.scenario('preset',name='农场伙伴');self.scenario('social',mode='quiet')
        calls=self.state()['calls'];assert calls>=1
        self.scenario('budget_limit',value=1);self.scenario('auto',enabled=1,interval=15)
        try:
            self.until(lambda:self.state()['person']['Life']['DecisionSource']=='local-budget-or-offline' and self.state()['person'].get('Job',{}).get('Status')=='fulfilled',180)
            state=self.state();assert state['calls']==calls
            return dict(source=state['person']['Life']['DecisionSource'],job=state['person']['Job'],no_extra_call=True)
        finally:self.scenario('auto',enabled=0);self.scenario('budget_limit',value=24);self.scenario('cancel')
    def service_error(self):
        self.fixture();self.scenario('preset',name='农场伙伴');self.scenario('model_name',value='together-unavailable-test-model')
        self.scenario('auto',enabled=1,interval=15)
        try:
            self.until(lambda:self.state()['person']['Life']['DecisionSource']=='local-after-model-error',60)
            self.until(lambda:self.state()['person'].get('Job',{}).get('Status')=='fulfilled',180)
            state=self.state();assert '400' in state['person']['Life']['LastDecisionError'],state['person']['Life']
            return dict(error=state['person']['Life']['LastDecisionError'],fallback=state['person']['Job'])
        finally:self.scenario('auto',enabled=0);self.scenario('model_name',value='deepseek-flash');self.scenario('cancel')
    def run(self):
        for name,fn in [('A11-late-reply',self.late_reply),('P5-real-model-preference-recall',self.recall),('A06-real-model-personality-negotiation',self.unwilling),('A11-budget-local-autonomy',self.budget),('A11-real-service-rejection-fallback',self.service_error)]:self.case(name,fn)
        return all(c['passed'] for c in self.cases)

if __name__=='__main__':sys.exit(0 if SocialSuite().run() else 1)
