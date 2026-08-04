"""Final navigation repair checks and genuine overnight shipping settlement."""
from advanced_companion import Advanced
from seven_day_companion import Week
from pathlib import Path
import json,sys
class Release(Advanced):
    def save(self):Path('outputs/release-checks.json').write_text(json.dumps(dict(cases=self.cases,passed=all(c['passed'] for c in self.cases)),ensure_ascii=False,indent=2))
    def round_trip(self):
        self.fixture();receipts=[]
        for destination in ('Mine','UndergroundMine1','Mine','Farm'):
            receipts.append(self.action('travel',destination=destination))
            assert self.actor()['location']==destination
        return receipts
    def overnight_shipping(self):
        self.shipping();before=self.scenario('configure')['facts'];after=Week.night(self,before['Day'])
        assert after['facts']['Money']==before['Money']+105,(before['Money'],after['facts']['Money'])
        return dict(before=before['Money'],after=after['facts']['Money'],items='3 normal parsnips @ native 35 gold',day_before=before['Day'],day_after=after['facts']['Day'])
    def run(self):
        for name,fn in [('P4-farm-mine-floor-return',self.round_trip),('A09-repaired-path-dynamic-obstacle',self.obstacles),('A02-repaired-path-production',self.production),('P3-native-overnight-shipping-income',self.overnight_shipping)]:self.case(name,fn)
        return all(c['passed'] for c in self.cases)
if __name__=='__main__':sys.exit(0 if Release().run() else 1)
