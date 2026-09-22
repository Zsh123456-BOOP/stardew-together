"""Run the supervisor stop rule against actual, unedited historical game failures."""
import argparse,json
from pathlib import Path
from root_failure_watch import RootFailureWatch
p=argparse.ArgumentParser();p.add_argument('--log',type=Path,required=True);p.add_argument('--run',required=True);p.add_argument('--out',type=Path,required=True);a=p.parse_args()
watch=RootFailureWatch();version=None;receipt=None;observed=[];stopped=None
for line_no,line in enumerate(a.log.open(),1):
 e=json.loads(line)
 if e.get('run')!=a.run:continue
 if e['kind']=='capacity_constraint_created':version=e['payload']['CapacityVersion']
 if e['kind']=='action_result':receipt=e['payload']
 if e['kind']!='task_finished' or e['payload'].get('error')!='capacity_all_candidates_infeasible':continue
 # Old schema: join the preceding native receipt and root-version event.
 # Store the provenance; never rewrite the source log or invent a failed action.
 normalized=dict(e,payload=dict(e['payload'],command_id=receipt['command_id'],capacity_version=version))
 stopped=watch.observe(normalized);observed.append(dict(source_line=line_no,native_event=e,joined_receipt=receipt['command_id'],joined_version=version,stop=stopped))
 assert watch.observe(normalized) is None,'same event must not be counted twice'
 if stopped:break
result=dict(passed=len(observed)==3 and stopped is not None and stopped['count']==3,mode='supervisor replay of three actual native failures, not a new live-game deadlock',source=str(a.log),run=a.run,observations=observed,stop=stopped)
a.out.write_text(json.dumps(result,ensure_ascii=False,indent=2));print(json.dumps({k:result[k] for k in ('passed','mode','stop')}));raise SystemExit(0 if result['passed'] else 1)
