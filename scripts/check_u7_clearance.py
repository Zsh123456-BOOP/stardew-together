"""Grouped native farm navigation checks. A fresh native AgentLab; no inventory/map writes."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=18780);p.add_argument('--mods-dir',type=Path,default=ROOT/'work/U7VerifiedMods');p.add_argument('--social-checks',action='store_true');p.add_argument('--service-checks',action='store_true');p.add_argument('--handoff-checks',action='store_true');p.add_argument('--decision-facts-checks',action='store_true');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=a.mods_dir.resolve();checks=[];b=None;initial=None

def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
def check(n,ok,data=None):
 checks.append(dict(name=n,passed=bool(ok),data=data));write('checks.json',checks);print(n,ok,flush=True)
config=json.loads((ROOT/'work/U6BuildMods/Together/config.json').read_text());config.update(Autonomy=False,RecordModelTrace=True)
(mods/'Together/config.json').write_text(json.dumps(config,ensure_ascii=False,indent=2))
write('manifest.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest(),fresh_native_save=True,no_save=True,normal_time=True))
log=(out/'game.log').open('w');proc=subprocess.Popen([sys.executable,'scripts/launch.py','--keep-window','--companion','--lab','--mods-dir',str(mods),'--port',str(a.port)],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True,start_new_session=True)
try:
 deadline=time.monotonic()+120;commanded=False
 while time.monotonic()<deadline:
  if proc.poll() is not None:raise RuntimeError('owned_game_exited')
  try:
   c=json.loads((mods/'AgentBridge/config.json').read_text());local=out/'bridge-private.json';fd=os.open(local,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
   with os.fdopen(fd,'w') as f:json.dump(dict(url=f'http://127.0.0.1:{a.port}',token=c['Token']),f)
   b=Bridge(local);h=b.request('GET','/health')
   if h['api_connected'] and not commanded:proc.stdin.write('agent_new\n');proc.stdin.flush();commanded=True
   if h['ready']:break
  except (OSError,ValueError,RuntimeError):pass
  time.sleep(.5)
 initial=b.state();check('isolated-AgentLab',initial['player']['name']=='AgentLab');write('initial.json',initial)
 def sc(n,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=n,**kw))
 def tool(n,**kw):
  try:r=sc('agent_tool',tool=n,args=kw)
  except BridgeError as e:r=dict(status='failed',error=str(e))
  with (out/'calls.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),tool=n,args=kw,result=r),ensure_ascii=False)+'\n')
  return r
 def wait(r,label,seconds=180):
  until=time.monotonic()+seconds
  while r.get('status')=='running' and time.monotonic()<until:
   time.sleep(.4);r=tool('action.status',id=r['command_id'])
   if label.startswith('clear'):
    with (out/'route-samples.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),receipt=r),ensure_ascii=False)+'\n')
  write(label+'.json',r);return r
 sc('agent_pause');sc('preparation_probe');time.sleep(.5)
 if a.decision_facts_checks:
  facts=sc('decision_facts_probe');write('decision-facts-before.json',facts)
  check('weather-and-native-watering-count-visible',all(k in facts['snapshot']['weather'] for k in ['farm_raining','farm_watering']) and facts['farm_water']['dry_crops']==0,facts['farm_water'])
  before=b.state();r=wait(tool('work.run',goal='water'),'already-watered-before-departure')
  after=b.state();check('water-zero-targets-never-travels-or-transfers',r.get('status')=='succeeded' and r.get('completed')==0 and r.get('executed')==False and before['player']==after['player'],r)
  access=tool('services.read',npc='Sam');write('sam-access-before.json',access)
  check('private-room-detected-before-departure',access.get('reach',{}).get('reachable')==False and bool(access['reach']['doors']),access)
  # After-hours and access restrictions are separate facts, not one generic path error.
  check('npc-window-and-approach-are-separate',access.get('entry',{}).get('can_enter_now')==False,access)
  r=sc('decision_chain',turn=dict(plan='检查等待开门不会占住其他独立工作的决策',speech='',calls=[dict(tool='player.service',args=dict(location='SeedShop',service='shop',shop='SeedShop')),dict(tool='world.read',args={})]));write('future-window-chain.json',r)
  deadline=time.monotonic()+15
  while time.monotonic()<deadline:
   d=b.request('GET','/lab/together')['autoplay']
   if d.get('continuation') is None:break
   time.sleep(.25)
  write('future-window-released.json',d)
  waiting=[t for t in d['state']['Schedule']['Tasks'] if not t['Terminal']]
  check('future-window-releases-observation-barrier',d.get('continuation') is None and bool(waiting),d.get('continuation'))
  tool('plan.cancel',ids=[t['spec']['id'] for t in waiting])
 if a.service_checks:
  hours=tool('services.read',location='SeedShop');write('seedshop-hours-before.json',hours);check('native-opening-hours-visible-before-travel',hours.get('opens')==900 and hours.get('can_enter_now')==False,hours)
  before=b.state();r=tool('player.procure',location='Town',shop='SeedShop',item='(O)472',count=1,budget=20,max_unit_price=20,keep_gold=0)
  check('wrong-procure-map-rejected-before-movement',r.get('error','').startswith('parameter_service_location_mismatch') and b.state()['player']==before['player'],r)
  r=tool('player.procure',location='SeedShop',shop='SeedShop',item='(O)472',count=1,budget=20,max_unit_price=20,keep_gold=0)
  check('correct-shop-before-opening-is-window-not-path-failure',r.get('error')=='shop_closed' and b.state()['player']==before['player'],r)
 if a.handoff_checks:
  r=tool('plan.submit',submission_id='service-deadline-check',expected_revision=tool('plan.read')['revision'],tasks=[dict(id='missed-opening',actor='player',tool='player.service',args=dict(location='SeedShop',service='shop',shop='SeedShop'),deadline=800),dict(id='independent-after-window',actor='player',tool='player.travel',args=dict(location='Farm'),sequence_after=['missed-opening'])]);write('window-plan.json',r)
  deadline=time.monotonic()+90
  while time.monotonic()<deadline:
   d=b.request('GET','/lab/together')['autoplay'];rows={t['spec']['id']:t for t in d['state']['Schedule']['Tasks']}
   if rows.get('independent-after-window',{}).get('Terminal'):break
   time.sleep(.4)
  write('window-plan-result.json',d)
  check('unreachable-opening-is-blocked-before-execution',rows.get('missed-opening',{}).get('state')=='blocked' and rows['missed-opening'].get('command_id') is None,rows.get('missed-opening'))
  check('independent-work-after-blocked-window-runs',rows.get('independent-after-window',{}).get('state')=='succeeded',rows.get('independent-after-window'))
 r=wait(tool('player.travel',location='Farm'),'farm-entry');check('native-farm-entry',r.get('status')=='succeeded',r)
 # Search observed native clutter, not a fabricated obstacle fixture.
 found=[]
 for offset in range(0,144,12):
  probe=sc('clearance_probe',offset=offset);write('probe-'+str(offset)+'.json',probe)
  if probe.get('found'):found.append(probe);break
 check('native-map-has-beneficial-clearance-route',bool(found),found)
 if not found:raise RuntimeError('no_native_clearance_case')
 probe=found[0];end=probe['end'];before=b.state();write('before-clear.json',before)
 held=None
 if probe.get('ordinary_steps') is None:
  held=wait(tool('player.move',x=end[0],y=end[1],reserve_stamina=270),'reserved-energy-route')
  check('reserved-energy-not-spent-on-clearing',held.get('before',{}).get('stamina')==held.get('after',{}).get('stamina'),held)
 r=wait(tool('player.move',x=end[0],y=end[1]),'clear-native-route');after=b.state();write('after-clear.json',after)
 route_effects=r.get('effects',[])+(held.get('effects',[]) if held else []);clear=[e for e in route_effects if e.get('kind')=='native_route_clear'];selected=[e for e in route_effects if e.get('kind')=='clearance_route_selected']
 check('native-clear-and-original-destination',r.get('status')=='succeeded' and bool(clear),r)
 check('clearance-selection-and-native-impact-evidence',bool(selected) and all(e['before']!=e['after'] for e in clear),clear)
 check('maximum-three-cleared-obstacles',len({tuple(e['tile']) for e in clear})<=3,clear)
 if a.social_checks:
  tile=clear[0]['tile'];r=wait(tool('player.work',skill='clear',slot=3,tiles=[dict(x=tile[0],y=tile[1])],reserve_stamina=0),'already-cleared-target')
  check('gone-target-does-not-swing-or-invent-progress',r.get('status')=='succeeded' and r.get('completed')==0 and r.get('before',{}).get('stamina')==r.get('after',{}).get('stamina') and any(e.get('kind')=='work_target_already_clear' for e in r.get('effects',[])),r)
 drops=sc('loose_drop_read');write('after-clear-drops.json',drops)
 # Complete actual pickup and a following unrelated resource task in the same session.
 if drops:
  tiles=list({(int(d['position'][0]//64),int(d['position'][1]//64)) for d in drops})
  pickup=wait(tool('player.collect_drops',tiles=[dict(x=x,y=y) for x,y in tiles]),'post-clear-pickup')
  check('pickup-completes-or-explicitly-defers',pickup.get('status') in ('succeeded','partial'),pickup)
 r=wait(tool('work.run',goal='wood',count=4,reserve_stamina=0,until=2400),'following-resource-work')
 check('independent-resource-work-continues',r.get('status') in ('succeeded','partial') and (r.get('gained',0)>0 or r.get('completed',0)>0),r)
 # Reproduce the former felled-tree pickup blocker on the native standard farm.
 tree_map=tool('map.read',actor_id='player',x=70,y=24,radius=8);write('tree-map.json',tree_map)
 if any(c.get('terrain')=='Tree' and c['x']==70 and c['y']==23 for c in tree_map.get('cells',[])):
  before=b.state();write('tree-before.json',before)
  r=tool('player.work',skill='chop',slot=0,tiles=[dict(x=70,y=23)],reserve_stamina=0);until=time.monotonic()+240
  with (out/'tree-samples.jsonl').open('w') as f:
   while r.get('status')=='running' and time.monotonic()<until:
    time.sleep(.3);r=tool('action.status',id=r['command_id']);f.write(json.dumps(dict(utc=time.time(),receipt=r,drops=sc('loose_drop_read'),player=b.state()['player']),ensure_ascii=False)+'\n');f.flush()
  write('tree-receipt.json',r);after=b.state();write('tree-after.json',after);drops=sc('loose_drop_read');write('tree-drops-after.json',drops)
  gained=after['player']['inventory'].get('(O)388:0',0)-before['player']['inventory'].get('(O)388:0',0)
  check('felled-tree-native-pickup-complete',r.get('status')=='succeeded' and gained>0 and len(drops)==0,dict(receipt=r,wood_gained=gained,ground_remaining=len(drops)))
 else:check('former-tree-case-present',False,tree_map)
 r=wait(tool('work.run',goal='stone',count=5,location='Farm',reserve_stamina=0,until=2400),'independent-stone-after-tree')
 check('no-old-drop-block-on-independent-task',r.get('status')=='succeeded' and r.get('gained',0)>0,r)
 r=wait(tool('player.travel',location='FarmHouse'),'return-house');check('normal-route-after-clearing',r.get('status')=='succeeded',r)
 r=wait(tool('player.travel',location='Forest'),'cross-map-route');check('cross-map-clearance-state-ownership',r.get('status')=='succeeded',r)
 if a.social_checks:
  r=wait(tool('player.travel',location='Town'),'town-entry');check('ordinary-town-route-not-excavation',r.get('status')=='succeeded',r)
  progress=tool('progress.read');write('social-progress-before.json',progress)
  intros=progress.get('social',{}).get('introductions',[]);check('native-remaining-introduction-list-visible',bool(intros) and 'remaining_npcs' in intros[0].get('details',{}),progress)
  if not intros:raise RuntimeError('native_introduction_quest_missing')
  q=intros[0];qid=q['quest_id'];remaining=q['details']['remaining_npcs'];probe=sc('social_probe');write('social-probe.json',probe)
  candidates=[n for n in probe['npcs'] if n['location']=='Town' and n['name'] in remaining and not n['sleeping'] and not n['invisible'] and not n['monster']]
  deadline=time.monotonic()+180
  while not candidates and time.monotonic()<deadline:
   time.sleep(2);probe=sc('social_probe');candidates=[n for n in probe['npcs'] if n['location']=='Town' and n['name'] in remaining and not n['sleeping'] and not n['invisible'] and not n['monster']]
  write('social-probe-selected.json',probe)
  candidates.sort(key=lambda n:not n['moving'])
  check('native-town-introduction-target-present',bool(candidates),candidates)
  if not candidates:raise RuntimeError('no_real_town_social_case')
  name=candidates[0]['name'];r=tool('player.social',npc=name,mode='greet',quest_id=qid);deadline=time.monotonic()+180
  with (out/'social-route-samples.jsonl').open('w') as f:
   while r.get('status')=='running' and time.monotonic()<deadline:
    time.sleep(.4);r=tool('action.status',id=r['command_id']);f.write(json.dumps(dict(receipt=r,observation=sc('social_probe')),ensure_ascii=False)+'\n');f.flush()
  write('native-greet.json',r);after=tool('progress.read');write('social-progress-after.json',after)
  new_remaining=after.get('social',{}).get('introductions',[{}])[0].get('details',{}).get('remaining_npcs',[])
  check('native-greet-removes-only-real-task-target',r.get('status')=='succeeded' and r.get('completed')==1 and name not in new_remaining and len(new_remaining)==len(remaining)-1,r)
  for i in range(3):
   r=wait(tool('player.social',npc=name,mode='greet',quest_id=qid),'repeat-greet-'+str(i))
   check('known-introduction-is-idempotent-'+str(i),r.get('status')=='succeeded' and r.get('completed')==0 and r.get('disposition')=='already_satisfied' and r.get('stop_reason')=='introduction_target_already_met',r)
  r=wait(tool('player.social',npc=name,mode='talk'),'talked-today');check('talked-today-is-observed-without-action',r.get('status')=='succeeded' and r.get('completed')==0 and r.get('stop_reason')=='talked_today',r)
  stable=tool('progress.read');write('social-progress-stable.json',stable);check('repeats-do-not-change-native-progress',stable['social']==after['social'])
  r=wait(tool('player.travel',location='Farm'),'return-after-social');check('work-can-continue-after-social-observation',r.get('status')=='succeeded',r)
 if a.service_checks:
  deadline=time.monotonic()+240
  while b.request('GET','/lab/together')['autoplay']['snapshot']['time']<900 and time.monotonic()<deadline:time.sleep(2)
  before=b.state();r=wait(tool('player.procure',location='SeedShop',shop='SeedShop',item='(O)472',count=1,budget=20,max_unit_price=20,keep_gold=0),'open-shop-native-purchase');after=b.state();write('purchase-world-before.json',before);write('purchase-world-after.json',after)
  check('native-purchase-after-opening',r.get('status')=='succeeded' and after['player']['inventory'].get('(O)472:0',0)-before['player']['inventory'].get('(O)472:0',0)==1,r)
  tool('menu.close');r=wait(tool('player.travel',location='Town'),'town-for-penny')
  deadline=time.monotonic()+240
  while b.request('GET','/lab/together')['autoplay']['snapshot']['time']<1030 and time.monotonic()<deadline:time.sleep(2)
  write('penny-before.json',sc('social_probe'));progress=tool('progress.read');q=progress['social']['introductions'][0]
  r=wait(tool('player.social',npc='Penny',mode='greet',quest_id=q['quest_id']),'penny-at-tree')
  check('screenshot-penny-approach-native-interaction',r.get('status')=='succeeded' and any(e.get('kind')=='native_social_interaction' and e.get('native_radius_verified') for e in r.get('effects',[])),r)
  r=wait(tool('player.travel',location='Farm'),'return-after-penny');check('normal-travel-after-dynamic-npc',r.get('status')=='succeeded',r)
 if a.handoff_checks:
  r=wait(tool('player.service',location='Town',service='daily_quests'),'native-board-open');check('native-board-opened',r.get('status')=='succeeded',r)
  r=sc('decision_chain',turn=dict(plan='原生任务板后还有独立行程，读取不能被排队动作锁住',speech='',calls=[dict(tool='player.travel',args=dict(location='Farm')),dict(tool='quest_board.read',args={})]));time.sleep(.5)
  d=b.request('GET','/lab/together')['autoplay'];write('board-handoff.json',d)
  check('board-needs-model-even-with-queued-work',d.get('needs_menu_decision')==True and d['snapshot']['menu']=='Billboard' and any(not t['Terminal'] for t in d['state']['Schedule']['Tasks']))
  check('board-releases-blocked-observation-continuation',d.get('continuation') is None,d.get('decision_reasons'))
  write('native-board-observation.json',tool('quest_board.read'));tool('menu.close')
  deadline=time.monotonic()+120
  while time.monotonic()<deadline:
   d=b.request('GET','/lab/together')['autoplay']
   if not any(not t['Terminal'] for t in d['state']['Schedule']['Tasks']):break
   time.sleep(.4)
  write('board-after-close.json',d);check('queued-native-travel-continues-after-board',d['snapshot']['location']=='Farm' and d['snapshot']['menu'] is None and not any(not t['Terminal'] for t in d['state']['Schedule']['Tasks']))
 if a.decision_facts_checks:
  r=wait(tool('player.travel',location='Farm'),'farm-for-rain-check');check('farm-before-native-plant',r.get('status')=='succeeded',r)
  plan=tool('farm.plan',seed='(O)472',count=1);write('rain-crop-plan.json',plan)
  options=plan.get('options',[])
  if options:
   r=wait(tool('work.run',goal='plant',plan_id=options[0]['plan_id'],reserve_stamina=0,max_food=0),'native-rain-crop-plant',300)
   check('native-crop-created-before-rain',r.get('status')=='succeeded',r)
  else:check('native-crop-created-before-rain',False,plan)
  for night in range(2):
   r=wait(tool('player.sleep',reason='原生跨日组合验证，不作为自主经营成绩'),'rain-native-sleep-'+str(night),180)
   check('native-rain-transition-'+str(night),r.get('status')=='succeeded',r);sc('preparation_probe')
  facts=sc('decision_facts_probe');write('rain-day-facts.json',facts)
  check('native-rain-with-live-crop-already-wet',facts['snapshot']['weather']['farm_raining'] and facts['farm_water']['crop_count']>0 and facts['farm_water']['dry_crops']==0,facts)
  before=b.state();r=wait(tool('work.run',goal='water'),'rain-no-travel');after=b.state()
  check('rain-no-watering-no-return-trip',r.get('completed')==0 and r.get('executed')==False and before['player']==after['player'],r)
  hours=tool('services.read',location='SeedShop');check('weekly-closure-is-not-2600-opening',hours.get('closed_today')==True and hours.get('opens') is None and hours.get('recheck_at') is None,hours)
  qid=tool('progress.read')['social']['introductions'][0]['quest_id'];access=tool('services.read',npc='Penny');write('rain-penny-access.json',access)
  before=b.state();r=wait(tool('player.social',npc='Penny',mode='greet',quest_id=qid),'rain-private-room-preflight');after=b.state()
  check('private-npc-preflight-preserves-player-position',access.get('available_now')==False and before['player']['tile']==after['player']['tile'],r)
 write('final-world.json',b.state());write('final-drops.json',sc('loose_drop_read'))
except Exception as e:check('suite-exception',False,repr(e))
finally:
 if b:
  try:write('final.json',b.request('GET','/lab/together'));sc('agent_pause')
  except Exception:pass
 if initial:
  src=mods/'Together/logs'/str(initial['save_id'])
  if src.exists():shutil.copytree(src,out/'native-logs',dirs_exist_ok=True)
 passed=bool(checks) and all(x['passed'] for x in checks)
 write('result.json',dict(passed=passed,checks=checks,game_pid=proc.pid,window_retained=not passed))
 if passed and proc.poll() is None:proc.stdin.write('agent_quit\n');proc.stdin.flush()
 elif b:
  try:sc('agent_ui')
  except Exception:pass
sys.exit(0 if passed else 1)
