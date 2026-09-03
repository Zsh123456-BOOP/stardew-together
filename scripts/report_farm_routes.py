"""Summarize sampled native routes without changing the game or inventing missing paths."""
import argparse,json
from collections import Counter,defaultdict
from pathlib import Path
p=argparse.ArgumentParser();p.add_argument('trial',type=Path);a=p.parse_args();root=a.trial/'routes'
last={};counts=defaultdict(Counter);visits=defaultdict(Counter);previous_tile={}
for line in (root/'paths.jsonl').read_text().splitlines():
 try:r=json.loads(line)
 except json.JSONDecodeError:continue
 actor=r['actor'];aid=actor['id'];key=(aid,r['day']);c=counts[key];pos=(actor['location'],*actor['tile']);visits[key][pos]+=1
 old=last.get(aid);c['observations']+=1
 if old and old['day']==r['day']:
  elapsed=r['utc']-old['utc'];c['observed_seconds']+=max(0,elapsed)
  if r['event']=='move':
   c['measured_tile_steps']+=r['observed_manhattan_delta']
   if previous_tile.get(aid)==pos:c['immediate_backtracks']+=1
   previous_tile[aid]=(old['actor']['location'],*old['actor']['tile'])
  elif r['event']=='sample_gap':c['position_gaps']+=1
  elif r['event']=='map_transition':c['map_transitions']+=1;previous_tile.pop(aid,None)
  elif 0<=elapsed<=6:c['stationary_sample_seconds']+=elapsed
 last[aid]=r
report={'coverage':json.loads((root/'metadata.json').read_text()),'actors':[{'actor':aid,'day':day,**dict(c),'most_observed_tiles':[{'location':loc,'tile':[x,y],'observations':n} for (loc,x,y),n in visits[(aid,day)].most_common(12)]} for (aid,day),c in counts.items()], 'limits':'Measured tile steps exclude gaps and map transitions. Stationary samples include tool animations, menus, waits and rest, not necessarily idle. Frequent tiles are observations, not exact dwell time. Do not compare partial F coverage with full later days as equal workloads.'}
(root/'summary.json').write_text(json.dumps(report,ensure_ascii=False,indent=2));print(json.dumps(report,ensure_ascii=False,indent=2))
