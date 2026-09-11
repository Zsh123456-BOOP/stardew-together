"""One native AgentLab session for data-driven acquisition, crafting and placement."""
import argparse,json,os,subprocess,sys,time
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--flash',action='store_true');p.add_argument('--model-only',action='store_true');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/GenericMods';b=None;checks=[]
def write(name,value):(out/name).write_text(json.dumps(value,ensure_ascii=False,indent=2))
def check(name,ok,data=None):
 checks.append(dict(name=name,passed=bool(ok),data=data));write('checks.json',checks)
 if not ok:raise RuntimeError(name)
 print(name,flush=True)
with (out/'game.log').open('w') as log:
 proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--mods-dir',str(mods),'--port','18772'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
 try:
  deadline=time.monotonic()+120;commanded=False
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('owned_game_exited')
   try:
    conf=json.loads((mods/'AgentBridge/config.json').read_text());local=out/'bridge-private.json';fd=os.open(local,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18772',token=conf['Token']),f)
    b=Bridge(local);h=b.request('GET','/health')
    if h['api_connected'] and not commanded:proc.stdin.write('agent_new\n');proc.stdin.flush();commanded=True
    if h['ready']:break
   except (OSError,ValueError,RuntimeError):pass
   time.sleep(1)
  check('AgentLab-isolation',b.state()['player']['name']=='AgentLab')
  def sc(name,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=name,**kw))
  def tool(name,**kw):return sc('agent_tool',tool=name,args=kw)
  def wait_action(r,label,seconds=150):
   deadline=time.monotonic()+seconds
   while r['status']=='running' and time.monotonic()<deadline:time.sleep(.3);r=tool('action.status',id=r['command_id'])
   write(label+'.json',r);check(label,r['status']=='succeeded',r);return r
  def wait_goal(id,label,seconds=450):
   deadline=time.monotonic()+seconds
   while time.monotonic()<deadline:
    d=b.request('GET','/lab/together');write('latest.json',d)
    goal=next(g for g in d['shared_goals'] if g['Id']==id)
    with (out/'samples.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),goal=goal,snapshot=d['autoplay']['snapshot'],schedule=d['autoplay']['schedule']),ensure_ascii=False)+'\n')
    if goal['Status']=='fulfilled':write(label+'.json',d);check(label,True,goal);return d
    if goal['AutoBlockedReason']:raise RuntimeError('goal_blocked:'+goal['AutoBlockedReason'])
    if d['autoplay']['state']['Status']!='running':raise RuntimeError('unexpected_pause:'+d['autoplay']['state']['Detail'])
    time.sleep(1)
   raise RuntimeError('goal_timeout:'+id)
  sc('agent_pause');tool('menu.close');sc('preparation_probe')
  world=tool('world.read');write('world-before.json',world)
  check('single-player-companion-set-empty',world['companions']==[],world['companions'])
  rules=tool('goal.rules');write('rule-coverage.json',rules);check('unparsed-rules-visible',rules['coverage']['unparsed']>0)
  wait_action(tool('player.collect_home_gifts'),'native-initial-gifts')
  initial=sc('preparation_read');write('before-goals.json',initial)
  if not a.model_only:
   warehouse=sc('goal_infrastructure_probe');write('warehouse-goal.json',warehouse)
   check('storage-policy-uses-generic-goal',warehouse['Completion']=='placed' and warehouse['AutoExecute'])
   result=wait_goal(warehouse['Id'],'acquire-craft-place-storage')
   stores=sc('preparation_read');write('warehouse-registered.json',stores);check('native-warehouse-registered',len(stores['stores'])==1,stores['stores'])
  # Different native recipe, same planner. No ingredient/tool commands from test.
  all_rules=[];offset=0
  while True:
   page=tool('goal.rules',offset=offset);all_rules.extend(page['rules'])
   if page.get('next_offset') is None:break
   offset=page['next_offset']
  candidates=[r for r in all_rules if r['Kind']=='craft' and r['Known'] and r['Item'].startswith('(O)') and len(r['Inputs'])==1 and r['Inputs'][0]['Item']=='(O)388']
  check('another-native-recipe-found',bool(candidates))
  selected=min(candidates,key=lambda r:(r['Inputs'][0]['Count'],r['Id']));write('second-native-rule.json',selected)
  if not a.model_only:
   second=tool('goal.create',request_id='generic-second',entity=selected['Id'],count=2,completion='crafted',purpose='原生批次与制作计数检查')
   tool('goal.run',id=second['Id']);wait_goal(second['Id'],'different-recipe-native-count')
   same=tool('goal.create',request_id='generic-second',entity=selected['Id'],count=2,completion='crafted',purpose='原生批次与制作计数检查')
   check('same-request-id-does-not-replay',same['Id']==second['Id'] and same['Status']=='fulfilled')
  # Unsupported entire material shape must fail without moving even one item.
  shape=sc('preparation_shape',tool='player.build',args=dict(blueprint='Coop',budget=4000));write('unsupported-shape.json',shape)
  check('unsupported-shape-no-transfer',shape.get('error','').startswith('loadout_shape_unsupported:') and shape['before']==shape['after'])
  if a.flash or a.model_only:
   existing_ids={g['Id'] for g in b.request('GET','/lab/together')['shared_goals']}
   sc('agent_pause');sc('model_name',value='deepseek-flash');sc('survival_start',goal='短程工具验证：仅控制玩家。使用goal.create(run:true)一次创建并启动，根据已解锁原生配方 '+selected['Id']+' 制作2份。用独立request_id创建新目标，不重复之前目标，不招募伙伴，不提前睡觉。')
   deadline=time.monotonic()+180
   while time.monotonic()<deadline:
    d=b.request('GET','/lab/together');write('flash-latest.json',d)
    if d['autoplay']['state']['Status']!='running':raise RuntimeError('flash_stopped:'+d['autoplay']['state']['Detail'])
    if any(g['Id'] not in existing_ids and g['Status']=='fulfilled' for g in d['shared_goals']):break
    time.sleep(1)
   check('flash-goal-native-completion',any(g['Id'] not in existing_ids and g['Status']=='fulfilled' for g in d['shared_goals']),d['autoplay']['state']['Decisions'])
  write('final.json',b.request('GET','/lab/together'));write('result.json',dict(passed=True,checks=checks))
 except Exception as e:
  write('result.json',dict(passed=False,error=str(e),checks=checks));print(type(e).__name__,str(e),flush=True)
 finally:
  if b:
   try:sc('agent_pause')
   except Exception:pass
  if proc.poll() is None:
   proc.stdin.write('agent_quit\n');proc.stdin.flush()
   try:proc.wait(timeout=30)
   except subprocess.TimeoutExpired:print('owned_exit_pending',proc.pid,flush=True)
sys.exit(0 if json.loads((out/'result.json').read_text())['passed'] else 1)
