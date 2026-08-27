"""Observe real Flash farming. No fixture, time change, resource grant or reset."""
from pathlib import Path
import argparse
import json
import sys
import time

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge


def main():
    parser = argparse.ArgumentParser()
    parser.add_argument('--start', action='store_true')
    parser.add_argument('--fresh', action='store_true', help='Require an untouched normal opening before starting')
    parser.add_argument('--seconds', type=int, default=3600)
    parser.add_argument('--interval', type=int, default=10)
    parser.add_argument('--pause-at-end', action='store_true')
    parser.add_argument('--out', default='work/farm-business-live')
    args = parser.parse_args()
    out = Path(args.out); out.mkdir(parents=True, exist_ok=True)
    b = Bridge(); state = b.state()
    if state.get('player', {}).get('name') != 'AgentLab':
        raise SystemExit('AgentLab only; no other save will be controlled.')

    def scenario(name, **values):
        return b.request('POST', '/lab/together', {'session_id': b.session, 'scenario': name, **values})

    def tool(name, **values):
        return scenario('agent_tool', tool=name, args=values)

    baseline = b.request('GET', '/lab/together')['autoplay']
    diagnostic=tool('agent.status')
    if args.fresh and (baseline['snapshot']['day']!=0 or baseline['snapshot']['money']!=500 or state.get('actors') or diagnostic['business']['machines'] or any(b['type'] not in {'Farmhouse','Greenhouse','Shipping Bin','Pet Bowl'} for b in diagnostic['business']['buildings'])):
        raise SystemExit('Fresh normal opening required; refusing to label fixture progression as natural gameplay.')
    trace=Path(__file__).resolve().parents[1]/'work/CompanionMods/Together/logs'/diagnostic['save_id']/diagnostic['save_epoch']
    (out/'run-info.json').write_text(json.dumps({'save_id':diagnostic['save_id'],'epoch':diagnostic['save_epoch'],'model':baseline.get('model'),'trace_directory':str(trace),'fresh_checks':args.fresh},ensure_ascii=False,indent=2))
    (out / 'baseline.json').write_text(json.dumps(baseline, ensure_ascii=False, indent=2))
    if args.start:
        tool('progress.pursue', enabled=False)
        tool('farm.business', enabled=True, expand=True, budget_per_day=2000, keep_gold=100, max_animals=12, max_machines=32, feed_days=7)
        scenario('agent_start', goal='自主经营这份农场，从正常开局发展到种植、畜牧、加工和酿酒。优先实际收益、现金周转和可维持的产能，不刷成就。使用farm.business持续经营，查询farm.business_status比较投资和瓶颈，随着真实收入提高合理的每日预算，保持种子和饲料周转金。程序已排的维护/生产不要重复派工；经营空档给玩家和伙伴独立安排有用途的采集、钓鱼、制作、采购、技能与经营所需解锁；临时缺材料要建立可执行供给计划。不要无理由发呆或提前睡觉，保持正常速度，真实回家睡觉并跨日续作。失败后核对状态、更换方案；不修改物资、时间或完成标记。与伙伴分享今天的安排和真实成果。')
    start = time.monotonic(); previous = None; samples = 0; last = baseline; errors = 0
    try:
        while time.monotonic() - start < args.seconds:
            try:
                last = b.request('GET', '/lab/together')['autoplay']; errors = 0
            except Exception as exc:
                errors += 1
                with (out / 'observer-errors.jsonl').open('a') as stream:
                    stream.write(json.dumps({'utc': time.time(), 'error': type(exc).__name__, 'count': errors}) + '\n')
                if errors >= 3:
                    break
                time.sleep(args.interval); continue
            a = last['state']; snapshot = last['snapshot']
            tasks = [{'id': t['spec']['id'], 'actor': t['spec']['actor'], 'tool': t['spec']['tool'], 'state': t['state'], 'purpose': t['spec']['purpose'], 'error': t.get('error')} for t in a['Schedule']['Tasks'] if t['state'] not in ('succeeded', 'cancelled')][-40:]
            sample = {'utc': time.time(), 'run_id': a['RunId'], 'snapshot': snapshot, 'status': a['Status'], 'detail': a['Detail'], 'decisions': a['Decisions'], 'normal_sleeps': a['SleepDays'], 'tasks': tasks}
            with (out / 'samples.jsonl').open('a') as stream:
                stream.write(json.dumps(sample, ensure_ascii=False) + '\n')
            (out / 'latest.json').write_text(json.dumps(last, ensure_ascii=False, indent=2))
            samples += 1
            signature = (snapshot.get('day'), a['Status'], a['Decisions'], tuple((t['id'], t['state']) for t in tasks))
            if signature != previous:
                print(json.dumps({'day': snapshot.get('day'), 'time': snapshot.get('time'), 'status': a['Status'], 'decisions': a['Decisions'], 'sleep': a['SleepDays'], 'active': [(t['actor'], t['tool']) for t in tasks if t['state'] == 'running']}, ensure_ascii=False), flush=True)
                previous = signature
            if a['Status'] != 'running':
                break
            time.sleep(args.interval)
    finally:
        if args.pause_at_end:
            b.session=None;b.state();scenario('agent_pause')
        from summarize_farm_logs import summarize
        (out/'summary.json').write_text(json.dumps(summarize(trace),ensure_ascii=False,indent=2))
        (out / 'result.json').write_text(json.dumps({'samples': samples, 'elapsed_seconds': time.monotonic()-start, 'last_status': last['state']['Status'], 'normal_sleeps': last['state']['SleepDays'], 'connection_failures': errors, 'paused_by_observer': args.pause_at_end, 'note': '运行观察，不代表跨季/后期已验收；权威动作日志在 Together/logs/<save>/<epoch>/day-*.jsonl。默认结束观察不暂停AI。'}, ensure_ascii=False, indent=2))


if __name__ == '__main__':
    main()
