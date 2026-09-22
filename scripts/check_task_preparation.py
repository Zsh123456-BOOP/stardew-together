"""Native AgentLab preparation chain. No inventory/time/progress fixtures."""
import argparse,json,os,subprocess,sys,time,shutil
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--ui-hold',action='store_true');p.add_argument('--save');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/CapacityMods';b=None;passed=False
write=lambda n,d:(out/n).write_text(json.dumps(d,ensure_ascii=False,indent=2))
with (out/'game.log').open('w') as log:
 proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--mods-dir',str(mods),'--port','18769'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
 try:
  deadline=time.monotonic()+120;commanded=False
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('owned_game_exited')
   try:
    config=json.loads((mods/'AgentBridge/config.json').read_text());private=out/'bridge-private.json';fd=os.open(private,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18769',token=config['Token']),f)
    b=Bridge(private);h=b.request('GET','/health')
    if h['api_connected'] and not commanded:proc.stdin.write(('agent_load '+a.save if a.save else 'agent_new')+'\n');proc.stdin.flush();commanded=True
    if h['ready']:break
   except (OSError,ValueError,RuntimeError):pass
   time.sleep(1)
  assert b.state()['player']['name']=='AgentLab'
  def sc(name,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=name,**kw))
  def tool(name,**kw):return sc('agent_tool',tool=name,args=kw)
  def wait_action(r,label,seconds=180):
   until=time.monotonic()+seconds
   while r['status']=='running' and time.monotonic()<until:
    time.sleep(.25);r=tool('action.status',id=r['command_id'])
   write(label+'.json',r)
   assert r['status']=='succeeded',label+':'+str(r.get('error'))
   return r
  sc('agent_pause');tool('menu.close');sc('preparation_probe')
  if not a.save:
   wait_action(tool('player.collect_home_gifts'),'gifts')
  initial=sc('preparation_read');write('initial.json',initial)
  if not a.save:
   r=tool('work.run',goal='store',required_free_slots=12);until=time.monotonic()+20
   while r['status']=='running' and time.monotonic()<until:time.sleep(.2);r=tool('action.status',id=r['command_id'])
   write('no_warehouse.json',r);assert r.get('error')=='capacity_all_candidates_infeasible','real absent warehouse must report candidates'
   for n in range(3):
    guard=sc('capacity_constraints',mode='guard',tool='work.run',args={'goal':'store','required_free_slots':12})
    write(f'guard-{n}.json',guard);assert guard['blocked'],'same state cannot re-dispatch failed store'
  def queue(label,goal,**kw):
   plan=tool('plan.read');spec={'id':label,'actor':'player','tool':'work.run','args':dict(goal=goal,**kw),'purpose':label}
   tool('plan.submit',submission_id=label,expected_revision=plan['revision'],tasks=[spec]);until=time.monotonic()+240
   while time.monotonic()<until:
    d=sc('preparation_read');write('latest.json',d)
    with (out/'samples.jsonl').open('a') as f:f.write(json.dumps({'utc':time.time(),'active':d['active'],'summary':d['preparationSummary'],'snapshot':d['autoplay']['snapshot']},ensure_ascii=False)+'\n')
    tasks=d['autoplay']['schedule']['recent_results'];t=next((t for t in tasks if t['id']==label),None)
    if t:
     write(label+'.json',d);assert t['state']=='succeeded',label+':'+str(t.get('error'));return d
    time.sleep(.5)
   raise RuntimeError('task_timeout:'+label)
  if not a.save:queue('native-wood','wood',count=50,include_trees=True,location='Farm',until=2000)
  wait_action(tool('work.run',goal='store',required_free_slots=2),'native-warehouse')
  before=sc('preparation_read');write('before-kit.json',before)
  banked=queue('bank-before-forage','forage',location='Farm',count=0,until=2100)
  assert any(i['Id']=='(T)Hoe' and i['Container']=='chest' for s in banked['stores'] for i in s['items']),'unrelated real tool must reach warehouse'
  if a.save:
   until=time.monotonic()+200
   while sc('preparation_read')['autoplay']['snapshot']['time']<900 and time.monotonic()<until:time.sleep(2)
   purchase=tool('player.procure',location='SeedShop',service='shop',shop='SeedShop',item='(O)472',count=3,budget=60,max_unit_price=20,keep_gold=0)
   until=time.monotonic()+180
   while purchase['status']=='running' and time.monotonic()<until:
    time.sleep(.25);purchase=tool('action.status',id=purchase['command_id'])
   if purchase.get('error')=='event_interrupted_read_menu':
    write('purchase-event-interruption.json',purchase)
    until=time.monotonic()+180
    while time.monotonic()<until:
     snap=sc('preparation_read')['autoplay']['snapshot']
     if not snap['event_up'] and snap['can_move'] and snap['menu'] is None:break
     time.sleep(.5)
    assert not snap['event_up'] and snap['can_move'] and snap['menu'] is None,'native event and player movement must recover before resuming purchase'
    purchase=tool('player.procure',location='SeedShop',service='shop',shop='SeedShop',item='(O)472',count=3,budget=60,max_unit_price=20,keep_gold=0)
   wait_action(purchase,'native-seed-purchase')
   wait_action(tool('player.travel',location='Farm'),'return-to-farm')
  else:assert any(i['Id']=='(O)472' and i['Container']=='chest' for s in banked['stores'] for i in s['items']),'unused seeds must reach warehouse'
  options=tool('farm.plan',seed='(O)472',count=3,priority='income');write('plant-plan.json',options)
  assert options['options'],'stored seeds must remain available for planning'
  field=options['options'][0]
  planted=queue('prepare-and-plant','plant',plan_id=field['plan_id'],until=2200)
  assert sum(i['count'] for i in planted['body']['items'] if i['id']=='(T)Hoe')==1,'native tool retrieved for planting'
  assert planted['autoplay']['state']['Quality']['PlantedTiles'],'planting must have native crop evidence'
  write('final.json',planted);sc('agent_pause');sc('overlay_capture');time.sleep(1)
  capture=mods/'Together/screenshots/panel.png'
  if capture.exists():shutil.copyfile(capture,out/'overlay-top.png')
  passed=True;print('NATIVE CHAIN PASSED',flush=True)
  if a.ui_hold:
   print('UI READY; write ui-done file after real mouse-wheel checks',flush=True);until=time.monotonic()+240
   while time.monotonic()<until and not (out/'ui-done').exists():time.sleep(1)
 except Exception as e:
  write('failure.json',dict(type=type(e).__name__,error=str(e)));print('STOP',type(e).__name__,str(e),flush=True)
  if b:
   try:write('failure-state.json',sc('preparation_read'));sc('overlay_capture');time.sleep(1);shutil.copyfile(mods/'Together/screenshots/panel.png',out/'failure.png')
   except Exception:pass
 finally:
  write('result.json',dict(passed=passed,no_inventory_or_progress_fixtures=True))
  if b:
   try:sc('agent_pause')
   except Exception:pass
  if proc.poll() is None:
   proc.stdin.write('agent_quit\n');proc.stdin.flush()
   try:proc.wait(timeout=30)
   except subprocess.TimeoutExpired:print('exit pending',proc.pid)
sys.exit(0 if passed else 1)
