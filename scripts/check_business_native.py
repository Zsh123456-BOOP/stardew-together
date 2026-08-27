"""One grouped AgentLab fixture suite. Never count fixture assets as earned progress."""
import json
import sys
import time
from pathlib import Path
sys.path.insert(0, str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge

b=Bridge(); assert b.state()['player']['name']=='AgentLab'
out=Path('work/business-native'); out.mkdir(exist_ok=True)
results=[]
def scenario(scenario_id, **args):
    return b.request('POST','/lab/together',{'session_id':b.session,'scenario':scenario_id,**args})
def tool(tool_id, **args):
    return scenario('agent_tool',tool=tool_id,args=args)
def check(value,label,detail=None):
    results.append({'pass':bool(value),'label':label,'detail':detail})
    (out/'results.json').write_text(json.dumps({'scope':'explicit supplied fixture, not natural gameplay','results':results},ensure_ascii=False,indent=2))
    print(('PASS ' if value else 'FAIL ')+label,flush=True)
def action(tool_id,**args):
    value=tool(tool_id,**args); deadline=time.monotonic()+150
    while value.get('status')=='running' and time.monotonic()<deadline:
        time.sleep(.2);value=tool('action.status',id=value['command_id'])
    if value.get('status')=='running':tool('action.cancel',id=value['command_id'])
    return value
scenario('agent_fixture');time.sleep(1);scenario('business_fixture');time.sleep(1)
for label,name,args in [
    ('keg actual fruit consumption and native timer','player.machine',dict(mode='load',machine='(BC)12',location='Farm',item='(O)398',count=1)),
    ('preserves actual vegetable consumption','player.machine',dict(mode='load',machine='(BC)15',location='Farm',item='(O)24',count=1)),
    ('animal product processing','player.machine',dict(mode='load',machine='(BC)24',location='Farm',item='(O)176',count=1)),
    ('tapper native installation preserves tree','player.tap_tree',dict(output='(O)725',location='Farm')),
    ('walk to seed shop, buy and close','player.procure',dict(location='SeedShop',shop='SeedShop',item='(O)472',count=2,recipe=False,max_unit_price=20,budget=40,keep_gold=100)),
    ('walk to ranch, select home and buy animal','player.acquire_animal',dict(type='White Chicken',name='BizCheck',location='AnimalShop',budget=800,keep_gold=100)),
]:
    try:
        result=action(name,**args);check(result.get('status')=='succeeded',label,result)
    except Exception as error:
        check(False,label,{'error':str(error)})
        try:tool('menu.close')
        except Exception:pass
try:
    before=b.request('GET','/lab/together')['autoplay']['snapshot']
    ledger=tool('farm.business_status')
    tool('farm.economy',location='Farm',budget=0,keep_gold=100,plots=8)
    after=b.request('GET','/lab/together')['autoplay']['snapshot']
    check(before['location']==after['location'] and before['tile']==after['tile'],'remote economy queries never move Farmer',{'before':before,'after':after})
    check('ledger' in ledger and 'options' in ledger,'business ledger and investment candidates serialize',ledger)
    scenario('agent_schedule_probe')
    tool('farm.business',enabled=True,expand=False,budget_per_day=0,keep_gold=100,max_animals=2,max_machines=3,feed_days=2)
    end=time.monotonic()+180; done=False
    while time.monotonic()<end:
        d=b.request('GET','/lab/together')['autoplay']
        done=any(t['state']=='succeeded' and t['spec']['id'].startswith('business-') for t in d['state']['Schedule']['Tasks'])
        if done:break
        time.sleep(1)
    check(done,'business policy independently submits and completes queued work',d['state']['Schedule']['Tasks'])
finally:
    scenario('agent_pause')
raise SystemExit(0 if all(r['pass'] for r in results) else 1)
