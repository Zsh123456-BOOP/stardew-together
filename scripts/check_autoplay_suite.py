"""Run related native regressions as one batch and collect every result before repairs."""
from pathlib import Path
import json,subprocess,sys,argparse
root=Path(__file__).resolve().parents[1]
cases=[('transit','check_autoplay_transit.py'),('resources','check_autoplay_resources.py'),('scheduler','check_autoplay_scheduler.py'),('native','check_autoplay_live.py')]
parser=argparse.ArgumentParser();parser.add_argument('--cases',nargs='+',choices=[c[0] for c in cases]);args=parser.parse_args()
if args.cases:cases=[c for c in cases if c[0] in args.cases]
results=[]
for label,script in cases:
    log=root/'work'/f'autoplay-suite-{label}.log'
    with log.open('w') as f:
        try:code=subprocess.run([sys.executable,str(root/'scripts'/script)],cwd=root,stdout=f,stderr=subprocess.STDOUT,timeout=180).returncode
        except subprocess.TimeoutExpired:code=124
    results.append({'case':label,'exit_code':code,'log':str(log)})
    (root/'work/autoplay-suite-results.json').write_text(json.dumps(results,ensure_ascii=False,indent=2))
    print(label,'PASS' if code==0 else 'FAIL',flush=True)
sys.exit(0 if all(r['exit_code']==0 for r in results) else 1)
