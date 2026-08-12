"""Bounded real-model daytime observation. AgentLab only; observer never chooses its actions."""
from pathlib import Path
import json,sys,time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path(sys.argv[1] if len(sys.argv)>1 else 'work/autoplay-normal-day');out.mkdir(exist_ok=True)
def scenario(name,**args):return b.request('POST','/lab/together',{'session_id':b.session,'scenario':name,**args})
def diag():return b.request('GET','/lab/together')['autoplay']
baseline=diag();(out/'baseline.json').write_text(json.dumps(baseline,ensure_ascii=False,indent=2))
scenario('agent_start',goal='正常速度共同经营这份农场，推进真实任务与建设目标。先根据当前季节、作物与库存制定当天安排，合理使用白天和体力，保持资源预留，能分工时让伙伴协作。遇到失败查状态调整，不修改物资、日期或完成标记。今天结束后继续下一天经营。')
seen={(e["Kind"],e["Text"]) for e in baseline["state"]["Journal"]};actions=[];decisions=0;passed=False;deadline=time.monotonic()+240
try:
    while time.monotonic()<deadline:
        d=diag();(out/'latest.json').write_text(json.dumps(d,ensure_ascii=False,indent=2))
        for event in d['state']['Journal']:
            key=(event['Kind'],event['Text'])
            if key in seen:continue
            seen.add(key)
            with (out/'events.jsonl').open('a') as f:f.write(json.dumps(event,ensure_ascii=False)+'\n')
            if event['Kind']=='decision':
                turn=json.loads(event['Text']);decisions+=1;print('DECISION',decisions,[c['tool'] for c in turn['calls']],flush=True)
            if event['Kind']=='action_result':
                r=json.loads(event['Text']);actions.append(r);print('ACTION',r.get('skill'),r.get('status'),r.get('completed'),r.get('error'),flush=True)
        succeeded=[r for r in actions if r.get('status')=='succeeded']
        farm=any(r.get('skill')=='player.work' and any(e.get('after',{}).get('watered')==1 for e in r.get('effects',[])) for r in succeeded)
        resource=any(r.get('skill')=='player.work' and any(e.get('before',{}).get('item') and e.get('after',{}).get('item') is None for e in r.get('effects',[])) for r in succeeded)
        if farm and resource:
            passed=True;print('PASS: model completed farm care and continued real resource gathering at normal speed',flush=True);break
        if d['state']['Status']!='running':print('STOP',d['state']['Detail'],flush=True);break
        time.sleep(2)
finally:
    scenario('agent_pause');(out/'final.json').write_text(json.dumps(diag(),ensure_ascii=False,indent=2))

assert passed, "Model did not demonstrate both farm care and gathering within this bounded observation"
