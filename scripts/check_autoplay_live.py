"""Isolated AgentLab checks for native player actions; never accesses another save."""
from pathlib import Path
import json
import time
import sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge, BridgeError
b=Bridge();state=b.state()
assert state['player']['name']=='AgentLab'
results=[]
def scenario(name,**kw):
    return b.request('POST','/lab/together',{'session_id':b.session,'scenario':name,**kw})
def tool(name,**args):
    return scenario('agent_tool',tool=name,args=args)
def check(ok,label,detail=None):
    results.append({'pass':bool(ok),'check':label,'detail':detail});print(('PASS: ' if ok else 'FAIL: ')+label,flush=True)
    Path('work/autoplay-native-checks.json').write_text(json.dumps(results,ensure_ascii=False,indent=2))
    assert ok,label

def action(name,**args):
    r=tool(name,**args);deadline=time.monotonic()+70
    while r['status']=='running' and time.monotonic()<deadline:
        time.sleep(.15);r=tool('action.status',id=r['command_id'])
    check(r['status']=='succeeded',name,r)
    return r
scenario('agent_fixture')
time.sleep(1.5)
slots=tool('inventory.read')['items']
check(slots[5]['item']['count']==6,'fixture supplies labelled separately from earned resources')
a=action('player.work',skill='till',slot=1,tiles=[{'x':61,'y':18},{'x':62,'y':18}]);check(a['completed']==2,'batch hoe preserves both native effects')
a=action('player.work',skill='plant',slot=5,tiles=[{'x':61,'y':18},{'x':62,'y':18}]);check(a['completed']==2,'native seeds consumed and plants created')
a=action('player.work',skill='water',slot=2,tiles=[{'x':61,'y':18},{'x':62,'y':18}]);check(a['completed']==2,'batch watering verifies each tile after animation')
check(tool('inventory.read')['items'][5]['item']['count']==4,'two seeds consumed exactly once')
craft_baseline=tool('progress.read')['crafting'].get('Chest',0)
m=tool('menu.open',page='crafting')
recipes=[c for c in m['choices'] if c['Label'].startswith('craft:Chest ')]
check(len(recipes)==1,'native unlocked chest recipe exposed')
r=tool('menu.choose',token=m['token'],id=recipes[0]['Id'])
m=r['menu'];check('(BC)130' in m['held'],'native crafting result is held on cursor')
try:tool('menu.choose',token='stale-token',id=recipes[0]['Id']);rejected=False
except BridgeError:rejected=True
check(rejected,'stale menu token cannot replay a purchase or craft')
try:tool('menu.close');rejected=False
except BridgeError:rejected=True
check(rejected,'held crafted item cannot be lost by closing menu')
empty=next(c for c in m['choices'] if c['Label'].startswith('inventory:') and '"empty":true' in c['Label'])
r=tool('menu.choose',token=m['token'],id=empty['Id']);tool('menu.close')
check(any(i['item'].get('id')=='(BC)130' for i in tool('inventory.read')['items']),'crafted chest transferred into actual inventory')
p=tool('progress.read');check(p['crafting'].get('Chest')==craft_baseline+1,'native crafting count incremented, not an invented goal flag')
# Use one untouched seed to verify real shipment and native income; planted crops remain.
mapdata=tool('map.read',x=71,y=14,radius=5)
shipping=next(x for x in mapdata['buildings'] if x['type']=='Shipping Bin')
a=action('player.move',x=shipping['x'],y=shipping['y']+shipping['height'])
gold=a['after']['money'];day=a['after']['day']
a=action('player.ship',slot=5);check(a['after']['money']==gold,'shipping does not award money before native overnight settlement')
a=action('player.sleep');check(a['after']['day']==day+1 and a['after']['money']>gold,'native save, date advance and shipping income all verified')
print('COMPLETED',len(results),'checks',flush=True)
