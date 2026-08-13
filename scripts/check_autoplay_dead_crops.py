"""Native scythe clearing and living-crop rejection; labelled AgentLab fixture."""
from pathlib import Path
import json,time,sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge,BridgeError
b=Bridge();assert b.state()['player']['name']=='AgentLab'
def scenario(name,**args):return b.request('POST','/lab/together',{'session_id':b.session,'scenario':name,**args})
def tool(name,**args):return scenario('agent_tool',tool=name,args=args)
scenario('agent_dead_crop_fixture');time.sleep(1)
def action(x):
    ready=time.monotonic()+10
    while True:
        try:r=tool('player.work',skill='clear_dead',slot=4,tiles=[{'x':n,'y':18} for n in x]);break
        except BridgeError as e:
            # This rejection occurs before an action is created; do not retry uncertain writes.
            if str(e)!='player_not_free_read_menu' or time.monotonic()>=ready:raise
            time.sleep(.2)
    end=time.monotonic()+40
    while r['status']=='running' and time.monotonic()<end:
        time.sleep(.15);r=tool('action.status',id=r['command_id'])
    return r
r=action([67,68]);assert r['status']=='succeeded',r
blocked=action([69]);assert blocked['status']=='failed' and blocked['error']=='refusing_to_clear_living_crop',blocked
Path('work/autoplay-dead-crop-checks.json').write_text(json.dumps({'dead_crop_result':r,'living_crop_result':blocked},ensure_ascii=False,indent=2))
print('PASS native scythe clears dead crops; living crops rejected',flush=True)
