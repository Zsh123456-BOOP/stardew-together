"""Seven native days with Flash. Read-only observation after one authorized start.
Failure preserves evidence; no fixture commands, token increases or automatic rescue.
"""
import argparse,json,sys,time
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
p=argparse.ArgumentParser();p.add_argument('--output',default='work/farm-week');p.add_argument('--seconds',type=int,default=9000);args=p.parse_args()
b=Bridge();s=b.state();assert s['player']['name']=='AgentLab'
out=Path(args.output);out.mkdir(parents=True,exist_ok=False)
def scenario(code,**values):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**values))
def tool(code,**values):return scenario('agent_tool',tool=code,args=values)
def read():return b.request('GET','/lab/together')['autoplay']
initial=read();snap=initial['snapshot'];diag=tool('agent.status')
if snap['day']!=0 or snap['time']>700 or snap['money']!=500 or any(a['name']!='Together_Partner' for a in s['actors']):raise SystemExit('Fresh day-one native opening plus custom partner required')
(out/'baseline.json').write_text(json.dumps(dict(state=s,autoplay=initial,diagnostics=diag),ensure_ascii=False,indent=2))
tool('farm.business',enabled=True,expand=True,budget_per_day=2000,keep_gold=100,max_animals=12,max_machines=32,feed_days=7)
tool('farm.operating',direction='balanced',player_water_limit=24,partner_water_limit=24,reason='先发展可持续种植和周转，再按真实瓶颈发展畜牧加工')
scenario('agent_start',goal='从正常开局自主经营连续七个完整游戏日。你控制玩家，与我们创建的小禾Together_Partner协作，不招募原生村民。优先稳定农业、实际现金回款与有用途的资源积累；逐步发展畜牧、加工和酿酒，不以刷成就为主线。程序经营政策负责日常维护和伙伴材料分工，不重复派相同任务。通过farm.production查询用途和候选，select批准设备等投资，make批准其他有用配方；不要为了清包卖掉有用途的材料，surplus仅在比较用途后授权今日余量出售。你负责经营空档的有效安排、采购和解锁、解释重要取舍、处理真实失败；查询farm.business_status和farm.operating掌握实际预算、在途货物和缺口。初始种子要通过原生交互领取。田地先规划再整块清障播种，不堵门口；材料优先补箱子和生产所需，完整伐木用玩家include_trees，伙伴只做已适配劳动。保留农务、补给与回家时间，低体力可做交接整理；必要时合理休息，不能刚种完就无理由睡觉。普通动作无需反复问模型，计划一次排多个可确定步骤，两条队列独立执行。晚上真实回家睡觉，处理升级结算并保存，次日继续经营。时间正常，不改物资、日期或进度。')
start=time.monotonic();seen=set();passed=False;reason='observer_timeout';last=initial;overlap=False;events=[]
try:
 while time.monotonic()-start<args.seconds:
  last=read();a=last['state'];snap=last['snapshot'];tasks=a['Schedule']['Tasks'];active=[t for t in tasks if t['state']=='running'];overlap|=any(t['spec']['actor']=='player' for t in active) and any(t['spec']['actor']!='player' for t in active)
  row=dict(utc=time.time(),day=snap['day'],time=snap['time'],status=a['Status'],detail=a['Detail'],cash=snap['money'],stamina=snap['stamina'],sleeps=a['SleepDays'],decisions=a['Decisions'],active=[dict(actor=t['spec']['actor'],tool=t['spec']['tool'],purpose=t['spec']['purpose']) for t in active])
  with (out/'samples.jsonl').open('a') as f:f.write(json.dumps(row,ensure_ascii=False)+'\n')
  (out/'latest.json').write_text(json.dumps(last,ensure_ascii=False,indent=2))
  for ev in a['Journal']:
   key=(ev['Kind'],ev['Text'])
   if key not in seen:
    seen.add(key)
    with (out/'events.jsonl').open('a') as f:f.write(json.dumps(ev,ensure_ascii=False)+'\n')
  print(json.dumps(row,ensure_ascii=False),flush=True)
  if a['Status']!='running':reason=a['Detail'];break
  if snap['day']>=7 and a['SleepDays']>=7 and snap['menu'] is None and not snap['event_up']:
   passed=True;reason='seven_native_days_completed';break
  time.sleep(10)
except Exception as exc:
 reason=type(exc).__name__+':'+str(exc)
finally:
 (out/'pre-pause.json').write_text(json.dumps(last,ensure_ascii=False,indent=2))
 try:
  scenario('agent_pause');scenario('agent_ui')
 except Exception:pass
 (out/'result.json').write_text(json.dumps(dict(passed=passed,reason=reason,elapsed=time.monotonic()-start,last_day=last['snapshot']['day'],normal_sleeps=last['state']['SleepDays'],overlap=overlap,model=last.get('model'),scope='native opening plus custom NPC; success here is continuity only, profitability/maintenance require trace audit'),ensure_ascii=False,indent=2))
print(json.dumps(dict(passed=passed,reason=reason),ensure_ascii=False),flush=True)
raise SystemExit(0 if passed else 1)
