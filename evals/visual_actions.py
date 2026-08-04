"""Compound production with game-frame capture, then a sourced overnight diary."""
from full_companion import Suite
from seven_day_companion import Week
from pathlib import Path
import json,time,sys
class Visual(Suite):
    def save(self):Path('outputs/visual-actions-report.json').write_text(json.dumps(dict(cases=self.cases,passed=all(c['passed'] for c in self.cases)),ensure_ascii=False,indent=2))
    def prepare(self):
        self.fixture();self.scenario('warp_player',location='Farm',x=44,y=27);self.until(lambda:self.b.state()['player']['tile']==[44,27],15);self.scenario('clear_fixture');Path('work/visual-start.json').write_text(json.dumps(dict(start=time.time())))
        self.scenario('plan',steps=[dict(skill='clear',count=2),dict(skill='till',count=2),dict(skill='plant',count=2)])
    def verify(self):
        self.until(lambda:self.state()['person']['Job']['Status']=='fulfilled',100)
        job=self.state()['person']['Job'];assert job['Completed']==6,job
        crops=[c for c in self.scenario('configure')['facts']['Crops'] if c['y']==28 and c['x'] in (45,46)]
        assert len(crops)==2 and all(not c['ripe'] for c in crops),crops
        frames=Path('work/CompanionMods/Together/screenshots/motion');start=json.loads(Path('work/visual-start.json').read_text())['start']
        self.until(lambda:all((frames/f'frame{i:04d}.png').stat().st_mtime>start for i in range(120)),50)
        before=self.state();after=Week.night(self,before['facts']['Day']);entry=next(d for d in after['person']['Social']['Diary'] if d['Day']==before['facts']['Day'])
        assert '种子种下了' in entry['Text'] and entry['Sources'],entry
        return dict(job=job,crops=crops,diary=entry)
if __name__=='__main__':
    s=Visual()
    if '--prepare' in sys.argv:s.prepare()
    else:
        s.case('compound-clear-till-plant-and-natural-diary',s.verify)
        sys.exit(0 if all(c['passed'] for c in s.cases) else 1)
