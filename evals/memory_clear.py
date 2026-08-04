"""Exercise the actual UI memory-clearing handler with a real model reply."""
from full_companion import Suite
from pathlib import Path
import json,sys
s=Suite();s.scenario('auto',enabled=0);s.scenario('close');s.scenario('budget_limit',value=24)
s.scenario('model',message='记住：今天的演示名叫收工小会。')
s.scenario('model',message='你还记得今天的演示名吗？')
s.until(lambda:not s.state()['thinking'],45)
before=s.state()['person'];assert before['LastDecision'] and before['Chat']
after=s.scenario('forget')['person']
assert after['LastDecision'] is None and after['Proposal'] is None
assert not after['Chat'] and not after['Memories'] and not after['Life']['Experiences']
assert all(not after['Social'][k] for k in ('Topics','Habits','Diary','Wishes','Preferences','SharedResults','PlayerNotes'))
assert after['Social']['Relationship']==before['Social']['Relationship']
assert (after.get('Job') or {}).get('Id')==(before.get('Job') or {}).get('Id')
Path('outputs/memory-clear-report.json').write_text(json.dumps(dict(passed=True,actual_model_reply_observed=True,cleared=['chat','experiences','diary','preferences','topics','habits','wishes','last_decision','unaccepted_proposal'],relationship_and_job_preserved=True),ensure_ascii=False,indent=2))
print('PASS actual memory clear including last model decision; relationship and current job preserved')
