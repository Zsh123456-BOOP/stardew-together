"""Live task evaluation: repeated full workflow, plus pause/return/resume."""
from pathlib import Path
import json
import subprocess
import sys
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge, ROOT, actor_state
from agent.tasks import Runner, Store

def check_final(bridge):
    state = bridge.state()
    bot = actor_state(state, 'bot-1')
    assert len(state['crops']) == 6
    assert all(not c['needs_water'] and not c['harvestable'] for c in state['crops'])
    assert bot['tile'] == [43, 18]
    assert sum(i['quantity'] for i in bot['inventory'] if i['id'] == '(O)24') == 6
    return {'remaining_crops': 6, 'dry': 0, 'ripe': 0, 'received': 6, 'returned': True}

def main():
    store = Store()
    plan = json.loads((ROOT / 'configs/demo-plan.json').read_text())
    reports = []
    for n in range(3):
        bridge = Bridge()
        bridge.reset_lab()
        task_id = store.create(plan, bridge.session)
        started = time.monotonic()
        result = Runner(bridge, store).run(task_id)
        assert result['status'] == 'succeeded', result
        reports.append({'scenario': 'full_workflow', 'repeat': n+1, 'task_id': task_id,
                        'seconds': round(time.monotonic()-started, 2), 'final': check_final(bridge),
                        'action_count': sum(e['kind']=='action' for e in store.events(task_id))})
        print(json.dumps(reports[-1]), flush=True)
    bridge = Bridge()
    bridge.reset_lab()
    task_id = store.create(plan, bridge.session)
    with (ROOT / 'work/pause-worker.log').open('w') as log:
        worker = subprocess.Popen([sys.executable, '-m', 'agent', 'run', task_id], cwd=ROOT, stdout=log, stderr=log)
        deadline = time.monotonic() + 30
        try:
            while not any(e['kind']=='action' and e['data']['skill']=='water' for e in store.events(task_id)):
                if time.monotonic() >= deadline or worker.poll() is not None:
                    raise AssertionError('Worker did not water the first crop')
                time.sleep(.05)
            store.update(task_id, control='pause')
            worker.wait(timeout=10)
        finally:
            if worker.poll() is None:
                store.update(task_id, control='pause')
                worker.wait(timeout=15)
    assert store.get(task_id)['status'] == 'paused'
    before_resume = bridge.state()
    watered = sum(not c['needs_water'] for c in before_resume['crops'])
    assert 0 < watered < 12
    return_id = store.create({'goal': '临时回来', 'steps': [{'actor_id': 'bot-1', 'skill': 'move_to', 'target': [43, 18]}]}, bridge.session)
    assert Runner(bridge, store).run(return_id)['status'] == 'succeeded'
    store.update(task_id, control='run')
    assert Runner(bridge, store).run(task_id)['status'] == 'succeeded'
    water_actions = [e['data'] for e in store.events(task_id) if e['kind']=='action' and e['data']['skill']=='water']
    targets = [tuple(e['evidence']['before']['crop']['tile']) for e in water_actions]
    assert len(targets) == len(set(targets)) == 12, 'Duplicate or missing watering after resume'
    reports.append({'scenario': 'pause_return_resume', 'task_id': task_id, 'watered_before_pause': watered,
                    'unique_water_actions': len(set(targets)), 'final': check_final(bridge)})
    output = ROOT / 'outputs/stage1-report.json'
    output.write_text(json.dumps({'passed': True, 'planner': 'Codex supplied the structured task; no external LLM service',
                                 'fixture': 'AgentLab-v1; frozen game clock; normal bot actions', 'runs': reports}, ensure_ascii=False, indent=2))
    print(json.dumps(reports[-1], ensure_ascii=False), flush=True)
    print('PASS: 3 repeated workflows + pause/return/resume', flush=True)

if __name__ == '__main__':
    main()
