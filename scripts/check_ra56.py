"""Grouped AgentLab checks: real stock, native operations, no seeded game data."""
import argparse,json,os,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',default='AgentLab_449635135');p.add_argument('--mode',choices=['packing','faults','cleanup','pause','pickup','combined'],default='packing');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/RA56Mods';b=None;results=[]
def write(n,d):(out/n).write_text(json.dumps(d,ensure_ascii=False,indent=2))
def check(name,ok,data):
 results.append(dict(name=name,passed=bool(ok),data=data));write('checks.json',results)
 if not ok:raise RuntimeError(name)
 print(name,flush=True)
with (out/'game.log').open('w') as log:
 proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--mods-dir',str(mods),'--port','18771'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
 try:
  deadline=time.monotonic()+120;commanded=False
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('owned_game_exited')
   try:
    conf=json.loads((mods/'AgentBridge/config.json').read_text());local=out/'bridge-private.json';fd=os.open(local,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18771',token=conf['Token']),f)
    b=Bridge(local);h=b.request('GET','/health')
    if h['api_connected'] and not commanded:proc.stdin.write(('agent_new' if a.save=='new' else 'agent_load '+a.save)+'\n');proc.stdin.flush();commanded=True
    if h['ready']:break
   except (OSError,ValueError,RuntimeError):pass
   time.sleep(1)
  assert b.state()['player']['name']=='AgentLab'
  def sc(name,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=name,**kw))
  def tool(name,**kw):return sc('agent_tool',tool=name,args=kw)
  def wait(r,label,seconds=200):
   until=time.monotonic()+seconds
   while r['status']=='running' and time.monotonic()<until:time.sleep(.25);r=tool('action.status',id=r['command_id'])
   write(label+'.json',r);return r
  def submit(label,toolname='work.run',**args):
   plan=tool('plan.read');tool('plan.submit',submission_id=label,expected_revision=plan['revision'],tasks=[dict(id=label,actor='player',tool=toolname,args=args,purpose=label)])
  def taskwait(label,seconds=220):
   until=time.monotonic()+seconds
   while time.monotonic()<until:
    d=sc('preparation_read');write('latest.json',d)
    with (out/'samples.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),active=d['active'],summary=d['preparationSummary'],snapshot=d['autoplay']['snapshot']),ensure_ascii=False)+'\n')
    for t in d['autoplay']['schedule']['recent_results']:
     if t['id']==label:write(label+'.json',d);return t,d
    if d['autoplay']['state']['Status']!='running':raise RuntimeError('unexpected_pause:'+d['autoplay']['state']['Detail'])
    time.sleep(.3)
   raise RuntimeError('task_timeout:'+label)
  sc('agent_pause');tool('menu.close');sc('preparation_probe');write('initial.json',sc('preparation_read'))
  for name,args in [('player.build',dict(blueprint='Coop',budget=4000)),('player.order_donate',dict(order='probe',dropbox='probe'))]:
   d=sc('preparation_shape',tool=name,args=args);write('shape-'+name+'.json',d)
   check('declaration-'+name,d.get('error','').startswith('loadout_shape_unsupported:') and d['before']==d['after'],d.get('error'))
  if a.mode not in ('cleanup','pickup','combined'):
   r=wait(tool('work.run',goal='store',required_free_slots=2),'warehouse');check('native-warehouse-prerequisite',r['status']=='succeeded',r.get('error'))
  # Production storage creates/registers a real chest; approach it through native movement.
  d=sc('preparation_read')
  # Known storage work has just visited its adjacent work tile.
  if a.mode=='packing':
   packed=sc('preparation_near_full');write('near-full-before.json',packed);check('near-full-conserved',packed['conserved'] and packed['bag_occupied']==12 and packed['chest_occupied']>=33,packed)
   submit('near-full-water',goal='water',location='Farm',count=0,until=2200)
   t,d=taskwait('near-full-water');write('near-full-after.json',d)
   check('native-ordered-withdrawal',not d['active'] and d['autoplay']['state']['Status']=='running' and any(i['Id']=='(T)WateringCan' and i['Container']=='player' for s in d['stores'] for i in s['items']),t)
  elif a.mode in ('pickup','combined'):
   wait(tool('player.collect_home_gifts'),'gifts')
   sc('pickup_block_probe')
   submit('pickup-injected',goal='wood',count=50,include_trees=True,location='Farm',until=2000)
   t,d=taskwait('pickup-injected');check('pickup-failure-reaches-scheduler',t.get('error')=='pickup_unreachable',t)
   try:
    submit('same-pickup-retry',goal='wood',count=50,include_trees=True,location='Farm',until=2000)
    t,d=taskwait('same-pickup-retry');error=t.get('error','')
   except Exception as e:error=str(e)
   check('pickup-blind-retry-blocked',error.startswith('known_failure_conditions_unchanged:'),error)
   submit('other-work-after-pickup',goal='stone',count=1,location='Farm',until=2200)
   t,d=taskwait('other-work-after-pickup');check('pickup-block-allows-other-work',t['state']=='succeeded' and d['autoplay']['state']['Survival']['Mode']=='model',t)
  elif a.mode=='cleanup':
   r=wait(tool('player.collect_home_gifts'),'gifts');check('native-gifts',r['status']=='succeeded',r.get('error'))
   r=wait(tool('player.travel',location='Farm'),'farm');check('native-farm-travel',r['status']=='succeeded',r.get('error'))
   options=tool('farm.plan',seed='(O)472',count=15,priority='income');write('plant-layout.json',options)
   check('plant-layout-exists',bool(options['options']),options)
   plan_id=options['options'][0]['plan_id']
   tool('farm.cleanup',request_id='combo-clean',scopes=['general'],daily_limit=3,reserve_stamina=30,until=1800)
   until=time.monotonic()+20;cleanup_id=None
   while time.monotonic()<until:
    plan=tool('plan.read')
    candidate=next((t for t in plan['tasks'] if t['spec']['tool']=='work.run' and t['spec']['args'].get('cleanup_id')=='combo-clean'),None)
    if candidate:cleanup_id=candidate['spec']['id'];break
    time.sleep(.1)
   check('cleanup-enqueued',bool(cleanup_id),plan)
   tool('plan.submit',submission_id='plant-after-clean',expected_revision=plan['revision'],tasks=[dict(id='plant-after-clean',actor='player',tool='work.run',args=dict(goal='plant',plan_id=plan_id,location='Farm',until=2000),after=[cleanup_id],priority=80,purpose='先有限清理，保留播种照料体力')])
   write('committed-care-budget.json',tool('farm.maintenance'))
   clean,dc=taskwait(cleanup_id,300);check('cleanup-not-starved-by-dependent-plant',clean['state']=='succeeded',clean)
   planted,dp=taskwait('plant-after-clean',400);check('plant-after-cleanup',planted['state']=='succeeded',planted)
   tool('farm.cleanup',request_id='combo-clean',mode='pause')
   submit('wood-pickup-probe',goal='wood',count=50,include_trees=True,location='Farm',until=2000)
   wt,wd=taskwait('wood-pickup-probe',400);write('pickup-result.json',wd)
   if wt.get('error')=='pickup_unreachable':
    submit('same-pickup-retry',goal='wood',count=50,include_trees=True,location='Farm',until=2000)
    retry,rd=taskwait('same-pickup-retry');check('pickup-blind-retry-blocked',retry.get('error','').startswith('known_failure_conditions_unchanged:'),retry)
    submit('work-after-pickup-block',goal='stone',count=1,location='Farm',until=2200)
    recovery,dr=taskwait('work-after-pickup-block');check('pickup-block-allows-other-work',recovery['state']=='succeeded' and dr['autoplay']['state']['Survival']['Mode']=='model',recovery)
   else:check('pickup-failure-reproduced',False,wt)
  elif a.mode=='pause':
   write('pause-stock-setup.json',sc('preparation_near_full'))
   r=wait(tool('player.move',x=64,y=28),'away-from-storage');check('walk-before-pause',r['status']=='succeeded',r.get('error'))
   submit('pause-water',goal='water',location='Farm',count=0,until=2300)
   until=time.monotonic()+15
   while time.monotonic()<until:
    d=sc('preparation_read')
    if d['active']:break
    time.sleep(.05)
   check('preparation-active-before-pause',d['active'],d['preparationSummary']);write('before-pause.json',d)
   sc('agent_pause');paused=sc('preparation_read');write('paused.json',paused)
   check('pause-cancels-preparation',not paused['active'] and paused['autoplay']['state']['Status']=='paused',paused['preparationSummary'])
   tool('plan.cancel',ids=['pause-water']);sc('preparation_resume')
   submit('resume-water',goal='water',location='Farm',count=0,until=2300);t,d=taskwait('resume-water')
   check('resume-replans-real-stocks',not d['active'] and d['autoplay']['state']['Status']=='running' and any(i['Id']=='(T)WateringCan' and i['Container']=='player' for s in d['stores'] for i in s['items']),t)
  elif a.mode=='faults':
   write('fault-stock-setup.json',sc('preparation_near_full'))
   for n,code in enumerate(['loadout_stock_changed','loadout_storage_busy','loadout_native_capacity_changed']):
    sc('preparation_fault',code=code)
    submit('fault-'+str(n),goal='water',count=1,location='Farm',until=2300)
    t,d=taskwait('fault-'+str(n));receipt=json.loads(t['receipt'])
    check(code,t.get('error')==code and receipt.get('conserved') is True and d['autoplay']['state']['Status']=='running',t)
    # Different supported work demonstrates that the failed transfer does not own the actor.
    submit('recover-'+str(n),goal='stone',count=1,location='Farm',until=2300)
    t,d=taskwait('recover-'+str(n));check('other-work-after-'+code,t['state']=='succeeded',t)
  if a.mode=='combined':
   sc('agent_pause');sc('survival_start',goal='正常速度经营农场。小禾当前只陪伴，劳动只派给玩家。领取礼包，规划连片田块，播种浇水，按实际资源安排后续工作。使用高层work.run，一次任务持续做到目标数量；只有缺料或预计装不下产物才整备，不逐个存货。')
   until=time.monotonic()+300;first_decisions=None
   while time.monotonic()<until:
    diag=b.request('GET','/lab/together');state=diag['autoplay']['state'];write('performance-latest.json',diag)
    if state['Status']!='running':raise RuntimeError('model_smoke_stopped:'+state['Detail'])
    if state['Decisions']>=2 and not diag['autoplay']['pending']:break
    time.sleep(1)
   write('performance.json',diag)
   check('flash-and-performance-observed',state['Decisions']>=2,dict(decisions=state['Decisions'],model=diag['autoplay']['model'],last_model_ms=diag['autoplay']['last_model_ms'],performance=diag['performance']))
  write('final.json',sc('preparation_read'));write('result.json',dict(passed=True,checks=results,mode=a.mode))
 except Exception as e:
  write('result.json',dict(passed=False,error=str(e),checks=results,mode=a.mode));print(type(e).__name__,str(e),flush=True)
 finally:
  if b:
   try:sc('agent_pause')
   except Exception:pass
  if proc.poll() is None:
   proc.stdin.write('agent_quit\n');proc.stdin.flush()
   try:proc.wait(timeout=30)
   except subprocess.TimeoutExpired:print('owned_exit_pending',proc.pid,flush=True)
sys.exit(0 if (out/'result.json').exists() and json.loads((out/'result.json').read_text())['passed'] else 1)
