"""Custom NPC maintenance, menu interruption, delivery and native sleep, fixture save only."""
import json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
out=Path('work/partner-recovery-v2');out.mkdir(exist_ok=True);checks=[];receipts=[]
def scenario(code,**args):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=code,**args))
def tool(code,**args):return scenario('agent_tool',tool=code,args=args)
def check(ok,label,detail=None):
 checks.append(dict(passed=bool(ok),label=label,detail=detail));(out/'checks.json').write_text(json.dumps(checks,ensure_ascii=False,indent=2));print(('PASS ' if ok else 'FAIL ')+label,flush=True)
 if not ok:raise SystemExit(label)
def poll(r,seconds=240):
 end=time.monotonic()+seconds
 while r['status']=='running' and time.monotonic()<end:
  time.sleep(.3);r=tool('action.status',id=r['command_id'])
 receipts.append(r);(out/'receipts.json').write_text(json.dumps(receipts,ensure_ascii=False,indent=2));return r
scenario('auto',enabled=0);scenario('agent_pause');scenario('agent_semantic_fixture');time.sleep(1);tool('companion.configure',enabled=True)
actor=next(a for a in b.state()['actors'] if a['name']=='Together_Partner')['id']
r=tool('work.run',actor_id=actor,goal='water',location='Farm',count=0);scenario('panel',tab=0);time.sleep(2)
r=tool('action.status',id=r['command_id']);check(r['status']=='running','native UI waits without losing companion farming')
scenario('close');r=poll(r);check(r['status']=='succeeded','companion resumes after UI closes');check(r['status']=='succeeded' and r['completed']>=2,'distant crops remain visible despite dense nearby rocks and protected planting areas',r)
r=poll(tool('work.run',actor_id=actor,goal='stone',location='Farm',count=3));check(r['status']=='succeeded','native material collection before delivery',r)
before=next(a for a in b.state()['actors'] if a['name']=='Together_Partner')['cargo']
r=poll(tool('work.run',actor_id=actor,goal='store',location='Farm',count=0));check(r['status']=='succeeded','custom pouch physically delivered to marked shared storage',r)
world=b.state();cargo=next(a for a in world['actors'] if a['name']=='Together_Partner')['cargo'];check(sum(cargo.values())<sum(before.values()),'delivery reduces actual carried cargo',dict(before=before,after=cargo))
day=world['day'];r=poll(tool('player.sleep'),seconds=120);check(r['status']=='succeeded' and b.state()['day']==day+1,'native sleep saves and reaches the next day',r)
tool('companion.configure',enabled=True);a=next(a for a in b.state()['actors'] if a['name']=='Together_Partner');check(a['labor']['used']==0,'next native day resets only the custom labor quota',a['labor'])
status=tool('agent.status');(out/'saved.json').write_text(json.dumps(dict(save_id=status['save_id'],actor=a['id'],cargo=a['cargo'],day=b.state()['day']),ensure_ascii=False,indent=2))
scenario('agent_pause')
