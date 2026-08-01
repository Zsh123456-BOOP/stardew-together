"""Real-game smoke test. Requires explicitly enabled AgentLab fixture."""
from pathlib import Path
import json
import sys
import time
import uuid
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge, BridgeError, actor_state

def run():
    b = Bridge()
    b.reset_lab()
    deadline = time.monotonic() + 5
    while b.state()['location'] != 'Farm':
        if time.monotonic() >= deadline:
            raise AssertionError('Fixture warp not completed')
        time.sleep(.1)
    command = {'session_id': b.session, 'command_id': uuid.uuid4().hex, 'actor_id': 'bot-1', 'skill': 'step', 'target': [44, 18]}
    b.request('POST', '/commands', command)
    b.request('POST', '/commands/' + command['command_id'] + '/cancel', {'session_id': b.session})
    while True:
        result = b.request('GET', '/commands/' + command['command_id'])
        if result['status'] != 'running':
            break
        if time.monotonic() >= deadline:
            raise AssertionError('Cancel never reached an action boundary')
        time.sleep(.1)
    assert result['status'] == 'succeeded' and result['cancellation_requested']
    # Retrying the identical command does not move a second tile.
    assert b.request('POST', '/commands', command)['status'] == 'succeeded'
    assert actor_state(b.state(), 'bot-1')['tile'] == [44, 18]
    bad = dict(command, command_id=uuid.uuid4().hex, session_id='old-session')
    try:
        b.request('POST', '/commands', bad)
        raise AssertionError('Stale session was accepted')
    except BridgeError as e:
        assert str(e) == 'stale_session'
    evidence = [result]
    for x in [45, 46, 47]:
        evidence.append(b.action('bot-1', 'step', [x, 18]))
    evidence.append(b.action('bot-1', 'water', [48, 18]))
    water = evidence[-1]['evidence']
    assert water['before']['crop']['needs_water'] and not water['after']['crop']['needs_water']
    for x in [48, 49]:
        evidence.append(b.action('bot-1', 'step', [x, 18]))
    evidence.append(b.action('bot-1', 'harvest', [50, 18]))
    harvested = actor_state(b.state(), 'bot-1')['inventory']
    assert sum(i['quantity'] for i in harvested if i['id'] == '(O)24') == 1
    output = Path(__file__).resolve().parents[1] / 'outputs/stage0-evidence.json'
    output.parent.mkdir(exist_ok=True)
    output.write_text(json.dumps({'passed': True, 'checks': ['real_movement', 'watering_effect', 'harvest_inventory', 'cancel_at_boundary', 'idempotent_retry', 'reject_stale_session'], 'actions': evidence}, ensure_ascii=False, indent=2))
    print('PASS: movement, water, harvest receipt, safe cancellation, deduplication, stale-session rejection')

if __name__ == '__main__':
    run()
