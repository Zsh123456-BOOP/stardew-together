"""Grouped native custom-partner checks; explicit tools, no model or resource fixture."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path('work/custom-partner-native');out.mkdir(exist_ok=True);checks=[];actions=[]
def scenario(scenario_id,**args):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=scenario_id,**args))
def tool(tool_id,**args):return scenario('agent_tool',tool=tool_id,args=args)
def check(ok,label,details=None):
 checks.append(dict(passed=bool(ok),label=label,details=details));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
 if not ok:raise SystemExit(label)
def poll(r):
 end=time.monotonic()+180
 while r.get('status')=='running' and time.monotonic()<end:
  time.sleep(.3);r=tool('action.status',id=r['command_id'])
 actions.append(r);(out/'actions.json').write_text(json.dumps(actions,ensure_ascii=False,indent=2));return r
scenario('auto',enabled=0);scenario('agent_pause')
tool('companion.configure',enabled=True,name='小禾',appearance='Leah')
s=b.state();actors=s['actors'];check(len(actors)==1 and actors[0]['name']=='Together_Partner','unique custom NPC instead of recruiting a native villager')
actor=actors[0]['id'];tool('companion.configure',enabled=True);check(len(b.state()['actors'])==1,'repeated ensure does not duplicate identity')
player=tool('player.travel',location='Farm');npc=tool('work.run',actor_id=actor,goal='stone',location='Farm',count=3)
check(player['status']=='running' and npc['status']=='running','Farmer and custom partner execute independently')
r=poll(player);check(r['status']=='succeeded','Farmer native farmhouse exit',r)
r=poll(npc);check(r['status']=='succeeded' and r.get('gained',0)>=3,'partner produces actual stone',r)
s=b.state()['actors'][0];check(sum(v for k,v in s['cargo'].items() if k.startswith('(O)390:'))>=3,'real output belongs to custom pouch',s['cargo'])
check(s['labor']['used']>0 and s['labor']['remaining']<180,'labor charged persistently and exposed independently of Farmer stamina',s['labor'])
r=poll(tool('work.run',actor_id=actor,goal='water',location='Farm',count=0));check(r['status']=='succeeded','empty maintenance closes rather than waiting forever',r)
ledger=tool('farm.operating');check(ledger['partner']['Created'] and ledger['policy']['PlayerWaterLimit']==24,'unified operating state exposes custom identity and work capacity')
(out/'identity.json').write_text(json.dumps(dict(save=s.get('save_id'),actor=actor,cargo=s['cargo'],labor=s['labor']),ensure_ascii=False,indent=2))
scenario('agent_pause')
