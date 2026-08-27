"""Explicit fixture: production queued during cleanup must run at a safe boundary."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
def scenario(name,**args):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=name,**args))
def tool(name,**args):return scenario('agent_tool',tool=name,args=args)
def diag():return b.request('GET','/lab/together')['autoplay']
scenario('agent_fixture');time.sleep(1);scenario('cleanup_fixture');time.sleep(1)
tool('farm.zones',zones=[dict(id='field',kind='crop',x=46,y=28,width=7,height=5)])
tool('farm.cleanup',request_id='yield-field',scopes=['zone:field'],daily_limit=20,until=2200)
scenario('agent_schedule_probe')
end=time.monotonic()+60
while time.monotonic()<end:
 a=diag();act=a['snapshot'].get('player_action') or {}
 if act.get('skill')=='player.work' and act.get('status')=='running':break
 time.sleep(.2)
else:raise SystemExit('cleanup never reached native work')
plan=tool('plan.read')
tool('plan.submit',submission_id='priority-after-cleanup',expected_revision=plan['revision'],tasks=[dict(id='priority-return',actor='player',tool='player.move',args=dict(x=45,y=27),purpose='fixture: higher-priority production travel')])
end=time.monotonic()+100
while time.monotonic()<end:
 a=diag();tasks=a['state']['Schedule']['Tasks'];target=next(t for t in tasks if t['spec']['id']=='priority-return')
 if target['state'] in ('succeeded','failed','blocked'):break
 time.sleep(.3)
scenario('agent_pause')
yields=[t for t in tasks if t.get('receipt') and 'cleanup_yield_to_production' in t['receipt']]
checks=[dict(passed=target['state']=='succeeded',label='queued production travel runs before the persistent cleanup order exhausts its scope'),dict(passed=any(t['state']=='succeeded' for t in yields),label='cleanup yields at a native action boundary without failing dependent tasks'),dict(passed=a['state']['Decisions']==0,label='safe handoff needs no LLM request')]
Path('work/cleanup-yield-results.json').write_text(json.dumps(dict(checks=checks,tasks=tasks),ensure_ascii=False,indent=2))
for c in checks:print(('PASS ' if c['passed'] else 'FAIL ')+c['label'],flush=True)
raise SystemExit(0 if all(c['passed'] for c in checks) else 1)
