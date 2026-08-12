"""Native gathering and early-sleep policy regression, explicit AgentLab fixtures only."""
from pathlib import Path
import json,sys,time
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,BridgeError
b=Bridge();assert b.state()['player']['name']=='AgentLab'
results=[]
def scenario(name,**args):return b.request('POST','/lab/together',{'session_id':b.session,'scenario':name,**args})
def tool(name,**args):return scenario('agent_tool',tool=name,args=args)
def check(ok,label,detail=None):
    results.append({'pass':bool(ok),'check':label,'detail':detail})
    Path('work/autoplay-resource-checks.json').write_text(json.dumps(results,ensure_ascii=False,indent=2))
    print(('PASS: ' if ok else 'FAIL: ')+label,flush=True);assert ok,label
def action(name,**args):
    r=tool(name,**args);end=time.monotonic()+60
    while r['status']=='running' and time.monotonic()<end:
        time.sleep(.1);r=tool('action.status',id=r['command_id'])
    check(r['status']=='succeeded',name,r);return r

def count(snapshot,item):return sum(i['item'].get('count',0) for i in snapshot['inventory']['items'] if i['item'].get('id')==item)
scenario('agent_resource_fixture');time.sleep(1.5)
scenario('agent_speed',rate=8)
check(b.request('GET','/lab/together')['autoplay']['clock_rate']==1,'legacy speed request remains native time')
try:
    scenario('agent_policy_probe')
    try:tool('player.sleep',reason='种完了',review='今天只想睡觉，不考虑继续采集资源或照料未完成作物。');blocked=False
    except BridgeError as e:blocked='useful_daylight_remaining' in str(e)
    check(blocked,'daytime sleep is rejected while useful work remains')
finally:scenario('agent_pause')
a=action('player.work',skill='clear',slot=0,tiles=[{'x':x,'y':y} for y in range(18,24) for x in (67,68)])
check(a['completed']==12 and count(a['after'],'(O)388')-count(a['before'],'(O)388')==12,'twelve twigs yield twelve native wood, with collection at every tile')
a=action('player.work',skill='clear',slot=3,tiles=[{'x':63,'y':18}])
check(a['completed']==1 and len(a['effects'])>=2,'multi-hit stone continues until actually broken')
a=action('player.work',skill='forage',tiles=[{'x':63,'y':17}])
check(count(a['after'],'(O)16')-count(a['before'],'(O)16')==1,'native forage reaches actual inventory')
r=tool('day.plan',priorities=['准备升级材料'],resources=[{'item':'(O)388','count':50,'purpose':'制作与升级'}])
check(r['resource_targets'][0]['missing']==max(0,50-r['resource_targets'][0]['owned']),'resource target is total required inventory, not repeated extra collection')
print('COMPLETED',len(results),'checks',flush=True)
