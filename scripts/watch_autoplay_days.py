"""Observe two full native days. Only start/pause are issued; all gameplay decisions are the model's."""
from pathlib import Path
import json,sys,time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path(sys.argv[1] if len(sys.argv)>1 else 'work/autoplay-continuous');out.mkdir(exist_ok=True)
def scenario(name,**args):return b.request('POST','/lab/together',{'session_id':b.session,'scenario':name,**args})
def diag():return b.request('GET','/lab/together')['autoplay']
baseline=diag();(out/'baseline.json').write_text(json.dumps(baseline,ensure_ascii=False,indent=2))
scenario('agent_start',goal='独立控制玩家与Abigail持续经营这份农场，以原生成就、任务和解锁进度组织长期目标，按前置条件与季节窗口并行推进制作、建设和探索，不要只围绕尚未解锁的高级心愿转圈。读取百科和实际存档规划：照料作物、合理安排当季种植，按有用途的资源缺口分工采集、制作与采购，处理真实任务，充分利用白天但保留体力和返家时间。优先一次安排多个可确定步骤，让两人独立推进；遇到失败查状态并调整，不反复发同一错误任务。正常上床保存，次日接着经营，不修改日期、物资或完成标记。')
seen={(e['Kind'],e['Text']) for e in baseline['state']['Journal']};decisions=[];completed=[];overlap=False;day0=baseline['snapshot']['day'];start=time.monotonic();end=start+2100;passed=False
try:
    while time.monotonic()<end:
        d=diag();(out/'latest.json').write_text(json.dumps(d,ensure_ascii=False,indent=2))
        for event in d['state']['Journal']:
            identity=(event['Kind'],event['Text'])
            if identity not in seen:
                seen.add(identity)
                if event['Kind']=='decision':decisions.append(json.loads(event['Text']))
                if event['Kind']=='task_finished':completed.append(json.loads(event['Text']))
                with (out/'events.jsonl').open('a') as f:f.write(json.dumps({'observed_at':time.time(),'run_id':d['state']['RunId'],**event},ensure_ascii=False)+'\n')
        running=d['state']['Schedule']['Tasks'];active={t['spec']['actor'] for t in running if t['state']=='running'};overlap |= 'player' in active and len(active)>1
        print('day',d['snapshot']['day'],'time',d['snapshot']['time'],'decisions',d['state']['Decisions'],'sleep',d['state']['SleepDays'],'running',[(t['spec']['actor'],t['spec']['tool']) for t in d['state']['Schedule']['Tasks'] if t['state']=='running'],flush=True)
        if d['state']['Status']!='running':print('STOP:',d['state']['Detail'],flush=True);break
        if d['snapshot']['day']>=day0+2 and d['state']['SleepDays']>=2:
            passed=True;print('PASS: two native sleep/save transitions with continuing model control',flush=True);break
        time.sleep(5)
finally:
    scenario('agent_pause');final=diag();(out/'final.json').write_text(json.dumps(final,ensure_ascii=False,indent=2))
    (out/'result.json').write_text(json.dumps({'passed':passed,'model':final.get('model','unknown'),'elapsed_seconds':time.monotonic()-start,'start_day':day0,'end_day':final['snapshot']['day'],'normal_sleeps':final['state']['SleepDays'],'actor_overlap_observed':overlap,'model_decisions':len(decisions),'completed_tasks':completed,'note':'Two transitions are necessary, not sufficient for efficiency or all-achievement claims; review the event trace.'},ensure_ascii=False,indent=2))
