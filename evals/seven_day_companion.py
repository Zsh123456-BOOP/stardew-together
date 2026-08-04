"""Seven dated activity + native overnight cycles, not seven bare sleeps.

Compressed scenario testing: real game actions, crops, saves and memories. This is
not seven full-length human play days and cannot supply subjective fun ratings.
"""
from full_companion import Suite
from pathlib import Path
from agent.client import BridgeError
import json,time,sys

class Week(Suite):
    def save(self):
        Path('outputs/seven-day-activities.json').write_text(json.dumps(dict(cases=self.cases,passed=all(c['passed'] for c in self.cases),method='compressed activities plus native sleep/save; not human playtesting'),ensure_ascii=False,indent=2))
    def night(self,day):
        self.scenario('bed');self.until(lambda:self.b.state()['location']=='FarmHouse',15)
        self.scenario('sleep');end=time.monotonic()+65
        while time.monotonic()<end:
            try:
                state=self.state()
                if state.get('menu') in ('ShippingMenu','LevelUpMenu'):self.scenario('night_continue')
                if state['facts']['Day']>day and state.get('menu') is None:
                    self.scenario('configure');return self.state()
            except BridgeError:pass
            time.sleep(.4)
        raise AssertionError('native overnight did not finish')
    def run(self):
        resume=Path('outputs/seven-day-activities.json')
        previous=json.loads(resume.read_text())['cases'] if '--resume' in sys.argv and resume.exists() else []
        self.cases=[c for c in previous if c['passed']]
        if not self.cases:
            self.fixture();self.scenario('preset',name='农场伙伴');self.scenario('social',mode='normal');self.scenario('project',kind='farm')
            self.scenario('model',message='记住：我们这周一起照料小月亮菜地，每天留一点时间在农场歇歇。')
            for _ in range(2):self.action('till');self.action('plant')
        start=self.cases[0]['evidence']['day'] if self.cases else self.state()['facts']['Day']
        first_project=self.state()['projects'][0]['Id']
        for index in range(len(self.cases),7):
            def day_activity():
                self.scenario('close');self.scenario('warp_player',location='Farm',x=44,y=23)
                self.until(lambda:self.b.state()['location']=='Farm',15)
                self.until(lambda:any(a['name']=='Abigail' for a in self.b.state()['actors']),30)
                if self.actor()['location']!='Farm':self.action('travel',destination='Farm')
                self.scenario('time',value=1000);day=self.scenario('configure')['facts']['Day']
                assert day==start+index,(day,start,index)
                # A real independent choice each day. Only the API budget is constrained;
                # no chosen goal, item, action result, relationship or diary is injected.
                self.scenario('budget_limit',value=1);prior=(self.state()['person'].get('Job') or {}).get('Id')
                self.scenario('auto',enabled=1,interval=15)
                def own_activity():
                    job=self.state()['person'].get('Job')
                    return job if job and job['Id']!=prior and job['Origin']=='autonomous' and job['Status']=='fulfilled' and job['Completed']>0 else None
                own=self.until(own_activity,180);self.scenario('auto',enabled=0);self.scenario('cancel')
                if self.actor()['location']!='Farm':self.action('travel',destination='Farm')
                actions=[]
                for skill in ('water','harvest','collect','deposit'):
                    for _ in range(12):
                        if not any(c['skill']==skill for c in self.actor()['candidates']):break
                        actions.append(self.action(skill))
                self.action('follow')
                self.until(lambda:self.actor()['location']=='Farm' and sum(abs(a-b) for a,b in zip(self.actor()['tile'],[44,23]))<7,90)
                self.scenario('plan',steps=[dict(skill='rest',count=1)])
                self.until(lambda:self.state()['person'].get('Job',{}).get('Status')=='fulfilled',60)
                self.scenario('model',message=f'日记：小月亮计划第{index+1}天，今天也一起歇了会儿。')
                before=self.scenario('configure');snapshot=dict(day=day,autonomous=own,actions=actions,crops=before['facts']['Crops'],job=before['person']['Job'])
                after=self.night(day)
                assert any(p['Id']==first_project for p in after['projects']),'shared plan forgotten'
                diary=next(d for d in after['person']['Social']['Diary'] if d['Day']==day)
                assert diary['Sources'] and f'第{index+1}天' in diary['PlayerNote'],diary
                assert '小月亮' in ' '.join(after['person']['Social']['Preferences'])
                snapshot['diary']=diary;snapshot['habits']=after['person']['Social']['Habits'];snapshot['next_day']=after['facts']['Day']
                return snapshot
            self.case('real-activities-and-save-day-'+str(index+1),day_activity)
            if not self.cases[-1]['passed']:break
        self.scenario('auto',enabled=0);self.scenario('budget_limit',value=24)
        if len(self.cases)==7 and all(c['passed'] for c in self.cases):
            habits=self.state()['person']['Social']['Habits'];index=next(i for i,h in enumerate(habits) if h['Skill']=='rest' and len(h['Days'])>=3)
            self.scenario('habit',index=index);assert self.state()['person']['Social']['Habits'][index]['Confirmed']
        self.save();return len(self.cases)==7 and all(c['passed'] for c in self.cases)

if __name__=='__main__':sys.exit(0 if Week().run() else 1)
