"""Compare a fresh native load with the last overnight checkpoint, before fixtures."""
from pathlib import Path
import json,sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();b.state();actual=b.request('GET','/lab/together')
expected=json.loads(Path('work/expected-week-save.json').read_text());person=expected['People']['Abigail']
assert actual['facts']['Day']==16
assert actual['person']['Social']['Diary']==person['Social']['Diary']
assert actual['person']['Social']['Preferences']==person['Social']['Preferences']
assert [p['Id'] for p in actual['projects']]==[p['Id'] for p in expected['Projects']]
assert actual['person']['Profile']==person['Profile'],'unsaved personality edits survived rollback'
assert actual['calls']>=expected['Calls'],'API usage ledger rolled back'
assert len(actual['person']['Social']['Diary'])==7
Path('outputs/save-reload-report.json').write_text(json.dumps(dict(passed=True,day=actual['facts']['Day'],diary_days=len(person['Social']['Diary']),project_ids=[p['Id'] for p in actual['projects']],preferences=person['Social']['Preferences'],unsaved_profile_changes_discarded=True,api_usage_not_rolled_back=True),ensure_ascii=False,indent=2))
print('PASS native reload preserves seven-day memories and discards unsaved fixture changes')
