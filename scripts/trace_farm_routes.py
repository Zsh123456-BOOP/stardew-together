"""Read-only tile trajectory and logistics observer for an existing native trial.
Never invokes tools, changes orders, moves characters, or resets game state.
"""
import argparse,json,time,sys
from pathlib import Path
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
p=argparse.ArgumentParser();p.add_argument('trial',type=Path);p.add_argument('--interval',type=float,default=.25);p.add_argument('--seconds',type=int,default=9000);a=p.parse_args()
if not .2<=a.interval<=5:raise SystemExit('interval must be 0.2..5 seconds')
base=json.loads((a.trial/'baseline.json').read_text());save=base['state']['save_id'];out=a.trial/'routes';out.mkdir(exist_ok=False)
b=Bridge();start=time.time();previous={};counts={};contexts={};context_at=0;last_storage=None;reason='observer_timeout'
(out/'metadata.json').write_text(json.dumps(dict(save_id=save,start_utc=start,interval_seconds=a.interval,scope='Actual sampled tile positions; recording begins now, earlier paths are not reconstructed. Gaps are not evidence of teleportation.',paths='paths.jsonl',logistics='storage.jsonl'),ensure_ascii=False,indent=2))
def append(name,row):
 with (out/name).open('a') as f:f.write(json.dumps(row,ensure_ascii=False,separators=(',',':'))+'\n')
try:
 while time.time()-start<a.seconds:
  tick=time.monotonic();now=time.time()
  if (a.trial/'result.json').exists():reason='trial_finished';break
  state=b.state()
  if state['save_id']!=save:reason='save_changed';break
  if now-context_at>=2:
   d=b.request('GET','/lab/together')['autoplay'];context_at=now;contexts={}
   for t in d['state']['Schedule']['Tasks']:
    if t['state']=='running':contexts.setdefault(t['spec']['actor'],[]).append(dict(task_id=t['spec']['id'],tool=t['spec']['tool'],goal=t['spec'].get('args',{}).get('goal'),purpose=t['spec']['purpose']))
  actors=[dict(id='player',tile=state['player']['tile'],location=state['location'],cargo=state['player']['inventory'])]
  actors += [dict(id=x['id'],tile=x['tile'],location=x['location'],cargo=x.get('cargo',{}),moving=x.get('moving'),labor=x.get('labor'),native_task=x.get('task')) for x in state['actors']]
  for actor in actors:
   aid=actor['id'];old=previous.get(aid);context=contexts.get(aid,[]);key=json.dumps(dict(actor=actor,context=context),sort_keys=True)
   if old is None or old['key']!=key or now-old['written']>=5:
    delta=None;event='first_observation'
    if old:
     if old['location']!=actor['location']:event='map_transition'
     else:
      delta=sum(abs(x-y) for x,y in zip(actor['tile'],old['tile']));event='move' if 0<delta<=2 else 'sample_gap' if delta>2 else 'stationary_or_state_change'
    append('paths.jsonl',dict(utc=now,day=state['day'],game_time=state['game_time'],actor=actor,context=context,context_observed_utc=context_at,event=event,observed_manhattan_delta=delta))
    previous[aid]=dict(key=key,written=now,tile=actor['tile'],location=actor['location']);counts[event]=counts.get(event,0)+1
   # Containers returned by /state belong to the observed player map.
  storage=dict(location=state['location'],containers=state.get('storage',[]));sk=json.dumps(storage,sort_keys=True)
  if sk!=last_storage:
   append('storage.jsonl',dict(utc=now,day=state['day'],game_time=state['game_time'],**storage));last_storage=sk
  time.sleep(max(0,a.interval-(time.monotonic()-tick)))
except Exception as e:reason=type(e).__name__+':'+str(e)
finally:
 (out/'result.json').write_text(json.dumps(dict(reason=reason,elapsed=time.time()-start,events=counts),ensure_ascii=False,indent=2))
print(json.dumps(dict(reason=reason,events=counts),ensure_ascii=False))
