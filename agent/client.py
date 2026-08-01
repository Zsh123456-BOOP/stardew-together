from __future__ import annotations

import json
from pathlib import Path
import time
import urllib.error
import urllib.request
import uuid

ROOT = Path(__file__).resolve().parents[1]

class BridgeError(RuntimeError):
    pass

class Bridge:
    def __init__(self, config: Path | None = None):
        settings = json.loads((config or ROOT / '.local.json').read_text())
        self.url = settings['url']
        self.token = settings['token']
        self.session = None

    def request(self, method: str, path: str, body=None):
        data = None if body is None else json.dumps(body, sort_keys=True).encode()
        request = urllib.request.Request(self.url + path, data=data, method=method,
            headers={'Authorization': 'Bearer ' + self.token, 'Content-Type': 'application/json'})
        try:
            # Local loopback never uses an environment HTTP proxy.
            with urllib.request.build_opener(urllib.request.ProxyHandler({})).open(request, timeout=7) as response:
                return json.load(response)
        except urllib.error.HTTPError as e:
            try:
                reason = json.loads(e.read()).get('error', str(e.code))
            except (ValueError, AttributeError):
                reason = str(e.code)
            raise BridgeError(reason) from None
        except (urllib.error.URLError, TimeoutError) as e:
            raise BridgeError('connection_uncertain: query command ID before retrying') from e

    def state(self):
        state = self.request('GET', '/state')
        if self.session and self.session != state['session_id']:
            raise BridgeError('stale_session')
        self.session = state['session_id']
        return state

    def map(self, actor='bot-1'):
        return self.request('GET', '/map?actor_id=' + actor)

    def action(self, actor, skill, target, *, command_id=None, timeout=12):
        if not self.session:
            self.state()
        command_id = command_id or uuid.uuid4().hex
        command = {'session_id': self.session, 'command_id': command_id, 'actor_id': actor, 'skill': skill, 'target': list(target)}
        result = self.request('POST', '/commands', command)
        deadline = time.monotonic() + timeout
        while result['status'] == 'running':
            if time.monotonic() >= deadline:
                result = self.request('POST', f'/commands/{command_id}/cancel', {'session_id': self.session})
                if result['status'] == 'running':
                    raise BridgeError(f'cancel_pending:{command_id}')
                break
            time.sleep(0.08)
            result = self.request('GET', f'/commands/{command_id}')
        if result['status'] != 'succeeded':
            raise BridgeError(result.get('error') or result['status'])
        return result

    def reset_lab(self):
        self.state()
        result = self.request('POST', '/lab/reset', {'session_id': self.session})
        self.session = None
        self.state()
        return result

def actor_state(state, actor):
    return next(a for a in state['actors'] if a['id'] == actor)
