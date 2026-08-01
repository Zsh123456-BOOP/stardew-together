"""Paid, bounded integration evaluation: real DeepSeek + real Squad + actual state."""
from pathlib import Path
import json
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,ROOT
from agent.companion import decide_and_run,watch


def run():
    b=Bridge(); cases=[]
    def save():
        path=ROOT/'outputs/companion-deepseek-report.json'
        path.parent.mkdir(exist_ok=True)
        path.write_text(json.dumps({'cases':cases},ensure_ascii=False,indent=2))
    def report(name,result):
        cases.append({'case':name,**result}); save()
        print(json.dumps({'case':name,'decision':result['decision'],'result':result['result'],
                          'metrics':result['metrics']},ensure_ascii=False),flush=True)

    b.reset_lab()
    result=decide_and_run('帮我挖掉附近的一块石头。',persona='adventurer')
    report('adventurer_accept',result)
    assert result['decision']['decision']=='accept' and result['result']['status']=='succeeded'
    assert result['result']['evidence']['mined_by_actor']

    b.reset_lab()
    before=b.state()['commands']
    result=decide_and_run('帮我挖掉附近的一块石头。',persona='fishing')
    report('fishing_persona_unwilling',result)
    assert result['decision']['decision'] in ('refuse','negotiate'),result
    assert result['result']['status']=='not_dispatched' and b.state()['commands']==before

    result=decide_and_run('这次请按我的明确指令去挖一块石头。',persona='fishing',force_skill='mine')
    report('explicit_force',result)
    assert result['forced'] and result['result']['status']=='succeeded'
    assert result['result']['evidence']['mined_by_actor']

    b.reset_lab()
    autonomous=watch(persona='adventurer',max_decisions=1,max_seconds=40,
                     on_result=lambda r:report('observed_idle_autonomy',r))
    assert len(autonomous)==1,'Idle watcher did not decide'
    assert autonomous[0]['decision']['decision']=='accept'
    assert autonomous[0]['result']['status']=='succeeded'
    total_tokens=sum(c['metrics']['usage'].get('total_tokens',0) for c in cases)
    calls=sum(c['metrics']['model_calls'] for c in cases)
    final={'passed':True,'cases':cases,'model_calls':calls,'total_tokens':total_tokens,
           'note':'One sample per scenario; no claim of general personality reliability.'}
    (ROOT/'outputs/companion-deepseek-report.json').write_text(json.dumps(final,ensure_ascii=False,indent=2))
    print('PASS: real-model acceptance, non-dispatched unwillingness, explicit force, idle-triggered autonomous action',flush=True)
    print(json.dumps({'calls':calls,'tokens':total_tokens}),flush=True)


if __name__=='__main__': run()
