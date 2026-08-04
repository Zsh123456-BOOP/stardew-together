"""Same-request real personality decisions and preserved timed autonomous work."""
from full_companion import Suite
from pathlib import Path
import json,time,sys

class Interruption(Suite):
    def save(self):
        Path('outputs/interruption-companion-report.json').write_text(json.dumps(dict(cases=self.cases,passed=all(c['passed'] for c in self.cases)),ensure_ascii=False,indent=2))
    def personalities(self):
        results=[];message='我们去挖三块石头吧。如果你今天不想做，请直接说你的想法，不用勉强。'
        for preset,likes,dislikes,wanted in [('冒险搭子','最喜欢挖矿，今天很想活动一下','无','accept'),('钓鱼搭子','安静钓鱼','非常害怕矿洞，今天不想挖矿','refuse')]:
            self.fixture();self.scenario('fresh_person');self.scenario('preset',name=preset);self.scenario('profile',likes=likes,dislikes=dislikes)
            self.scenario('model',message=message);self.until(lambda:not self.state()['thinking'],45)
            state=self.state();p=state['person'];proposal=p.get('LastDecision');job=p.get('Job')
            if wanted=='accept':
                assert job and job['Status'] in ('active','fulfilled') and any(s['skill']=='mine' for s in job['Steps']),p['Chat'][-1]
                self.until(lambda:self.state()['person']['Job']['Status']=='fulfilled',100)
            else:assert proposal and proposal['decision'] in ('refuse','negotiate') and (not job or job['Status'] not in ('active','waiting')),p['Chat'][-1]
            results.append(dict(persona=preset,reply=p['Chat'][-1],decision=proposal['decision'] if proposal else 'accepted-and-executing',job=job))
        return results
    def timed_resume(self):
        self.fixture();self.scenario('preset',name='钓鱼搭子');self.scenario('social',mode='quiet')
        self.scenario('configure',policy={'Enabled':False,'DailyBudget':0,'KeepGold':500})
        self.scenario('budget_limit',value=1);assert self.state()['calls']>=1
        # Restore energy through the real rest skill, not by editing a success state.
        for _ in range(2):
            self.scenario('plan',steps=[dict(skill='rest',count=1)])
            self.until(lambda:self.state()['person']['Job']['Status']=='fulfilled',45)
        self.scenario('auto',enabled=1,interval=15)
        def fishing():
            job=self.state()['person'].get('Job')
            if not job or job['Origin']!='autonomous' or job['Status']!='active' or not job.get('Command'):return None
            r=next((r for r in self.b.state()['commands'] if r['command_id']==job['Command']),{})
            return job if r.get('skill')=='fish' and r['evidence']['activity_seconds']>=5 else None
        original=self.until(fishing,180);self.scenario('auto',enabled=0)
        self.scenario('plan',steps=[dict(skill='rest',count=1)])
        state=self.state();saved=next(j for j in state['person']['Life']['Suspended'] if j['Id']==original['Id'])
        assert 1<=saved['RemainingSeconds']<30 and len(saved['PartialReceipts'])==1,saved
        def done():
            job=self.state()['person'].get('Job')
            return job if job and job['Id']==original['Id'] and job['Status']=='fulfilled' else None
        completed=self.until(done,120)
        assert completed['Completed']==1 and len(completed['PartialReceipts'])==1,completed
        self.scenario('budget_limit',value=24)
        return dict(paused=saved,resumed=completed)
    def run(self):
        self.case('A06-same-request-two-real-personalities',self.personalities)
        self.case('A04-autonomous-timed-interruption-resume',self.timed_resume)
        return all(c['passed'] for c in self.cases)

if __name__=='__main__':sys.exit(0 if Interruption().run() else 1)
