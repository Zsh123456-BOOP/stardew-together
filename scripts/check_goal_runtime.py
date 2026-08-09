"""Concentrated wish-plan acceptance in the explicitly isolated AgentLab save."""
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
    b = Bridge()
    b.state()
    def call(scenario, **kw):
        return b.request('POST', '/lab/together', {'session_id': b.session, 'scenario': scenario, **kw})
    def state():
        return b.request('GET', '/lab/together')
    results = call('goal_suite')['results']
    def check(ok, label, **proof):
        results.append(dict(pass_=bool(ok), label=label, **proof))
        results[-1]['pass'] = results[-1].pop('pass_')
        print(('PASS ' if ok else 'FAIL ') + label, flush=True)
    def wait_model():
        deadline = time.monotonic() + 40
        while state()['thinking'] and time.monotonic() < deadline:
            time.sleep(.3)
        assert not state()['thinking'], 'model timeout'
        return state()
    call('goal_fixture')
    call('select', npc='Abigail')
    call('fresh_person')
    call('budget_limit', value=100)
    call('time', value=1000)
    call('goal_add', id='craft:Keg')
    s=state();g=s['shared_goals'][-1]
    check(g['Status']=='active' and any(n['Status']=='locked' for n in g['Nodes']), 'advanced locked wish persists while gathering is possible')
    node=next(n for n in g['Nodes'] if n['Item']=='(O)378')
    opts=call('goal_options')
    option=next((o for o in opts if o['Id']=='goal:'+g['Id']+':'+node['Id']),None)
    check(option is not None and option['Steps'][0]['target_item']=='(O)378', 'autonomy offers specific copper acquisition for keg goal')
    call('goal_assign', id=g['Id'], node=node['Id'], owner='player')
    check(not any(o['Id']=='goal:'+g['Id']+':'+node['Id'] for o in call('goal_options')), 'player-owned step excluded from model options')
    call('goal_assign', id=g['Id'], node=node['Id'], owner='together')
    refusal=call('goal_refusal')
    check(refusal['pass'], refusal['label'])
    if args.models:
        call('preset', name='冒险搭子')
        before=state()['calls']
        call('goal_work', id=g['Id'], node=node['Id'])
        s=wait_model()
        check(s['calls']==before+1 and (s['person'].get('Job') is not None or s['person'].get('Proposal') is not None), 'real DeepSeek negotiates a material step', speech=s['person']['Chat'][-1]['Text'])
        if s['person'].get('Job') is None or not (s['person']['Job']['OptionId'] or '').startswith('goal:'):
            call('goal_accept', forced=1)
    else:
        call('goal_work', id=g['Id'], node=node['Id'], forced=1)
    started=state()['person']['Job']
    check(started is not None and started['Steps'][0]['target_item']=='(O)378', 'accepted work retains exact desired output')
    call('close')
    deadline=time.monotonic()+55
    while time.monotonic()<deadline:
        s=state();job=s['person'].get('Job') or {}
        if job.get('Status') not in ('active','waiting'): break
        time.sleep(.5)
    call('goal_menu');s=state();g=next(x for x in s['shared_goals'] if x['Id']==g['Id'])
    copper=sum(x['Count'] for x in s['facts']['Stock'] if x['Item']=='(O)378')
    node=next(n for n in g['Nodes'] if n['Item']=='(O)378')
    check(copper>0 and node['Owned']>0, 'companion actually mines copper and wish allocation updates', actual_copper=copper, job=s['person']['Job'])
    check(g['Status']=='active', 'one successful mining action does not falsely fulfill keg goal')
    packet=call('knowledge_query', id='(O)378')
    check(any('心愿' in f['Label'] for f in packet['Facts']), 'encyclopedia copper evidence includes updated shared goal')
    call('goal_pause', id=g['Id'])
    check(not any(o['Id'].startswith('goal:'+g['Id']+':') for o in call('goal_options')), 'pausing removes all wish work from autonomous options')
    call('goal_pause', id=g['Id'])
    call('goal_menu')
    if args.models:
        before=state()['calls']
        call('model', message='我们准备的 Keg 还缺什么？配方还没学会的话今天能先做什么？')
        s=wait_model()
        check(s['calls']>before, 'companion receives updated goal context for progress conversation', notice=s['notice'], speech=s['person']['Chat'][-1]['Text'])
    destination=Path(__file__).resolve().parents[1]/'work/goal-acceptance.json'
    destination.write_text(json.dumps({'scope':'isolated AgentLab','results':results},ensure_ascii=False,indent=2))
    failures=[r for r in results if not r['pass']]
    print(json.dumps({'checks':len(results),'failures':failures,'report':str(destination)},ensure_ascii=False,indent=2))
    return bool(failures)

if __name__=='__main__':
    raise SystemExit(main())
