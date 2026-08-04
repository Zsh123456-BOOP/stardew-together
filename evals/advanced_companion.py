"""AgentLab boundary cases, live navigation and combat; fixtures never stand in for effects."""
from full_companion import Suite
from pathlib import Path
import json, time, sys

class Advanced(Suite):
    def save(self):
        Path('outputs/advanced-companion-report.json').write_text(json.dumps(dict(cases=self.cases,passed=all(c['passed'] for c in self.cases)),ensure_ascii=False,indent=2))
    def blocked_stock(self):
        self.fixture();self.action('till');self.scenario('reservation',item='(O)472',count=9)
        assert not any(c['skill']=='plant' for c in self.actor()['candidates'])
        self.scenario('reservation',item='(O)472',count=8)
        result=self.action('plant')
        assert not any(c['skill']=='plant' for c in self.actor()['candidates'])
        stock=next(c for c in self.b.state()['storage'] if c['role']=='supplies')['items']
        assert stock['(O)472:0']==8,stock
        return dict(result=result,remaining=stock)
    def full_storage(self):
        self.fixture();self.scenario('full_pouch')
        before=self.b.state()['player']['inventory'];r=self.action('collect',wait=False)
        r=self.until(lambda:self.terminal(r['command_id']))
        assert r['status']!='succeeded' and not r['evidence']['effect_by_actor'],r
        assert any(c['skill']=='collect' for c in self.actor()['candidates'])
        self.scenario('full_output');cargo=self.actor()['cargo']
        assert not any(c['skill']=='deposit' for c in self.actor()['candidates'])
        assert self.actor()['cargo']==cargo and self.b.state()['player']['inventory']==before
        return r
    def season(self):
        self.fixture();self.scenario('day',value=27)
        try:assert not any(c['skill'] in ('till','plant') for c in self.actor()['candidates'])
        finally:self.scenario('day',value=10)
        self.scenario('wet_fields');assert not any(c['skill']=='water' for c in self.actor()['candidates'])
        return dict(late_season_seeds_blocked=True,already_watered_not_repeated=True)
    def gift(self):
        self.fixture();self.scenario('gift_fixture');r=self.action('gift')
        output=next(c for c in self.b.state()['storage'] if c['role']=='output')['items']
        assert output.get('(O)145:0')==1 and not self.actor()['cargo'].get('(O)145:0'),output
        return r
    def mine_stairs(self):
        self.fixture();self.scenario('mine_fixture');first=self.actor()['location']
        r=self.action('travel',destination='UndergroundMine2');assert self.actor()['location']=='UndergroundMine2'
        back=self.action('travel',destination='Mine');assert self.actor()['location']=='Mine'
        return dict(from_map=first,descent=r,exit=back)
    def defense(self):
        self.fixture();self.scenario('mine_fixture')
        self.scenario('warp_player',location='UndergroundMine1',x=self.actor()['tile'][0],y=self.actor()['tile'][1]+2)
        kills=self.scenario('configure')['facts']['Progress']['monster_kills']
        r=self.action('rest',wait=False,seconds=20);time.sleep(1);self.scenario('combat_fixture')
        seen=[];end=time.monotonic()+100
        while time.monotonic()<end:
            r=self.b.request('GET','/commands/'+r['command_id']);seen.append(r['evidence'].get('combat_paused'))
            if r['status']!='running':break
            time.sleep(.15)
        assert r['status']=='succeeded' and r['evidence']['resumes']>=1 and any(seen),r
        after=self.scenario('configure')['facts']['Progress']['monster_kills'];assert after>kills,(kills,after)
        return dict(command=r,native_monster_kills_before=kills,native_monster_kills_after=after)
    def obstacles(self):
        self.fixture();r=self.action('travel',wait=False,destination='Beach')
        a=self.until(lambda:self.actor() if len(self.actor().get('path_preview',[]))>=4 else None,20)
        x,y=a['path_preview'][3];location=a['location']
        self.scenario('obstacle',location=location,x=x,y=y);samples=[]
        try:
            end=time.monotonic()+150
            while time.monotonic()<end:
                a=self.actor();samples.append((time.monotonic(),a['location'],a['tile']))
                r=self.b.request('GET','/commands/'+r['command_id'])
                if r['status']!='running':break
                time.sleep(.1)
            assert r['status']=='succeeded',r
            assert not any(m==location and tile==[x,y] for _,m,tile in samples),'walked through added obstacle'
            for left,right in zip(samples,samples[1:]):
                if left[1]==right[1]:assert sum(abs(a-b) for a,b in zip(left[2],right[2]))<=max(3,int((right[0]-left[0])*20)),('same-map jump',left,right)
        finally:self.scenario('obstacle',location=location,x=x,y=y,remove=1)
        return dict(command=r,observations=len(samples),obstacle=[location,x,y])
    def closed_shop(self):
        self.fixture('economy');self.scenario('town_key',enabled=0);self.scenario('time',value=700)
        from agent.client import BridgeError
        try:self.action('travel',wait=False,destination='SeedShop')
        except BridgeError as e:assert str(e)=='no_route',str(e)
        else:raise AssertionError('closed shop allowed')
        assert self.scenario('configure')['facts']['SpentToday']==0
        return dict(closed_door_rejected=True,money_unchanged=True)
    def run(self):
        for name,fn in [('A09-exact-reservations',self.blocked_stock),('A10-full-storage',self.full_storage),('P3-season-rain',self.season),('P5-physical-gift',self.gift),('P4-mine-stairs',self.mine_stairs),('A05-live-defense-resume',self.defense),('A13-dynamic-obstacle',self.obstacles),('A10-closed-shop',self.closed_shop)]:self.case(name,fn)
        return all(c['passed'] for c in self.cases)

if __name__=='__main__':sys.exit(0 if Advanced().run() else 1)
