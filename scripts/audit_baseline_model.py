"""Read-only model trace audit. Never includes transport/auth configuration."""
import argparse, collections, json
from pathlib import Path


def audit(root, run):
    requests, responses, observations = [], [], []
    scalar_fields = ('slot_count', 'free_slots', 'occupied', 'capacity_constraints', 'capacity_release_conditions')
    for path in sorted(root.glob('*/model-*.jsonl')):
        for line_number, line in enumerate(path.open(), 1):
            event = json.loads(line)
            source = str(path) + ':' + str(line_number)
            payload = event['payload']
            if event['kind'] == 'request':
                body = json.loads(payload['body'])
                context = json.loads(body['messages'][-1]['content'])
                if context.get('run_id') != run:
                    continue
                inv = context.get('inventory_plan', {})
                policy = context.get('business', {})
                now = context.get('now', {})
                requests.append(dict(call_id=event['call_id'], source=source, utc=event['utc'], now=now,
                    missing_scalars=[key for key in scalar_fields if key not in inv],
                    capacity={key: inv.get(key) for key in scalar_fields},
                    policies={key: value for key, value in policy.items() if 'policy' in key or 'routine' in key or 'investment' in key},
                    candidates=context.get('operating_candidates', []), budget=context.get('context_budget', {})))
                for entry in context.get('recent', []):
                    data = entry.get('data', entry)
                    if data.get('tool') in ('world.read', 'day.read', 'day.plan', 'plan.read', 'progress.roadmap'):
                        result = data.get('result')
                        observations.append(dict(call_id=event['call_id'], tool=data['tool'], source=source,
                            pointer=isinstance(result, dict) and 'current_context' in result,
                            keys=list(result) if isinstance(result, dict) else None))
            elif event['kind'] == 'response':
                try:
                    body = json.loads(payload['body'])
                except (ValueError, TypeError):
                    responses.append(dict(call_id=event['call_id'], source=source, utc=event['utc'], usage={}, decision={}, transport_status=payload.get('status'), response_parse_error=True))
                    continue
                message = next(iter(body.get('choices', [])), {}).get('message', {}).get('content', '')
                try:
                    decision = json.loads(message)
                except (ValueError, TypeError):
                    decision = {'unparsed_content': message}
                responses.append(dict(call_id=event['call_id'], source=source, utc=event['utc'], usage=body.get('usage', {}), decision=decision))
    ids = {row['call_id'] for row in requests}
    responses = [row for row in responses if row['call_id'] in ids]
    usage = collections.Counter()
    for row in responses:
        usage.update({key: value for key, value in row['usage'].items() if isinstance(value, (int, float))})
    return dict(run=run, request_count=len(requests), response_count=len(responses),
        missing_scalar_requests=[row['call_id'] for row in requests if row['missing_scalars']],
        query_observations=observations, usage=dict(usage), requests=requests, responses=responses)


if __name__ == '__main__':
    p = argparse.ArgumentParser()
    p.add_argument('--logroot', type=Path, required=True)
    p.add_argument('--run', required=True)
    p.add_argument('--out', type=Path, required=True)
    args = p.parse_args()
    args.out.write_text(json.dumps(audit(args.logroot, args.run), ensure_ascii=False, indent=2))
