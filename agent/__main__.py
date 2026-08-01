"""CLI tools for a human or LLM planner. Does not pretend rules are an LLM."""
import argparse
import json
from pathlib import Path
import sys
from .client import Bridge, BridgeError
from .tasks import Store, Runner

def main():
    p = argparse.ArgumentParser(description='Non-visual Stardew Agent tools')
    sub = p.add_subparsers(dest='command', required=True)
    sub.add_parser('state')
    sub.add_parser('reset-lab')
    new = sub.add_parser('submit')
    new.add_argument('plan', type=Path)
    new.add_argument('--run', action='store_true')
    for name in ('run', 'pause', 'resume', 'task', 'events'):
        item = sub.add_parser(name)
        item.add_argument('task_id')
    args = p.parse_args()
    bridge = Bridge()
    store = Store()
    if args.command == 'state':
        result = bridge.state()
    elif args.command == 'reset-lab':
        result = bridge.reset_lab()
    elif args.command == 'submit':
        task_id = store.create(json.loads(args.plan.read_text()), bridge.state()['session_id'])
        print(json.dumps({'task_id': task_id}, ensure_ascii=False), flush=True)
        result = Runner(bridge, store).run(task_id) if args.run else store.get(task_id)
    elif args.command == 'pause':
        store.get(args.task_id)
        store.update(args.task_id, control='pause')
        result = {'task_id': args.task_id, 'pause_requested': True, 'note': 'Check task status for acknowledgement at next action boundary.'}
    elif args.command in ('run', 'resume'):
        if args.command == 'resume':
            store.update(args.task_id, control='run')
        result = Runner(bridge, store).run(args.task_id)
    elif args.command == 'events':
        result = store.events(args.task_id)
    else:
        result = store.get(args.task_id)
    print(json.dumps(result, ensure_ascii=False, indent=2))
    if isinstance(result, dict) and result.get('status') == 'failed':
        return 1
    return 0

if __name__ == '__main__':
    try:
        sys.exit(main())
    except (BridgeError, ValueError) as e:
        print(json.dumps({'error': str(e)}, ensure_ascii=False), file=sys.stderr)
        sys.exit(1)
