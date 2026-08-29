"""Read-only assertions after a real process restart and native save reload."""
import json,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();s=b.state();expected=json.loads(Path(sys.argv[1]).read_text());actors=[a for a in s['actors'] if a['name']=='Together_Partner']
checks=[dict(label='custom identity is restored once',passed=len(actors)==1)]
if len(actors)==1:
 a=actors[0]
 checks.append(dict(label='custom worker restores independent control, not villager follow mode',passed=a['control_mode']=='independent'))
 checks.extend([dict(label='same saved actor identifier',passed=a['id']==expected['actor']),dict(label='native date survives restart',passed=s['day']==expected['day']),dict(label='actual pouch survives restart',passed=a['cargo']==expected['cargo'])])
Path('work/partner-persistence.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2))
for c in checks:print(('PASS ' if c['passed'] else 'FAIL ')+c['label'])
raise SystemExit(0 if all(c['passed'] for c in checks) else 1)
