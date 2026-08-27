"""Native navigation + recruited companion labor. No fixtures or paid model calls.
Uses an existing AgentLab save, walks to Linus and issues explicit tool commands.
"""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path('work/navigation-companion-native');out.mkdir(exist_ok=True)
checks=[];actions=[]
def scenario(name,**args):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=name,**args))
def tool(name,**args):return scenario('agent_tool',tool=name,args=args)
def poll(r,seconds=180):
 end=time.monotonic()+seconds
 while r['status']=='running' and time.monotonic()<end:
  time.sleep(.25);r=tool('action.status',id=r['command_id'])
 actions.append(r);(out/'actions.json').write_text(json.dumps(actions,ensure_ascii=False,indent=2))
 return r
def check(ok,label,detail=None):
 checks.append(dict(passed=bool(ok),label=label,detail=detail));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
 if not ok:raise SystemExit(label)
scenario('auto',enabled=0);scenario('agent_pause');scenario('performance_reset')
r=poll(tool('player.travel',location='Town'));check(r['status']=='succeeded','native travel exits farmhouse and reaches Town',r.get('error'))
r=poll(tool('player.travel',location='Mountain'));check(r['status']=='succeeded','Town route reaches Mountain without checking every unrelated door',r.get('error'))
r=poll(tool('player.recruit_companion',npc='Linus'));check(r['status']=='succeeded','native approach recruits the actual Linus under current invitation policy',r.get('error'))
w=tool('world.read');mate=next(c for c in w['companions'] if c['name']=='Linus');actor=mate['id']
policy=json.loads((Path('work/CompanionMods/Together/config.json')).read_text())
(out/'run-policy.json').write_text(json.dumps(dict(source='explicit tools, no LLM',allow_trial_recruitment=policy.get('AllowTrialRecruitment'),clock_rate=b.request('GET','/lab/together')['autoplay']['clock_rate']),ensure_ascii=False,indent=2))
npc=tool('work.run',actor_id=actor,goal='forage',location='Forest',count=3)
player=tool('player.travel',location='Farm')
check(npc['status']=='running' and player['status']=='running','player and recruited companion have simultaneous independent actions')
player=poll(player);check(player['status']=='succeeded','player returns through native exits while companion travels to work')
player=tool('work.run',goal='wood',location='Farm',count=3,reserve_stamina=40)
player=poll(player);check(player['status']=='succeeded' and player.get('gained',0)>=3,'player gathers actual wood while companion works in Forest',player.get('error'))
npc=poll(npc);check(npc['status']=='succeeded' and npc.get('completed',0)>=3,'companion forage accepts actual wild crop harvest targets',npc.get('error'))
w=tool('world.read');mate=next(c for c in w['companions'] if c['id']==actor)
check(sum(mate.get('cargo',{}).values())>=npc['completed'],'every collected target has actual carried output',mate.get('cargo'))
receipts=[tool('action.status',id=e['command_id']) for e in npc['evidence'] if e.get('kind')=='labor']
(out/'companion-labor.json').write_text(json.dumps(receipts,ensure_ascii=False,indent=2))
check(any(r.get('skill')=='harvest' and sum(r.get('evidence',{}).get('resource_changes',{}).values())>0 for r in receipts),'native wild-crop harvest delivers its real output to the remote companion')
perf=b.request('GET','/lab/together')['performance'];(out/'performance.json').write_text(json.dumps(perf,ensure_ascii=False,indent=2))
check(perf['p95_ms']<16.667 and perf['max_ms']<1500,'Together update avoids repeated multi-second route/maintenance stalls',perf)
scenario('agent_pause')
