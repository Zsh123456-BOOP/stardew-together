"""Stage A supervisor. Owns one isolated game; never edits game/save data.

Crash recovery is automatic, but ANY restart disqualifies the uninterrupted
14-day gate. Evidence is retained; it cannot be stitched into a passing run.
"""
import argparse
import hashlib
import json
import os
import signal
import shutil
from pathlib import Path
import subprocess
import sys
import time

ROOT = Path(__file__).resolve().parents[1]
sys.path.insert(0, str(ROOT))
from agent.client import Bridge, BridgeError
from root_failure_watch import RootFailureWatch


def main():
    p = argparse.ArgumentParser()
    p.add_argument('--output', required=True, type=Path)
    p.add_argument('--mods-dir', default='work/SurvivalMods', type=Path)
    p.add_argument('--port', default=18768, type=int)
    p.add_argument('--days', default=14, type=int)
    p.add_argument('--trial', action='store_true', help='User-requested autonomous trial, never 14-day gate credit')
    p.add_argument('--exit-on-stop', action='store_true', help='Explicitly close owned game after capturing evidence; default preserves window')
    p.add_argument('--baseline', action='store_true', help='Exactly three measurement days; never gate credit')
    p.add_argument('--g3-min-ratio', type=float, help='User-approved threshold only; mandatory for the 14-day gate')
    p.add_argument('--seconds', default=18000, type=int)
    p.add_argument('--load', help='Existing AgentLab checkpoint; recovery check only, never clean acceptance')
    p.add_argument('--recovery-probe', action='store_true', help='AgentLab-only: inject six local failures, crash owned process after native save, verify automatic reload')
    args = p.parse_args()
    if not 1 <= args.days <= 28: p.error('days must be 1..28')
    if args.baseline and args.days!=3:p.error('baseline must be exactly three days')
    if args.days==14 and not args.recovery_probe and args.g3_min_ratio is None:p.error('14-day gate requires the user-approved G3 threshold after baseline')
    if args.g3_min_ratio is not None and not 0<=args.g3_min_ratio<=1:p.error('G3 ratio must be 0..1')
    out = args.output.resolve(); out.mkdir(parents=True, exist_ok=False)
    mods = args.mods_dir.resolve()
    proc = None; stream = None; bridge = None
    restarts = 0; saved_days = set(); last = None; initial = None; save_name = args.load
    failure = 'observer_timeout'; passed = False; probe_passed = False; probe_checkpoint = None; baseline_completed=False; trial_completed=False; quality_result={}
    start = time.monotonic(); start_day = None; start_sleeps = 0; run_id = None
    last_progress_day = -1; crashes_here = 0
    trace_verified=False
    root_watch=RootFailureWatch();log_offsets={};last_full=0;failed_attempts={};observed_attempts=set()

    def write(name, data):
        (out/name).write_text(json.dumps(data, ensure_ascii=False, indent=2))

    def event(kind, **data):
        with (out/'supervisor.jsonl').open('a') as f:
            f.write(json.dumps(dict(utc=time.time(),kind=kind,**data),ensure_ascii=False)+'\n')

    def launch(name):
        nonlocal proc, stream, bridge
        stream = (out/f'game-{restarts}.log').open('w')
        proc = subprocess.Popen([sys.executable, 'scripts/launch.py', '--companion', '--lab',
                                 '--mods-dir', str(mods), '--port', str(args.port), *([] if args.exit_on_stop else ['--keep-window'])],
                                cwd=ROOT, stdin=subprocess.PIPE, stdout=stream,
                                stderr=subprocess.STDOUT, text=True, start_new_session=True)
        event('process_started', pid=proc.pid, restart=restarts, save=name)
        until = time.monotonic()+120; commanded = False
        while time.monotonic()<until:
            if proc.poll() is not None: raise RuntimeError('game_exited_during_start')
            try:
                config = json.loads((mods/'AgentBridge/config.json').read_text())
                local = out/'bridge-private.json'
                fd = os.open(local,os.O_WRONLY|os.O_CREAT|os.O_TRUNC,0o600)
                with os.fdopen(fd,'w') as f: json.dump(dict(url=f'http://127.0.0.1:{args.port}',token=config['Token']),f)
                candidate = Bridge(local); h = candidate.request('GET','/health')
                if h['api_connected'] and not commanded:
                    proc.stdin.write(('agent_load '+name if name else 'agent_new')+'\n');proc.stdin.flush();commanded=True
                if h['ready']:
                    bridge=candidate; state=bridge.state()
                    if state['player']['name']!='AgentLab':raise RuntimeError('wrong_save')
                    return state
            except (OSError,ValueError,RuntimeError) as e:
                if str(e)=='wrong_save':raise
            time.sleep(1)
        raise RuntimeError('game_start_timeout')

    def scenario(name, **data):
        return bridge.request('POST','/lab/together',dict(session_id=bridge.session,scenario=name,**data))

    try:
        write('manifest.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),days=args.days,baseline=args.baseline,normal_speed=True,mods=str(mods),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest()))
        # Baselines are reviewable only with full HTTP bodies (never auth headers).
        config_path=mods/'Together/config.json'
        config=json.loads(config_path.read_text()) if config_path.exists() else {}
        config['RecordModelTrace']=True
        config_path.write_text(json.dumps(config,ensure_ascii=False,indent=2))
        write('trace-preflight.json',dict(enabled=True,verified_request=False,verified_response=False))
        initial=launch(save_name)
        save_name='AgentLab_'+initial['save_id']
        last=bridge.request('GET','/lab/together')['autoplay']
        start_day=last['snapshot']['day'];start_sleeps=last['state']['SleepDays']
        write('baseline.json',dict(world=initial,autoplay=last,recovery_only=bool(args.load)))
        if args.load:
            if last['state']['Status']!='running' or last['state']['Survival']['Resumes']<1:
                raise RuntimeError('checkpoint_did_not_auto_resume')
        else:
            if start_day!=0 or last['snapshot']['time']>700 or last['snapshot']['money']!=500:
                raise RuntimeError('not_fresh_native_start')
            scenario('survival_start',goal=f'正常时间连续自主经营{args.days}天，所有劳动由玩家本体执行，小禾关闭且不在场，不招募或派工给任何NPC。领取原生初始种子，规划连片农田、种植浇水收获，适度采集备料、采购补种与出货，优先维持农务和资金周转。使用day.routine把water、harvest、feed、pet分配给player。配方目标优先goal.create(run:true)自动备料并连续制作放置；用plan.submit一次排多个可确定步骤，基础劳动用work.run。晚间使用原生player.sleep，不能修改时间、物资或进度。目标是持续真实经营，不是只连续睡觉刷天数。日志将核验每天实际行动和降级原因。')
            if args.recovery_probe:
                for _ in range(6):scenario('survival_failure_probe',kind='local')
        run_id=bridge.request('GET','/lab/together')['autoplay']['state']['RunId']
        target=start_day+args.days
        while time.monotonic()-start<args.seconds:
            if proc.poll() is not None:
                event('process_exited',returncode=proc.returncode,last_day=last['snapshot']['day'])
                if proc.returncode==0:raise RuntimeError('game_closed_no_crash_restart')
                if args.baseline or args.trial:raise RuntimeError('trial_crash_no_stitching')
                crashes_here+=1
                if crashes_here>=3:raise RuntimeError('same_checkpoint_crashed_three_times')
                if not saved_days and not args.load:raise RuntimeError('crash_before_first_native_checkpoint')
                restarts+=1;stream.close();launch(save_name)
            try:
                diagnostic=bridge.request('GET','/lab/together');last=diagnostic['autoplay']
                if time.monotonic()-last_full>=30:
                    last_full=time.monotonic()
                    with (out/'diagnostics.jsonl').open('a') as full:full.write(json.dumps(dict(utc=time.time(),data=diagnostic),ensure_ascii=False)+'\n')
            except BridgeError:
                # Let an exiting native process finish before deciding whether
                # this is a crash or an unresponsive live game. Never spawn a duplicate.
                try:proc.wait(timeout=5)
                except subprocess.TimeoutExpired:raise RuntimeError('live_game_bridge_unresponsive')
                continue
            s=last['snapshot'];a=last['state']
            for path in (mods/'Together/logs'/str(initial['save_id'])).glob('*/day-*.jsonl'):
                with path.open() as f:
                    f.seek(log_offsets.get(str(path),0))
                    while True:
                        offset=f.tell();line=f.readline()
                        if not line:break
                        if not line.endswith('\n'):f.seek(offset);break
                        ev=json.loads(line)
                        if ev.get('run')!=run_id:continue
                        stop=root_watch.observe(ev)
                        if stop:
                            write('repeated-root-stop.json',dict(**stop,autoplay=last))
                            scenario('agent_pause')
                            write('repeated-root-paused.json',bridge.request('GET','/lab/together'))
                            raise RuntimeError('same_root_three_distinct_attempts')
                    log_offsets[str(path)]=f.tell()
            if not trace_verified:
                traces=list((mods/'Together/logs'/str(initial['save_id'])).glob('*/model-'+run_id+'.jsonl'))
                kinds={json.loads(line)['kind'] for path in traces for line in path.read_text().splitlines() if line.endswith('}')}
                if 'response' in kinds:
                    if 'request' not in kinds:raise RuntimeError('model_trace_request_missing')
                    write('trace-preflight.json',dict(enabled=True,verified_request=True,verified_response=True))
                    trace_verified=True
                elif a['Decisions']>0:raise RuntimeError('model_trace_response_missing')
            if a['RunId']!=run_id:raise RuntimeError('run_identity_changed')
            for task in a.get('Schedule',{}).get('Tasks',[]):
                cid=task.get('command_id');state=task.get('state');spec=task['spec']
                if not cid or state not in ('failed','succeeded') or cid in observed_attempts:continue
                observed_attempts.add(cid)
                key=(spec['actor'],spec['tool'],spec.get('args',{}).get('goal',''),spec.get('args',{}).get('location',''))
                if state=='succeeded':failed_attempts.pop(key,None);continue
                error=task.get('error','');old_error,count=failed_attempts.get(key,('',0));count=count+1 if old_error==error else 1;failed_attempts[key]=(error,count)
                if count>=3:
                    write('repeated-operation-stop.json',dict(operation=key,error=error,attempts=count,autoplay=last))
                    raise RuntimeError('same_operation_three_failures:'+error)
            if args.recovery_probe and restarts:
                current=bridge.state()
                probe_passed=(a['Status']=='running' and a['Survival']['Resumes']==1 and s['day']==probe_checkpoint['day'] and current['player']['inventory']==probe_checkpoint['inventory'])
                write('recovery-evidence.json',dict(checkpoint=probe_checkpoint,reloaded=last,world=current,verified=probe_passed))
                failure='native_checkpoint_auto_recovery_verified' if probe_passed else 'recovery_evidence_mismatch'
                break
            if s['day']>last_progress_day:
                last_progress_day=s['day'];crashes_here=0
            confirmed=a['Survival']['LastSavedDay']
            if confirmed>start_day and confirmed not in saved_days:
                save_path=Path.home()/'.config/StardewValley/Saves'/save_name/save_name
                if not save_path.is_file():raise RuntimeError('native_save_file_missing')
                saved_days.add(confirmed)
                event('native_checkpoint',day=confirmed,sha256=hashlib.sha256(save_path.read_bytes()).hexdigest(),bytes=save_path.stat().st_size)
                write(f'day-{confirmed}-checkpoint.json',last)
                if args.recovery_probe and probe_checkpoint is None:
                    probe_checkpoint=dict(day=confirmed,inventory=bridge.state()['player']['inventory'])
                    # Only the native child of OUR launcher may be killed.
                    rows=subprocess.check_output(['ps','-axo','pid=,ppid=,comm='],text=True).splitlines()
                    children=[int(row.split(None,2)[0]) for row in rows if len(row.split(None,2))==3 and row.split(None,2)[1]==str(proc.pid) and row.split(None,2)[2].endswith('StardewModdingAPI')]
                    if len(children)!=1:raise RuntimeError('owned_native_process_not_unique')
                    event('lab_crash_injected',pid=children[0],checkpoint=confirmed)
                    os.kill(children[0],signal.SIGKILL);proc.wait(timeout=15);continue
            row=dict(utc=time.time(),restart=restarts,day=s['day'],time=s['time'],status=a['Status'],detail=a['Detail'],mode=a['Survival']['Mode'],reason=a['Survival']['Reason'],sleeps=a['SleepDays'],decisions=a['Decisions'],verified_actions=a['VerifiedActions'],cash=s['money'],energy=s['stamina'],location=s['location'],tile=s['tile'],menu=s['menu'],action=s['player_action'],model_pending=last['pending'])
            with (out/'samples.jsonl').open('a') as f:f.write(json.dumps(row,ensure_ascii=False)+'\n')
            write('latest.json',last);print(json.dumps(row,ensure_ascii=False),flush=True)
            if a['Status']!='running':raise RuntimeError(a['Detail'])
            if s['day']>=target and a['SleepDays']-start_sleeps>=args.days and not s['menu'] and not s['event_up']:
                expected=set(range(start_day+1,target+1))
                uninterrupted=not args.load and restarts==0 and expected<=saved_days
                q=a.get('Quality',{});days=sorted([d for d in q.get('Days',[]) if d.get('Finalized') and start_day<=d['Day']<target],key=lambda d:d['Day'])
                ratios=[d.get('AwakeLaborRatio') for d in days]
                if any(r is None for r in ratios):raise RuntimeError('actual_sleep_time_accounting_missing')
                g1=all(d.get('DegradationTime') is None or not 600<=d['DegradationTime']<=1200 for d in days)
                g2=not any(left['VerifiedActionsDelta']==right['VerifiedActionsDelta']==0 and right['Day']==left['Day']+1 for left,right in zip(days,days[1:]))
                net=(days[-1]['CashEnd']+days[-1]['AssetsEnd']-days[0]['CashStart']-days[0]['AssetsStart']) if days else 0
                g4=net>0 and q.get('CycleCompleted',False)
                quality_result=dict(days=days,g1=g1,g2=g2,g3_ratios=ratios,g3_threshold=args.g3_min_ratio,g4=g4,net_asset_cash_change=net,cycle_evidence=q.get('CycleEvidence',[]),complete_day_evidence=len(days)==args.days)
                write('quality-summary.json',quality_result)
                baseline_completed=bool(args.baseline and uninterrupted and len(days)==3)
                trial_completed=bool(args.trial and uninterrupted and len(days)==args.days)
                passed=bool(uninterrupted and args.days==14 and len(days)==14 and g1 and g2 and g4 and args.g3_min_ratio is not None and all(r>=args.g3_min_ratio for r in ratios))
                failure='14_uninterrupted_native_days_and_quality' if passed else 'three_day_measurement_only_user_G3_pending' if baseline_completed else 'user_requested_trial_complete_not_14_day_gate' if trial_completed else 'completed_but_gate_not_passed'
                break
            time.sleep(5)
    except Exception as e:
        failure=type(e).__name__+':'+str(e);event('stopped',reason=failure)
    finally:
        write('result.json',dict(passed=passed,recovery_probe_passed=probe_passed,reason=failure,restarts=restarts,saved_days=sorted(saved_days),start_day=start_day,last_day=last['snapshot']['day'] if last else None,elapsed=time.monotonic()-start,scope='Stage A continuity plus G1/G2/G4; G3 is measured in baseline and user-defined for gate',baseline_completed=baseline_completed,trial_completed=trial_completed,quality=quality_result))
        if bridge:
            try:scenario('agent_pause')
            except Exception:pass
        if proc and proc.poll() is None and args.exit_on_stop:
            proc.stdin.write('agent_quit\n');proc.stdin.flush()
            try:proc.wait(timeout=30)
            except subprocess.TimeoutExpired:event('exit_pending',pid=proc.pid)
        if stream:stream.close()
        if last and initial:
            source=mods/'Together/logs'/str(initial['save_id'])
            if source.exists():shutil.copytree(source,out/'native-logs',dirs_exist_ok=True)
            usage=mods/'Together/usage'
            (out/'usage').mkdir(exist_ok=True)
            for path in usage.glob(str(initial['save_id'])+'-*.json'):shutil.copy2(path,out/'usage'/path.name)
            if 'diagnostic' in locals():write('final-diagnostics.json',diagnostic)
            if bridge:
                save_path=Path.home()/'.config/StardewValley/Saves'/save_name
                if save_path.is_dir():shutil.copytree(save_path,out/'native-save',dirs_exist_ok=True)
            files=[]
            for path in out.rglob('*'):
                if path.is_file() and path.name!='bridge-private.json':files.append(dict(path=str(path.relative_to(out)),bytes=path.stat().st_size,sha256=hashlib.sha256(path.read_bytes()).hexdigest()))
            write('evidence-index.json',files)
    return 0 if passed or probe_passed or baseline_completed or trial_completed else 1


if __name__=='__main__':sys.exit(main())
