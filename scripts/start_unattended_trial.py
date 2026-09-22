"""Start one fresh native AgentLab trial, then exit. No gameplay supervisor/restarts.

The mod counts native nights and pauses at the requested day. Logs remain in
Mods/Together/logs; the launcher only keeps the game's console pipe alive.
"""
import argparse,hashlib,json,os,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge

def main():
 p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--mods-dir',type=Path,required=True);p.add_argument('--port',type=int,default=18796);p.add_argument('--days',type=int,default=7);a=p.parse_args()
 if not 1<=a.days<=28:p.error('days must be 1..28')
 out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=a.mods_dir.resolve()
 def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
 cfg=mods/'Together/config.json';settings=json.loads(cfg.read_text());settings.update(RecordModelTrace=True,Autonomy=False,SinglePlayerAutoplay=True);cfg.write_text(json.dumps(settings,ensure_ascii=False,indent=2))
 write('manifest.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),days=a.days,mods=str(mods),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest(),model=settings.get('Model'),token_budget=settings.get('ModelTokenBudgetPerDay'),normal_time=True,supervised=False,restarts_allowed=False,scope='user trial, not 14-day acceptance'))
 with (out/'game.log').open('w') as log:
  proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--keep-window','--mods-dir',str(mods),'--port',str(a.port)],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True,start_new_session=True)
  write('process.json',dict(launcher_pid=proc.pid,stdout=str(out/'game.log')))
  deadline=time.monotonic()+120;commanded=False;b=None
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('game_exited_before_start')
   try:
    c=json.loads((mods/'AgentBridge/config.json').read_text());private=out/'bridge-private.json';fd=os.open(private,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url=f'http://127.0.0.1:{a.port}',token=c['Token']),f)
    b=Bridge(private);h=b.request('GET','/health')
    if h['api_connected'] and not commanded:proc.stdin.write('agent_new\n');proc.stdin.flush();commanded=True
    if h['ready']:break
   except (OSError,ValueError,RuntimeError):pass
   time.sleep(.5)
  if b is None:raise RuntimeError('bridge_not_ready')
  state=b.state()
  if state['player']['name']!='AgentLab':raise RuntimeError('wrong_save')
  write('initial-world.json',state)
  goal=f'正常时间自主经营连续{a.days}个游戏日。只控制玩家，小禾关闭，不招募伙伴。自己决定种植、采购、采集、钓鱼、任务与投资取舍；维护连续计划，真实原生操作并正常睡觉过夜。你可以自主选择销毁普通未预留物品，先使用inventory.capacity核对用途、数量和真实腾格效果。经营和资金回流是目标，不是只睡觉刷天数。'
  r=b.request('POST','/lab/together',dict(session_id=b.session,scenario='survival_start',goal=goal,trial_days=a.days));write('start-receipt.json',r)
  d=b.request('GET','/lab/together')['autoplay'];write('started-state.json',d)
  if d['state']['Status']!='running' or d['state']['TrialTargetDay']!=d['snapshot']['day']+a.days:raise RuntimeError('trial_start_not_verified')
  write('log-index.json',dict(save_id=state['save_id'],run_id=d['state']['RunId'],native_logs=str(mods/'Together/logs'/str(state['save_id'])),game_log=str(out/'game.log'),trace_enabled=True,completion_event='user_trial_completed',stop_condition='native day target plus verified native SleepDays; window preserved',no_external_supervisor=True))
  # Keep-window launcher retains the native pipe after this one-time starter exits.
  proc.stdin.close();print(json.dumps(dict(started=True,output=str(out),run_id=d['state']['RunId'],target_day=d['state']['TrialTargetDay']),ensure_ascii=False))
if __name__=='__main__':main()
