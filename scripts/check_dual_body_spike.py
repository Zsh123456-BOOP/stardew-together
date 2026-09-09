"""Stage 0 only: run B first, then A; stop on the first native failure.

Run against a fresh AgentLab with scripts/launch.py --companion --lab
--mods-dir work/DualBodyMods --port 18767. No model calls or save writes.
Runtime evidence also lives in work/DualBodyMods/Together/dual-body-evidence.
"""
import json
import sys
import time
from pathlib import Path

sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge

out=Path(sys.argv[1] if len(sys.argv)>1 else 'work/dual-body-spike');out.mkdir(parents=True,exist_ok=True)
b=Bridge();state=b.state()
assert state['player']['name']=='AgentLab'
(out/'initial-state.json').write_text(json.dumps(state,ensure_ascii=False,indent=2))

def scenario(name,**kwargs):
    return b.request('POST','/lab/together',dict(session_id=b.session,scenario=name,**kwargs))

(out/'metadata.json').write_text(json.dumps(scenario('dual_body_metadata'),indent=2))
for chain in ['B','A']:
    scenario('dual_body_start',chain=chain)
    deadline=time.monotonic()+140
    while True:
        state=scenario('dual_body_read')
        with (out/(chain+'-samples.jsonl')).open('a') as f:
            f.write(json.dumps(state,ensure_ascii=False)+'\n')
        if state['phase'] in ('passed','failed') or time.monotonic()>deadline:break
        time.sleep(.5)
    (out/(chain+'-result.json')).write_text(json.dumps(state,ensure_ascii=False,indent=2))
    print(chain,state['phase'],state['error'],flush=True)
    if state['phase']!='passed':
        raise SystemExit('STOP: no retry, no architecture workaround; inspect native evidence.')
print('Both native probes passed; this is not a production or gameplay-loop claim.')
