"""Observe an already running AgentLab experiment; makes no gameplay decisions."""
from pathlib import Path
import json,time,sys
sys.path.insert(0,str(Path(__file__).resolve().parents[1]))
from agent.client import Bridge
b=Bridge();assert b.state()['player']['name']=='AgentLab'
folder=Path(sys.argv[1] if len(sys.argv)>1 else 'work/autoplay-observations');folder.mkdir(exist_ok=True)
seen=set();end=time.monotonic()+900
while time.monotonic()<end:
    s=b.request('GET','/lab/together')['autoplay']
    (folder/'latest.json').write_text(json.dumps(s,ensure_ascii=False,indent=2))
    with (folder/'events.jsonl').open('a') as f:
        for e in s['state']['Journal']:
            identity=(e['Kind'],e['Text'])
            if identity not in seen:
                seen.add(identity);f.write(json.dumps({'observed_at':time.time(),'run_id':s['state']['RunId'],**e},ensure_ascii=False)+'\n')
    print('day',s['snapshot']['day'],'time',s['snapshot']['time'],'state',s['state']['Status'],'decisions',s['state']['Decisions'],'verified',s['state']['VerifiedActions'],flush=True)
    if s['state']['Status']!='running':break
    time.sleep(5)
