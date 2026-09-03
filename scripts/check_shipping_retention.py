"""Native shipping/storage/closing regression; isolated AgentLab fixture, not gameplay evidence."""
import json, sys, time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge, BridgeError
b = Bridge()
assert b.state()['player']['name'] == 'AgentLab'
out = Path(sys.argv[1] if len(sys.argv) > 1 else 'work/shipping-retention-native')
out.mkdir(parents=True, exist_ok=True)
checks = []
def sc(name, **kw):
    return b.request('POST', '/lab/together', dict(session_id=b.session, scenario=name, **kw))
def tool(name, **kw): return sc('agent_tool', tool=name, args=kw)
def check(ok, label):
    checks.append(dict(passed=bool(ok), label=label))
    (out/'checks.json').write_text(json.dumps(checks, ensure_ascii=False, indent=2))
    print(('PASS ' if ok else 'FAIL ') + label, flush=True)
    assert ok, label
def action(name, **kw):
    r = tool(name, **kw); end = time.monotonic()+120
    while r['status'] == 'running' and time.monotonic() < end:
        time.sleep(.25); r = tool('action.status', id=r['command_id'])
    return r
def submit(tasks, name):
    return tool('plan.submit', submission_id=name, expected_revision=tool('plan.read')['revision'], tasks=tasks)
try:
    sc('shipping_retention_fixture'); time.sleep(2)
    before = sc('shipping_retention_read')
    uses = tool('farm.production', action='uses', item='(O)92')
    check(uses['total'] > 0 and len(uses['recipes']) <= 12 and any(r['Id'] == 'craft:Torch' for r in uses['recipes']), 'material-use index reads native recipes with bounded pages and unlock states')
    decision = tool('farm.production', action='make', recipe='craft:Torch', count=1, request_id='retention-torch', reason='验证承诺只保护真实配方需要的数量')
    ledger = tool('farm.production')
    sap = next(m for m in ledger['materials'] if m['item'] == '(O)92')['allocation']
    check(sap['Committed'] == 2 and sap['Free'] == 10, 'approved recipe reserves two actual sap, leaving unrelated ten free')
    sc('shipping_retention_fixture'); time.sleep(2)
    try:
        rejected = tool('player.ship_items', items=[dict(item='(O)92', count=10)])
    except BridgeError as e: rejected = dict(status='failed', error=str(e))
    (out/'protected-sale.json').write_text(json.dumps(rejected, ensure_ascii=False, indent=2))
    check('sale_requires_surplus_decision' in str(rejected), 'tree sap sale is rejected before travel')
    after = sc('shipping_retention_read')
    check(after['bag'] == before['bag'] and after['bin'] == {} and after['tile'] == before['tile'], 'rejected shipment changes neither inventory, bin nor position')
    r = action('player.craft', recipe='Torch', count=1)
    after = sc('shipping_retention_read')
    check(r['status'] == 'succeeded' and after['bag']['(O)92'] == 10 and after['bag']['(O)388'] == 4, 'retained sap remains usable for native crafting')
    # Fresh fixture removes the crafted torch so all available slots are explicit.
    sc('shipping_retention_fixture'); time.sleep(2)
    before = sc('shipping_retention_read'); sc('agent_schedule_probe'); tool('day.routine', enabled=False)
    submit([dict(id='empty-store', tool='work.run', args=dict(goal='store')),
            dict(id='early-sale', tool='player.ship_items', args=dict(items=[dict(item='(O)24', count=1)]))], 'early-logistics')
    time.sleep(2)
    d = b.request('GET', '/lab/together')['autoplay']; after = sc('shipping_retention_read')
    (out/'early.json').write_text(json.dumps(dict(before=before, after=after, state=d['state']), ensure_ascii=False, indent=2))
    check(after['bag'] == before['bag'] and after['bin'] == {} and after['tile'] == before['tile'], 'unneeded storage and early tiny sale cause no trip or item movement')
    check(d['state']['VerifiedActions'] == 0, 'deferred and empty logistics are not counted as actual progress')
    day = tool('day.read'); start_money = after['money']
    submit([dict(id='closing-sleep', tool='player.sleep', args=dict(reason='农务已完成，体力不足，收工', review='核对了农务和体力；今天没有必须继续的劳动，集中出货并保存，明日再继续。'))], 'close-day')
    end = time.monotonic()+240
    while time.monotonic() < end:
        d = b.request('GET', '/lab/together')['autoplay']
        task = next(t for t in d['state']['Schedule']['Tasks'] if t['spec']['id'] == 'closing-sleep')
        if task['state'] in ('succeeded', 'failed', 'blocked'): break
        time.sleep(.3)
    after = sc('shipping_retention_read')
    (out/'closing.json').write_text(json.dumps(dict(before=before, after=after, state=d['state']), ensure_ascii=False, indent=2))
    shipments = [t for t in d['state']['Schedule']['Tasks'] if t['spec']['id'].startswith('closing-') and t['spec']['tool'] == 'player.ship_items']
    check(len(shipments) == 1 and shipments[0]['state'] == 'succeeded', 'two crop products are delivered in one native manifest')
    check(after['bag'].get('(O)92') == 12 and after['bag'].get('(O)771') == 25, 'automatic shipping retains all tree sap and fiber')
    check(task['state'] == 'succeeded' and after['day'] == day['day']+1 and d['state']['SleepDays'] == 1, 'closing shipment continues into native sleep, save and next day')
    check(after['money']-start_money == 8*35+8*80, 'only actual shipped parsnips and potatoes produce native next-day income')
finally:
    sc('agent_pause'); sc('agent_ui')
