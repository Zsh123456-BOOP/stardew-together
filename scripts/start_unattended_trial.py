"""Start one fresh native AgentLab trial, then exit. No gameplay supervisor/restarts.

The mod counts native nights and pauses at the requested day. Logs remain in
Mods/Together/logs; the launcher only keeps the game's console pipe alive.
"""
import argparse,hashlib,json,os,re,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge

def main():
 p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--mods-dir',type=Path,required=True);p.add_argument('--port',type=int,default=18796);p.add_argument('--days',type=int,default=7);p.add_argument('--config-root',type=Path);p.add_argument('--save-name');p.add_argument('--preflight',action='store_true');a=p.parse_args()
 if not 1<=a.days<=28:p.error('days must be 1..28')
 if a.save_name and (not a.config_root or not re.fullmatch(r'AgentLab_[0-9]+',a.save_name)):p.error('--save-name requires --config-root and AgentLab_<digits>')
 if a.config_root and not a.config_root.is_dir():p.error('--config-root must already exist')
 out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=a.mods_dir.resolve()
 def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
 cfg=mods/'Together/config.json';settings=json.loads(cfg.read_text());settings.update(RecordModelTrace=True,Autonomy=False,SinglePlayerAutoplay=True);cfg.write_text(json.dumps(settings,ensure_ascii=False,indent=2))
 write('manifest.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),days=a.days,mods=str(mods),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest(),bridge_dll_sha256=hashlib.sha256((mods/'AgentBridge/AgentBridge.dll').read_bytes()).hexdigest(),presentation_defaults=dict(windowed=True,muted=True,width=1280,height=800),model=settings.get('Model'),token_budget=settings.get('ModelTokenBudgetPerDay'),normal_time=True,supervised=False,restarts_allowed=False,scope='user trial, not 14-day acceptance'))
 with (out/'game.log').open('w') as log:
  command=[sys.executable,'scripts/launch.py','--companion','--lab','--keep-window','--mods-dir',str(mods),'--port',str(a.port)]
  if a.config_root:command+=['--config-root',str(a.config_root.resolve())]
  proc=subprocess.Popen(command,cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True,start_new_session=True)
  write('process.json',dict(launcher_pid=proc.pid,stdout=str(out/'game.log')))
  deadline=time.monotonic()+120;commanded=False;b=None
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('game_exited_before_start')
   try:
    c=json.loads((mods/'AgentBridge/config.json').read_text());private=out/'bridge-private.json';fd=os.open(private,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url=f'http://127.0.0.1:{a.port}',token=c['Token']),f)
    b=Bridge(private);h=b.request('GET','/health')
    if h['api_connected'] and not commanded:
     if a.config_root and Path(h.get('saves_path','')).resolve()!=a.config_root.resolve()/'StardewValley/Saves':raise ValueError('isolated_save_path_not_verified')
     proc.stdin.write(('agent_load '+a.save_name if a.save_name else 'agent_new')+'\n');proc.stdin.flush();commanded=True
    if h['ready']:break
   except (OSError,ValueError,RuntimeError):pass
   time.sleep(.5)
  if b is None or not commanded:raise RuntimeError('bridge_or_isolated_save_path_not_ready')
  state=b.state()
  if state['player']['name']!='AgentLab':raise RuntimeError('wrong_save')
  presentation=b.request('GET','/health').get('presentation',{})
  write('presentation.json',presentation)
  if not presentation.get('windowed') or any(presentation.get(k)!=0 for k in ('music','sound','ambient','footsteps')):raise RuntimeError('windowed_muted_defaults_not_applied')
  write('initial-world.json',state)
  if a.config_root:write('save-isolation.json',dict(config_root=str(a.config_root.resolve()),saves_path=h['saves_path'],save_name=a.save_name))
  if a.preflight:
   probe=b.request('POST','/lab/together',dict(session_id=b.session,scenario='on_demand_probe'));write('preflight.json',probe)
   if not probe.get('checks') or not all(probe['checks'].values()):raise RuntimeError('native_preflight_failed')
  goal='正常时间持续自主经营农场，以未来净收益、生产能力与任务推进为目标。只控制玩家，小禾关闭，不招募伙伴。自己决定种植、采购、采集、钓鱼、任务与投资取舍；维护连续计划，真实原生操作并正常睡觉过夜。你可以自主选择销毁普通未预留物品，先使用inventory.capacity核对用途、数量和真实腾格效果。经营和资金回流是目标，不是只睡觉刷天数。'
  r=b.request('POST','/lab/together',dict(session_id=b.session,scenario='survival_start',goal=goal,trial_days=a.days));write('start-receipt.json',r)
  d=b.request('GET','/lab/together')['autoplay'];write('started-state.json',d)
  if d['state']['Status']!='running' or d['state']['TrialTargetDay']!=d['snapshot']['day']+a.days:raise RuntimeError('trial_start_not_verified')
  write('log-index.json',dict(save_id=state['save_id'],run_id=d['state']['RunId'],native_logs=str(mods/'Together/logs'/str(state['save_id'])),game_log=str(out/'game.log'),trace_enabled=True,completion_event='user_trial_completed',stop_condition='native day target plus verified native SleepDays; window preserved',no_external_supervisor=True))
  # Keep-window launcher retains the native pipe after this one-time starter exits.
  proc.stdin.close();print(json.dumps(dict(started=True,output=str(out),run_id=d['state']['RunId'],target_day=d['state']['TrialTargetDay']),ensure_ascii=False))
if __name__=='__main__':main()
