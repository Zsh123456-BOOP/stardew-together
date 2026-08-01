"""Another real robot moves into a cached route; no map/position cheating."""
from pathlib import Path
import json
import subprocess
import sys
import time
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge, ROOT, actor_state
from agent.tasks import Runner, Store

def main():
    b, store = Bridge(), Store()
    b.reset_lab()
    setup = {'goal': '定位动态障碍机器人', 'steps': [{'skill': 'move_to', 'actor_id': 'bot-2', 'target': [46, 19]}]}
    setup_id = store.create(setup, b.session)
    assert Runner(b, store).run(setup_id)['status'] == 'succeeded'
    plan = {'goal': '道路动态变化时继续到达目标', 'steps': [{'skill': 'move_to', 'actor_id': 'bot-1', 'target': [49, 18]}]}
    task_id = store.create(plan, b.session)
    with (ROOT / 'work/obstacle-worker.log').open('w') as log:
        worker = subprocess.Popen([sys.executable, '-m', 'agent', 'run', task_id], cwd=ROOT, stdout=log, stderr=log)
        deadline = time.monotonic() + 10
        try:
            while not any(e['kind']=='dispatch' for e in store.events(task_id)):
                if time.monotonic() > deadline or worker.poll() is not None:
                    raise AssertionError('No initial route dispatched')
                time.sleep(.02)
            obstacle = b.action('bot-2', 'step', [46, 18])
            assert obstacle['status'] == 'succeeded'
            worker.wait(timeout=30)
        finally:
            if worker.poll() is None:
                store.update(task_id, control='pause')
                worker.wait(timeout=15)
    result = store.get(task_id)
    assert result['status'] == 'succeeded', result
    replans = [e for e in store.events(task_id) if e['kind']=='replan']
    assert replans, 'Did not exercise a cached-route failure'
    assert actor_state(b.state(), 'bot-1')['tile'] == [49, 18]
    assert actor_state(b.state(), 'bot-2')['tile'] == [46, 18]
    grid = b.map('bot-1')
    allowed = set(map(tuple, grid['passable']))
    blocked = next([x, y] for y in range(grid['height']) for x in range(grid['width']) if (x, y) not in allowed)
    failed_id = store.create({'goal': '负例：不可通行的目标', 'steps': [{'skill': 'move_to', 'target': blocked}]}, b.session)
    negative = Runner(b, store).run(failed_id)
    assert negative['status'] == 'failed' and negative['error'] == 'unreachable'
    assert actor_state(b.state(), 'bot-1')['tile'] == [49, 18]
    report = {'passed': True, 'task_id': task_id, 'obstacle': [46, 18], 'replans': len(replans),
              'arrived': [49, 18], 'negative_case': {'task_id': failed_id, 'target': blocked, 'status': 'failed', 'error': 'unreachable'},
              'events': store.events(task_id)}
    (ROOT / 'outputs/dynamic-obstacle-report.json').write_text(json.dumps(report, ensure_ascii=False, indent=2))
    print(json.dumps({k:v for k,v in report.items() if k!='events'}, ensure_ascii=False))

if __name__ == '__main__':
    main()
