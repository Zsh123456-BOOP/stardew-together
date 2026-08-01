"""Small, bounded DeepSeek JSON client. Credentials never enter prompts or logs."""
from pathlib import Path
import json
import os
import time
import urllib.request
import urllib.error
from .client import ROOT


class ModelError(RuntimeError):
    pass


def settings(path: Path | None = None):
    values = {}
    path = path or ROOT / '.env'
    if path.exists():
        for line in path.read_text().splitlines():
            if line.strip() and not line.lstrip().startswith('#') and '=' in line:
                key, value = line.split('=', 1)
                values[key.strip()] = value.strip().strip('\"\'')
    return {key: os.environ.get(key, values.get(key, default)) for key, default in {
        'DEEPSEEK_API_KEY': '', 'DEEPSEEK_MODEL': 'deepseek-flash',
        'DEEPSEEK_BASE_URL': 'https://api.deepseek.com'}.items()}


class DeepSeek:
    def __init__(self):
        self.config = settings()
        if not self.config['DEEPSEEK_API_KEY']:
            raise ModelError('missing_deepseek_api_key')
        # Never send this provider's key to an arbitrary endpoint from configuration.
        if self.config['DEEPSEEK_BASE_URL'].rstrip('/') not in ('https://api.deepseek.com','https://api.deepseek.com/v1'):
            raise ModelError('unsupported_deepseek_endpoint')

    def json(self, system, context):
        payload = {'model': self.config['DEEPSEEK_MODEL'], 'messages': [
            {'role': 'system', 'content': system},
            {'role': 'user', 'content': json.dumps(context, ensure_ascii=False)}],
            'response_format': {'type': 'json_object'}, 'thinking': {'type': 'disabled'},
            'max_tokens': 600, 'stream': False}
        req = urllib.request.Request(self.config['DEEPSEEK_BASE_URL'].rstrip('/') + '/chat/completions',
            data=json.dumps(payload).encode(), headers={'Content-Type': 'application/json',
            'Authorization': 'Bearer ' + self.config['DEEPSEEK_API_KEY']})
        started = time.monotonic()
        try:
            with urllib.request.urlopen(req, timeout=35) as response:
                data = json.load(response)
        except urllib.error.HTTPError as e:
            raise ModelError(f'deepseek_http_{e.code}') from None
        except (urllib.error.URLError, TimeoutError, OSError):
            raise ModelError('deepseek_network_error') from None
        except ValueError:
            raise ModelError('deepseek_invalid_response') from None
        try:
            choice = data['choices'][0]
            if choice.get('finish_reason') != 'stop':
                raise ModelError('deepseek_incomplete_response')
            decision = json.loads(choice['message']['content'])
        except (KeyError, IndexError, TypeError, ValueError):
            raise ModelError('deepseek_invalid_json') from None
        return decision, {'model': data.get('model', self.config['DEEPSEEK_MODEL']),
                          'latency_seconds': round(time.monotonic()-started, 3), 'usage': data.get('usage', {})}
