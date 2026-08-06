"""Run encyclopedia acceptance against the explicitly isolated AgentLab runtime.

Launch scripts/launch.py --companion --lab and load AgentLab first. --models uses
the already configured model through the same in-game pipeline; no key is read
or printed by this script. Reports are local and ignored by Git.
"""
import argparse
import json
from pathlib import Path
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--models', action='store_true')
    args = parser.parse_args()
    bridge = Bridge()
    bridge.state()
    def call(scenario, **kw):
        return bridge.request('POST', '/lab/together', {'session_id': bridge.session, 'scenario': scenario, **kw})
    def state():
        return bridge.request('GET', '/lab/together')
    results = call('knowledge_suite')['results']
    def check(value, label, **evidence):
        results.append({'pass': bool(value), 'label': label, **evidence})
    call('knowledge_mode', value='all')
    call('knowledge_book', query='日历')
    view = call('knowledge_button', label='日历')
    check(view is not None and 'guide:calendar' in view['EntityIds'], 'calendar click handler opens live evidence')
    view = call('knowledge_button', label='农场')
    check(view is not None and 'guide:growth' in view['EntityIds'], 'farm click handler opens live crop details')
    view = call('knowledge_button', label='献祭')
    check(view is not None and 'guide:bundle' in view['EntityIds'], 'bundle click handler opens progress')
    baseline = state()
    call('knowledge_note', id='(O)472', text='下次想一起种一点；这是测试便签。')
    packet = call('knowledge_query', id='(O)472')
    check(any('测试便签' in f['Value'] and '不是游戏事实' in f['Source'] for f in packet['Facts']), 'notes remain attributed to player')
    call('knowledge_pin', id='(O)472')
    book = call('knowledge_export')['book']
    check('(O)472' in book['Favorites'], 'pinning persists favorite')
    after = state()
    check(after['farm_policy'] == baseline['farm_policy'], 'pinning cannot expand spending or terrain permissions')
    call('knowledge_mode', value='discovered')
    private = call('knowledge_query', id='location:IslandNorth')
    check(private['Status'] == 'not_found', 'undiscovered late-game map hidden from direct lookup')
    call('knowledge_mode', value='all')
    if args.models:
        def ask(query, entry=None):
            before = state()
            call('knowledge_ask', query=query, **({'id': entry} if entry else {}))
            deadline = time.monotonic() + 75
            while state()['thinking']:
                if time.monotonic() > deadline:
                    raise TimeoutError('knowledge pipeline did not finish')
                time.sleep(.5)
            after = state()
            report = call('knowledge_export')
            check((after['person'].get('Job') or {}).get('Id') == (before['person'].get('Job') or {}).get('Id'), 'encyclopedia reply preserves existing task identity')
            return report, after['calls'] - before['calls']
        report, calls = ask('今天种这个来得及在本季结束前成熟吗？简短说明条件。', '(O)472')
        check(calls == 1 and '显示本地资料' not in report['answer'] and len(report['answer']) > 10,
              'real model grounded crop answer', calls=calls, answer=report['answer'])
        report, calls = ask('给我查查哪天能给镇上的朋友过庆生会')
        check(calls <= 2 and calls > 0 and report['packet'] is not None,
              'bounded natural-language query rewrite', calls=calls, answer=report['answer'])
        call('model_name', value='together-intentionally-invalid-model')
        try:
            report, calls = ask('这是什么？', '(O)472')
            check('本地资料' in report['answer'] and len(report['packet']['Facts']) > 0, 'model service failure retains local evidence')
        finally:
            call('model_name', value='deepseek-flash')
    destination = Path(__file__).resolve().parents[1] / 'work/knowledge-acceptance.json'
    destination.write_text(json.dumps({'scope': 'isolated AgentLab', 'results': results}, ensure_ascii=False, indent=2))
    failures = [r for r in results if not r['pass']]
    print(json.dumps({'checks': len(results), 'failures': failures, 'report': str(destination)}, ensure_ascii=False, indent=2))
    return 1 if failures else 0


if __name__ == '__main__':
    raise SystemExit(main())
