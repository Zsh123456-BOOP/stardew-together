"""Isolated AgentLab integration checks; never runs fixtures on a personal save.

Creates a fresh test farmer, checks partial storage, resume and a
real overnight profession choice. Outputs evidence, not a simulated pass.
"""
import argparse,json,os,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge

def main():
 p=argparse.ArgumentParser();p.add_argument('--mods-dir',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=18801);a=p.parse_args()
 out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=a.mods_dir.resolve()
 def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
 config=mods/'Together/config.json';settings=json.loads(config.read_text());settings.update(Autonomy=False,RecordModelTrace=True,SinglePlayerAutoplay=True);config.write_text(json.dumps(settings,ensure_ascii=False,indent=2))
 with (out/'game.log').open('w') as log:
  proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--keep-window','--mods-dir',str(mods),'--port',str(a.port)],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True,start_new_session=True)
  write('process.json',{'launcher_pid':proc.pid});commanded=False;b=None
  try:
   deadline=time.monotonic()+90
   while time.monotonic()<deadline:
    if proc.poll() is not None:raise RuntimeError('game_exited_before_ready')
    try:
     c=json.loads((mods/'AgentBridge/config.json').read_text());private=out/'bridge-private.json';fd=os.open(private,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
     with os.fdopen(fd,'w') as f:json.dump({'url':f'http://127.0.0.1:{a.port}','token':c['Token']},f)
     b=Bridge(private);h=b.request('GET','/health')
     if h['api_connected'] and not commanded:proc.stdin.write('agent_new\n');proc.stdin.flush();commanded=True
     if h['ready']:break
    except (OSError,ValueError,RuntimeError):pass
    time.sleep(.5)
   if b is None:raise RuntimeError('no_bridge')
   state=b.state();assert state['player']['name']=='AgentLab';write('initial.json',state)
   def sc(mode,**args):return b.request('POST','/lab/together',dict(session_id=b.session,scenario='memory_rebuild',mode=mode,**args))
   def tool(name,**args):return b.request('POST','/lab/together',dict(session_id=b.session,scenario='agent_tool',tool=name,args=args))
   time.sleep(3)
   resume=sc('resume');write('resume.json',resume);assert resume['goal_preserved'] and resume['SleepDays']==3 and resume['TrialTargetSleeps']==7;print('PASS full goal and counters resume',flush=True)
   sc('store');time.sleep(2)
   r=tool('work.run',goal='store',location='Farm',required_free_slots=0,until=2300);deadline=time.monotonic()+60
   while r['status']=='running' and time.monotonic()<deadline:time.sleep(.25);r=tool('action.status',id=r['command_id'])
   write('storage.json',r);assert r['status']=='succeeded' and r.get('deposited',0)>0;print('PASS native partial-stack storage',flush=True)
   recovery=sc('recovery');write('recovery.json',recovery);assert recovery['continued'] and recovery['manual_pause_preserved'];print('PASS recoverable failure isolation and manual pause',flush=True)
   b.state();sc('night');time.sleep(3);night=tool('player.sleep',reason='原生过夜回归测试，已23点',review='升级职业后必须正常保存并进入次日');write('night-start.json',night);deadline=time.monotonic()+120;finished=None
   while time.monotonic()<deadline:
    time.sleep(1)
    try:
     b.state();d=b.request('GET','/lab/together');a2=d['autoplay'];finished=a2
     if a2['state']['SleepDays']>=a2['state']['TrialTargetSleeps'] and a2['state']['Status']=='paused':break
    except RuntimeError:pass
   write('night-final.json',finished)
   assert finished and finished['state']['SleepDays']>=finished['state']['TrialTargetSleeps']
   result=sc('read');assert any(p in (6,7) for p in result['professions']);assert result['card']['ProfessionChoices'];print('PASS native profession choice, save and next-day completion',flush=True)
   reflection=sc('reflection',native_receipt=r);write('reflection-start.json',reflection);assert reflection['requested'];deadline=time.monotonic()+65
   while not result['memory'].get('reflections') and time.monotonic()<deadline:time.sleep(1);result=sc('read')
   write('memory-final.json',result);assert result['memory'].get('reflections');print('PASS model reflection with verified native evidence IDs',flush=True)
   write('result.json',dict(passed=True,scope='isolated native fixtures, not seven-day acceptance'))
  except Exception as e:
   write('result.json',dict(passed=False,error=str(e)));raise
  finally:
   if proc.poll() is None:proc.stdin.write('agent_quit\n');proc.stdin.flush();proc.stdin.close()

if __name__=='__main__':main()
