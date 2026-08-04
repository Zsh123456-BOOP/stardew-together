"""Targeted real-action regression after shared reachability cache optimization."""
from release_checks import Release
from pathlib import Path
import json,sys
class PostSearch(Release):
    def save(self):Path('outputs/post-search-checks.json').write_text(json.dumps(dict(cases=self.cases,passed=all(c['passed'] for c in self.cases)),ensure_ascii=False,indent=2))
    def run(self):
        for name,fn in [('core-map-round-trip',self.round_trip),('dynamic-obstacle',self.obstacles),('production-supply-consumption',self.production),('building-feeding-route',self.feeding)]:self.case(name,fn)
        return all(c['passed'] for c in self.cases)
if __name__=='__main__':sys.exit(0 if PostSearch().run() else 1)
