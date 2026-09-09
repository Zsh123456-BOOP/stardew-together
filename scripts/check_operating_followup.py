"""Grouped native follow-up: condition memory, mixed partner work, harvest/reinvestment.

Uses explicit fixtures in a disposable AgentLab save only. No model or formal-save resources.
"""
import json
import sys
import time
from pathlib import Path

sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge

b = Bridge()
assert b.state()["player"]["name"] == "AgentLab"
out = Path(sys.argv[1] if len(sys.argv) > 1 else "work/operating-followup-native")
out.mkdir(parents=True, exist_ok=True)
checks = []

def sc(name, **kw):
    return b.request("POST", "/lab/together", dict(session_id=b.session, scenario=name, **kw))

def tool(name, **kw):
    return sc("agent_tool", tool=name, args=kw)

def check(ok, label):
    checks.append(dict(passed=bool(ok), label=label))
    (out / "checks.json").write_text(json.dumps(checks, ensure_ascii=False, indent=2))
    print(("PASS " if ok else "FAIL ") + label, flush=True)
    assert ok, label

def wait(test, timeout=180):
    end = time.monotonic() + timeout
    while time.monotonic() < end:
        state = sc("operating_followup_read")
        with (out / "samples.jsonl").open("a") as f:
            f.write(json.dumps(state, ensure_ascii=False) + "\n")
        if test(state):
            return state
        time.sleep(.5)
    return state

try:
    sc("operating_followup_fixture")
    time.sleep(1)
    evidence = sc("operating_failure_probe")
    (out / "failure-memory.json").write_text(json.dumps(evidence, indent=2))
    check(evidence["unrelated_energy_and_quantity_blocked"], "changing stamina or quantity cannot bypass the same material policy failure")
    check(evidence["real_policy_change_released"], "approving the missing project releases its condition memory")
    sc("agent_schedule_probe")
    state = wait(lambda s: s["remaining"] == 0 and any(o["Id"]=="lab-mixed" and o["CompletedToday"]==6 for o in s["orders"]))
    (out / "cleanup.json").write_text(json.dumps(state, ensure_ascii=False, indent=2))
    check(state["remaining"] == 0, "idle custom partner clears all six mixed targets from one approved patch")
    check(state["protected_present"], "protected neighboring weeds remain untouched")
    order = next(o for o in state["orders"] if o["Id"] == "lab-mixed")
    check(order["CompletedToday"] == 6, "shared daily cleanup allowance counts actual completed targets")
    check(state["calls"] == 0, "mixed cleaning and automatic target selection need no model callbacks")
    cargo = sum(v for actor in state["partner"] for v in actor["cargo"].values())
    check(cargo > 0, "partner labor yields real cargo instead of only animation")
    sc("agent_pause")
    sc("operating_reinvestment_fixture")
    time.sleep(1)
    before = sc("operating_followup_read")
    locked = False
    try:
        tool("farm.economy", location="Greenhouse", budget=200, keep_gold=100, plots=6)
    except Exception as exc:
        locked = "greenhouse_not_unlocked" in str(exc)
    check(locked, "locked native greenhouse is rejected before any planning or travel")
    check(before["ripe"] == 3, "reinvestment fixture begins with three native mature crops")
    sc("agent_schedule_probe")
    day = b.state()["day"]
    tool("plan.submit", submission_id="harvest-for-investment", expected_revision=tool("plan.read")["revision"], tasks=[dict(id="harvest-for-investment", actor="player", tool="work.run", args=dict(goal="harvest", location="Farm", count=0), day=day, purpose="harvest then reopen actual investment capacity")])
    state = wait(lambda s: s["investment"]["Phase"] in ("done", "blocked") and s["investment"]["Reviews"] > 0, 300)
    diagnostics = b.request("GET", "/lab/together")["autoplay"]
    (out / "reinvestment.json").write_text(json.dumps(dict(before=before, after=state, diagnostics=diagnostics), ensure_ascii=False, indent=2))
    check(state["investment"]["Phase"] == "done" and state["investment"]["Error"] == "", "harvest reopens the finished investment and completes its native chain")
    check(state["crops"] > 0 and state["ripe"] == 0, "real seed purchase is followed by planting newly freed land")
    spent = before["snapshot"]["money"] - state["snapshot"]["money"]
    check(0 < spent <= 200 and state["snapshot"]["money"] >= 100, "native reinvestment spends only its approved cash budget")
    check(state["calls"] == 0, "harvest, quote, purchase, preparation and planting chain has zero model callbacks")
finally:
    sc("agent_pause")
    sc("agent_ui")
