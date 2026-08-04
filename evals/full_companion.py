"""Final integrated phase. Uses actual AgentLab state; never edits success counters.

Fixtures are explicit and separate from assertions. Failures are retained in the report,
including when later cases can still run. No fake LLM is used in the model cases.
"""
from pathlib import Path
import json, sys, time, uuid, traceback
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge, ROOT

class Suite:
    def __init__(self):
        self.b=Bridge();self.b.state();self.cases=[]
        assert self.b.state()['player']['name']=='AgentLab'
    def scenario(self,name,**data):
        return self.b.request('POST','/lab/together',dict(session_id=self.b.session,scenario=name,**data))
    def state(self):return self.b.request('GET','/lab/together')
    def actor(self,name='Abigail'):return next(a for a in self.b.state()['actors'] if a['name']==name)
    def until(self,fn,seconds=60):
        end=time.monotonic()+seconds
        while time.monotonic()<end:
            value=fn()
            if value:return value
            time.sleep(.2)
        raise AssertionError('timed out waiting for observed condition')
    def fixture(self,name='production'):
        self.scenario('auto',enabled=0);self.scenario('close');self.scenario('cancel')
        self.scenario('warp_player',location='Farm',x=44,y=23)
        self.until(lambda:self.b.state()['location']=='Farm',15)
        self.scenario(name);self.scenario('time',value=1000)
    def action(self,skill,wait=True,**extra):
        a=self.actor();target=next((c for c in a['candidates'] if c['skill']==skill),None)
        if skill not in ('travel','follow','rest','guard','fish','dismiss'):
            assert target,('no actual candidate',skill,a)
            extra['target_id']=target['target_id']
        body=dict(session_id=self.b.session,command_id=uuid.uuid4().hex,actor_id=a['id'],skill=skill,**extra)
        r=self.b.request('POST','/commands',body)
        if wait:
            r=self.until(lambda:self.terminal(r['command_id']),300)
            assert r['status']=='succeeded',r
            assert self.b.request('POST','/commands',body)==r,'receipt changed on replay'
        return r
    def terminal(self,id):
        r=self.b.request('GET','/commands/'+id)
        return r if r['status']!='running' else None
    def case(self,name,fn):
        print('START',name,flush=True);start=time.monotonic()
        try:
            evidence=fn();self.cases.append(dict(name=name,passed=True,seconds=time.monotonic()-start,evidence=evidence))
        except Exception as e:
            self.cases.append(dict(name=name,passed=False,seconds=time.monotonic()-start,error=str(e),trace=traceback.format_exc(limit=3)))
            for r in self.b.state().get('commands',[]):
                if r['status']=='running':
                    try:self.b.request('POST','/commands/'+r['command_id']+'/cancel',{'session_id':self.b.session})
                    except Exception:pass
        self.save();print('PASS' if self.cases[-1]['passed'] else 'FAIL',name,flush=True)
    def save(self):
        out=ROOT/'outputs/full-companion-report.json';out.parent.mkdir(exist_ok=True)
        out.write_text(json.dumps(dict(cases=self.cases,passed=all(c['passed'] for c in self.cases)),ensure_ascii=False,indent=2))
    def production(self):
        self.fixture();start=self.b.state()['player']['inventory'];proof=[]
        for skill in ['till','plant','till','plant']:proof.append(self.action(skill))
        f=self.scenario('configure')['facts']
        planted=[c for c in f['Crops'] if c['x'] in (45,46) and c['y']==28]
        assert len(planted)==2 and all(not c['ripe'] for c in planted),planted
        assert self.b.state()['player']['inventory']==start
        chest=next(c for c in self.b.state()['storage'] if c['role']=='supplies')
        assert chest['items'].get('(O)472:0')==7,chest
        return proof
    def feeding(self):
        self.fixture();state=self.state();care=next(c for c in state['facts']['CareLocations'] if c['Skill']=='feed')
        before=state['facts']['HayInSilo'];travel=self.action('travel',destination=care['Location']);feed=self.action('feed')
        after=self.scenario('configure')['facts']
        assert after['HayInSilo']==before-1 and after['FeedNeeded']==0,after
        self.action('travel',destination='Farm')
        return dict(travel=travel,feed=feed,before=before,after=after['HayInSilo'])
    def shipping(self):
        self.fixture('economy');money=self.state()['facts']['Money'];r=self.action('ship')
        state=self.scenario('configure');assert state['facts']['Money']==money,'shipping invented instant gold'
        chest=next(c for c in self.b.state()['storage'] if c['role']=='sell');assert chest['items']=={},chest
        assert state['facts']['Transactions'] and ':ship:' in state['facts']['Transactions'][-1]
        return r
    def buying(self):
        self.fixture('economy');self.scenario('warp_actor',npc='Pierre',location='SeedShop',x=4,y=17)
        self.action('travel',destination='SeedShop');before=self.state()['facts']['Money']
        buys=[self.action('buy'),self.action('buy')];state=self.scenario('configure')
        assert state['facts']['Purchased'].get('lab-seeds')==2,state
        assert 0<state['facts']['SpentToday']<=100 and state['facts']['Money']==before-state['facts']['SpentToday']
        assert not any(c['skill']=='buy' for c in self.actor()['candidates'])
        self.action('travel',destination='Farm');return buys
    def removed_supplies(self):
        self.fixture();r=self.action('plant',wait=False) if any(c['skill']=='plant' for c in self.actor()['candidates']) else None
        if r is None:self.action('till');r=self.action('plant',wait=False)
        self.scenario('supply_remove');done=self.until(lambda:self.terminal(r['command_id']))
        assert done['status']!='succeeded',done
        assert not done['evidence']['effect_by_actor'],done
        return done
    def quiet(self):
        self.fixture();self.scenario('social',mode='quiet');self.scenario('preset',name='钓鱼搭子')
        self.scenario('auto',enabled=1,interval=15);initial=self.state()['person']['Social']['Relationship'].copy()
        self.until(lambda:self.state()['person'].get('Job'),100)
        state=self.state();assert state['person']['Social']['Openings']==0,state['person']['Social']
        assert state['person']['Social']['Relationship']['Comfort']>=initial['Comfort']
        self.scenario('auto',enabled=0);self.scenario('cancel');return state['person']['Job']
    def forced(self):
        self.fixture();before=self.state()['person']['Social']['Relationship']['Comfort']
        self.scenario('plan',forced=1,steps=[dict(skill='water',count=2)])
        self.until(lambda:self.state()['person']['Job']['Status']=='fulfilled',90)
        after=self.state()['person']['Social']['Relationship']['Comfort'];assert after==max(0,before-2),(before,after)
        return self.state()['person']['Job']
    def real_model(self):
        self.fixture();self.scenario('preset',name='农场伙伴');calls=self.state()['calls']
        self.scenario('model',message='请帮我翻一格已授权种植区的土，再播一粒原料箱里的防风草种子。')
        self.until(lambda:not self.state()['thinking'],45)
        state=self.state();assert state['calls']==calls+1,state
        assert state['person']['Chat'][-1]['Who']!='提示',state['notice']
        if state['person'].get('Job') and state['person']['Job']['Status']=='active':
            self.until(lambda:self.state()['person']['Job']['Status'] not in ('active','waiting'),180)
        return dict(calls=self.state()['calls']-calls,chat=self.state()['person']['Chat'][-3:],job=self.state()['person'].get('Job'))
    def run(self):
        for name,fn in [('A02-production',self.production),('P3-building-feeding',self.feeding),('P3-shipping',self.shipping),('P3-native-shopping-budget',self.buying),
                        ('A09-supply-removal',self.removed_supplies),('A15-quiet-autonomy',self.quiet),('A07-force-once',self.forced),('A06-real-model-production',self.real_model)]:
            self.case(name,fn)
        return all(c['passed'] for c in self.cases)

if __name__=='__main__':sys.exit(0 if Suite().run() else 1)
