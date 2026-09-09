"""Bounded live Flash observation; no fixtures, no automatic rescue or budget increase."""
import argparse
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge

p = argparse.ArgumentParser()
p.add_argument("--output", required=True)
p.add_argument("--seconds", type=int, default=180)
p.add_argument("--decisions", type=int, default=8)
args = p.parse_args()
b = Bridge()
initial = b.state()
assert initial["player"]["name"] == "AgentLab"
out = Path(args.output)
out.mkdir(parents=True, exist_ok=False)

def sc(name, **values):
    return b.request("POST", "/lab/together", dict(session_id=b.session, scenario=name, **values))

def read():
    return b.request("GET", "/lab/together")

baseline = read()
assert baseline["autoplay"]["model"] == "deepseek-flash"
(out / "baseline.json").write_text(json.dumps(dict(state=initial, diagnostics=baseline), ensure_ascii=False, indent=2))
sc("agent_start", goal="短程验证现有农场经营：优先处理今天真实成熟作物、照料与已批准投资，和自定义伙伴小禾分工。沿用已有经营预算及片区政策，不追加额度、不招募原生村民。普通清理、采购和补种由已启用程序队列执行，不重复派工。检查真实工具结果，必要时查百科并调整经营方向。")
started = time.monotonic()
seen = set()
last = baseline
reason = "time_limit"
try:
    while time.monotonic() - started < args.seconds:
        last = read()
        d = last["autoplay"]
        s, snap = d["state"], d["snapshot"]
        row = dict(utc=time.time(), snapshot=snap, status=s["Status"], detail=s["Detail"], decisions=s["Decisions"], pending=d["pending"], model_context=d["model_context"], performance=last["performance"])
        with (out / "samples.jsonl").open("a") as f:
            f.write(json.dumps(row, ensure_ascii=False) + "\n")
        for e in s["Journal"]:
            key = (e["Kind"], e["Text"])
            if key not in seen:
                seen.add(key)
                with (out / "events.jsonl").open("a") as f:
                    f.write(json.dumps(e, ensure_ascii=False) + "\n")
        print(json.dumps(dict(day=snap["day"], time=snap["time"], cash=snap["money"], decisions=s["Decisions"], status=s["Status"])), flush=True)
        if s["Status"] != "running":
            reason = "runtime_stopped:" + s["Detail"]
            break
        if snap["day"] != baseline["autoplay"]["snapshot"]["day"]:
            reason = "day_boundary"
            break
        if s["Decisions"] >= args.decisions and not d["pending"]:
            reason = "decision_limit"
            break
        time.sleep(5)
finally:
    (out / "pre-pause.json").write_text(json.dumps(last, ensure_ascii=False, indent=2))
    sc("agent_pause")
    sc("agent_ui")
    (out / "result.json").write_text(json.dumps(dict(reason=reason, elapsed_seconds=time.monotonic()-started, note="Bounded smoke observation, not a continuous multi-day acceptance run", snapshot=last["autoplay"]["snapshot"], performance=last["performance"]), ensure_ascii=False, indent=2))
