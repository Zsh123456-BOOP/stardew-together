"""Bounded Flash integration check; three jobs in AgentLab, not full-day certification."""
from pathlib import Path
import json,time,sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
def scenario(name,**args):return b.request('POST','/lab/together',{'session_id':b.session,'scenario':name,**args})
def diag():return b.request('GET','/lab/together')['autoplay']
old_jobs={j['command_id'] for j in diag().get('work',[])}
scenario('agent_semantic_fixture');time.sleep(2)
scenario('model_name',value='deepseek-flash')
scenario('agent_start',goal='一起分工完成这一批真实工作：玩家为农场的6株新作物浇水并收集12个木材，Abigail在农场收集3个石料。检查补给并自行处理缺水。不要给每一块资源单独下指令，不改物资或时间；依据实际结果汇报。完成这批后暂停，等待玩家下一步安排。')
start=time.monotonic();passed=False
try:
    while time.monotonic()-start<180:
        d=diag();Path('work/semantic-flash-latest.json').write_text(json.dumps(d,ensure_ascii=False,indent=2));jobs=[j for j in d.get('work',[]) if j['command_id'] not in old_jobs]
        achieved={j['goal'] for j in jobs if j['status']=='succeeded' and (j['goal']=='water' and j['completed']>=6 or j['goal']=='wood' and j['gained']>=12 or j['goal']=='stone' and j['gained']>=3)}
        print('decisions',d['state']['Decisions'],'completed',sorted(achieved),'status',d['state']['Status'],flush=True)
        if achieved=={'water','wood','stone'}:passed=True;break
        if d['state']['Status']!='running' or d['state']['Decisions']>=8:break
        time.sleep(3)
finally:
    scenario('agent_pause');final=diag();Path('work/semantic-flash-final.json').write_text(json.dumps(final,ensure_ascii=False,indent=2))
    usagepath=Path('work/CompanionMods/Together/usage/autoplay-model-usage.jsonl')
    usage=[json.loads(l) for l in usagepath.read_text().splitlines()] if usagepath.exists() else []
    usage=[r for r in usage if r['run_id']==final['state']['RunId']]
    summary={'passed':passed,'model':final['model'],'elapsed_seconds':time.monotonic()-start,'decisions':final['state']['Decisions'],'usage':usage,'jobs':[{k:j[k] for k in ['actor','goal','status','completed','gained','refills','stop_reason']} for j in final['work'] if j['command_id'] not in old_jobs],'scope':'three-job Flash integration with explicit lab fixture; not a full-day or all-achievement result'}
    Path('work/semantic-flash-result.json').write_text(json.dumps(summary,ensure_ascii=False,indent=2));print(json.dumps(summary,ensure_ascii=False),flush=True)
sys.exit(0 if passed else 1)
