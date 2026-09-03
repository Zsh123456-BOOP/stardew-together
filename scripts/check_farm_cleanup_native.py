"""Grouped native cleanup checks, restricted to an explicitly prepared AgentLab fixture.

Not a natural farming run: objects and a full inventory are deliberately supplied.
"""
import json
from pathlib import Path
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge, BridgeError

b = Bridge()
assert b.state()['player']['name'] == 'AgentLab'
out = Path(sys.argv[1] if len(sys.argv)>1 else 'work/farm-cleanup-native'); out.mkdir(parents=True, exist_ok=True)
checks = []

def scenario(name, **kw):
    return b.request('POST', '/lab/together', dict(session_id=b.session, scenario=name, **kw))

def tool(name, **kw):
    return scenario('agent_tool', tool=name, args=kw)

def check(ok, label, detail=None):
    checks.append(dict(passed=bool(ok), label=label, detail=detail))
    (out/'results.json').write_text(json.dumps(checks, ensure_ascii=False, indent=2))
    print(('PASS ' if ok else 'FAIL ')+label, flush=True)

def read():
    return scenario('cleanup_read')

def wait_for(predicate, seconds=150):
    end = time.monotonic()+seconds
    state = read()
    while not predicate(state) and time.monotonic() < end:
        time.sleep(.4); state = read()
    return state

scenario('agent_fixture'); time.sleep(1); scenario('cleanup_fixture'); time.sleep(1)
zones = [dict(id='field', kind='crop', x=46, y=28, width=7, height=5),
         dict(id='grass', kind='pasture', x=53, y=28, width=2, height=3),
         dict(id='woods', kind='woodland', x=53, y=33, width=2, height=2),
         dict(id='keep', kind='reserve', x=45, y=33, width=1, height=1)]
tool('farm.zones', zones=zones)
summary = tool('farm.maintenance')
(out/'summary-before.json').write_text(json.dumps(summary, ensure_ascii=False, indent=2))
field = [g for g in summary['summary']['areas'] if g['zone']=='zone:field']
check(summary.get('details') is None and len(json.dumps(summary)) < 14000,
      'default summary aggregates counts without serializing every map tile')
check(sum(g['count'] for g in field if g['kind'] in ('weed','twig','stone')) == 9,
      'real weeds, twigs and stones are visible as nine eligible field targets', field)
args = dict(request_id='native-field', scopes=['zone:field'], daily_limit=20, until=2200, reserve_stamina=30)
tool('farm.cleanup', **args); repeated = tool('farm.cleanup', **args)
check(repeated.get('idempotent') and len(read()['orders']) == 1, 'same request is idempotent')
try:
    conflict = tool('farm.cleanup', **{**args, 'daily_limit':21})
    rejected = 'error' in conflict
except BridgeError:
    rejected = True
check(rejected, 'conflicting reuse of a request ID is rejected')
time.sleep(1)
check(len(read()['targets']) == 9, 'saving a cleanup goal while paused does not move the player')
scenario('agent_schedule_probe')
partial = wait_for(lambda s: s['orders'][0]['Completed'] >= 2)
tool('farm.cleanup', request_id='native-field', mode='pause')
time.sleep(1); paused = read(); time.sleep(1); still = read()
check(partial['orders'][0]['Completed'] >= 2 and paused['targets'] == still['targets'],
      'partial cleanup can pause without losing its remaining physical targets', paused)
old_task = still['orders'][0]['TaskId']
tool('farm.cleanup', **args)
resumed = wait_for(lambda s: bool(s['orders'][0]['TaskId']) and s['orders'][0]['TaskId'] != old_task, 8)
check(bool(resumed['orders'][0]['TaskId']) and resumed['orders'][0]['TaskId'] != old_task,
      'explicit resume dispatches without inheriting a failed-action retry cooldown')
done = wait_for(lambda s: s['orders'][0]['Status'] == 'complete', 240)
(out/'cleanup-receipt.json').write_text(json.dumps(done, ensure_ascii=False, indent=2))
check(not done['targets'] and done['orders'][0]['Status'] == 'complete',
      'one goal continues through multiple native batches without LLM decisions', done)
check(done['orders'][0]['CompletedToday'] == 9,
      'pause between native impact and semantic receipt preserves the full daily quota usage', done['orders'][0])
check(done['young'] and done['mature'], 'ordinary cleanup leaves both young and mature trees')
check(done['grass'] and done['protected_tree'] and done['protected_twig'] and done['chest'],
      'single-tile clearing preserves adjacent pasture, woodland, reserve and chest')
check((done['stored'] or 0) >= 70, 'full inventory is unloaded to a real output chest before cleanup continues', done['stored'])
receipts = []
for task in b.request('GET', '/lab/together')['autoplay']['state']['Schedule']['Tasks']:
    if not task.get('receipt'):
        continue
    for evidence in json.loads(task['receipt']).get('evidence', []):
        if evidence.get('kind') != 'cleanup_labor':
            continue
        receipts.append(tool('action.status', id=evidence['command_id']))
(out/'native-actions.json').write_text(json.dumps(receipts, ensure_ascii=False, indent=2))
check(any(r.get('completed', 0) > 1 for r in receipts),
      'nearby compatible targets share a native batch instead of stopping to collect after every object')
check(any(len({e['work_slot'] for e in r.get('effects', []) if 'work_slot' in e}) > 1 for r in receipts),
      'one local route switches tools for mixed nearby obstacles instead of grouping distant stones first')
scenario('agent_pause')
zones[0]['allow_trees'] = True
tool('farm.zones', zones=zones)
tool('farm.cleanup', request_id='native-trees', scopes=['zone:field'], remove_trees=True,
     daily_limit=2, until=2200, reserve_stamina=30)
scenario('agent_schedule_probe')
trees = wait_for(lambda s: any(o['Id']=='native-trees' and o['Status']=='complete' for o in s['orders']), 180)
check(not trees['young'] and not trees['mature'] and trees['protected_tree'],
      'explicit redevelopment uses native axe for young and mature trees only in its approved zone', trees)
check(trees['loose'] == 0 and trees['wood_carried'] > 0,
      'tree work includes scattered native loot pickup before its order completes', trees)
status = b.request('GET', '/lab/together')['autoplay']
check(status['state']['Decisions'] == 0, 'grouped execution did not ask an LLM for individual targets')
scenario('agent_pause')
(out/'final.json').write_text(json.dumps(trees, ensure_ascii=False, indent=2))
raise SystemExit(0 if all(c['passed'] for c in checks) else 1)
