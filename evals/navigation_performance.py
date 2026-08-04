"""Verify repaired search on real core maps and sample warm autonomous updates."""
from full_companion import Suite
from pathlib import Path
import json,time,sys
class Navigation(Suite):
    def save(self):Path('outputs/navigation-performance.json').write_text(json.dumps(dict(cases=self.cases,passed=all(c['passed'] for c in self.cases)),ensure_ascii=False,indent=2))
    def fishing(self):
        self.fixture();self.scenario('performance_reset')
        receipts=[]
        for destination in ('Farm','Beach'):
            if self.actor()['location']!=destination:self.action('travel',destination=destination)
            receipts.append(self.action('fish',seconds=30))
        assert all(r['evidence']['activity_seconds']>=30 for r in receipts)
        return dict(receipts=receipts,body=self.b.state()['performance'],orchestrator=self.state()['performance'])
    def warm_autonomy(self):
        self.fixture();self.scenario('preset',name='农场伙伴');self.scenario('budget_limit',value=1)
        self.scenario('auto',enabled=1,interval=15);time.sleep(12);self.scenario('performance_reset')
        time.sleep(60)
        data=dict(body=self.b.state()['performance'],orchestrator=self.state()['performance'],actors=len(self.b.state()['actors']),seconds=60,autonomy=True,job=self.state()['person']['Job'])
        self.scenario('auto',enabled=0);self.scenario('budget_limit',value=24)
        assert data['orchestrator']['samples']>1000
        return data
    def run(self):
        self.case('A03-shore-search-farm-and-beach',self.fishing)
        self.case('A17-warm-autonomous-performance',self.warm_autonomy)
        return all(c['passed'] for c in self.cases)
if __name__=='__main__':sys.exit(0 if Navigation().run() else 1)
