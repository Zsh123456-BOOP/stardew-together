"""Final coverage of animal products, litter permissions, event pause and mine lifetime."""
from advanced_companion import Advanced
from pathlib import Path
import json,time,sys

class Finishing(Advanced):
    def save(self):
        Path('outputs/finishing-companion-report.json').write_text(json.dumps(dict(cases=self.cases,passed=all(c['passed'] for c in self.cases)),ensure_ascii=False,indent=2))
    def products(self):
        evidence=[]
        for kind,item in [('White Cow','(O)184'),('Sheep','(O)440')]:
            self.fixture();self.scenario('animal_products',type=kind);before=self.b.state()['player']['inventory']
            for _ in range(5):
                r=self.action('tend',wait=False);r=self.until(lambda:self.terminal(r['command_id']))
                if r['status']=='succeeded':break
                assert r['error'] in ('production_permission_or_target_changed','target_changed_without_actor_evidence'),r
            assert r['status']=='succeeded' and r['evidence']['effect_by_actor'],r
            assert self.actor()['cargo'].get(item+':0')==1,self.actor()['cargo']
            assert self.b.state()['player']['inventory']==before
            assert not any(c['skill']=='tend' for c in self.actor()['candidates'])
            evidence.append(r)
        return evidence
    def forage(self):
        self.fixture();self.scenario('forage_fixture');before=self.b.state()['player']['inventory'];r=self.action('forage')
        assert self.actor()['cargo'].get('(O)18:0')==1 and self.b.state()['player']['inventory']==before
        return r
    def clearing(self):
        self.fixture();self.scenario('clear_fixture');before=self.b.state()['player']['inventory']
        results=[self.action('clear'),self.action('clear')]
        assert not any(c['skill']=='clear' for c in self.actor()['candidates'])
        assert self.actor()['cargo'].get('(O)388:0')==1,self.actor()['cargo']
        assert self.b.state()['player']['inventory']==before
        self.action('till')
        return results
    def event_pause(self):
        self.fixture();r=self.action('water',wait=False)
        self.scenario('event_fixture');self.until(lambda:self.state()['event_up'],10)
        before=self.actor()['tile'];time.sleep(2)
        paused=self.b.request('GET','/commands/'+r['command_id'])
        assert self.state()['event_up'] and self.actor()['tile']==before
        assert paused['status']=='running' and not paused['evidence']['effect_by_actor'],paused
        self.until(lambda:not self.state()['event_up'],15)
        result=self.until(lambda:self.terminal(r['command_id']),45);assert result['status']=='succeeded',result
        return dict(during_event=paused,after_event=result)
    def mine_lifetime(self):
        self.fixture();self.scenario('mine_fixture');start=self.b.state()['game_time']
        assert self.b.state()['location']=='Farm'
        hold=self.action('rest',wait=False,seconds=60)
        time.sleep(20)
        actor=self.actor();assert actor['registered_location'] and actor['location']=='UndergroundMine1',actor
        self.b.request('POST','/commands/'+hold['command_id']+'/cancel',{'session_id':self.b.session})
        assert self.b.state()['game_time']!=start,'game clock did not advance'
        r=self.action('travel',destination='Mine')
        return dict(before_clock=start,after_clock=self.b.state()['game_time'],floor_remained_registered=True,exit=r)
    def run(self):
        for name,fn in [('P3-milk-and-shear',self.products),('P3-forage-cargo',self.forage),('P3-authorized-litter-clearing',self.clearing),('A14-native-event-pause',self.event_pause),('P4-occupied-mine-lifetime',self.mine_lifetime),('A08-live-NPC-kill-credit',self.defense)]:self.case(name,fn)
        return all(c['passed'] for c in self.cases)

if __name__=='__main__':sys.exit(0 if Finishing().run() else 1)
