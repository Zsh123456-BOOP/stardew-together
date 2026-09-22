"""One isolated native session for the query/route/planting repair, not a trial."""
import argparse,json,os,subprocess,sys,time,urllib.request
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge

def main():
 p=argparse.ArgumentParser();p.add_argument('--mods-dir',type=Path,required=True);p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=18806);p.add_argument('--probe-only',action='store_true');p.add_argument('--contracts-only',action='store_true');p.add_argument('--zero-energy-return',action='store_true');a=p.parse_args();mods=a.mods_dir.resolve();out=a.output.resolve();out.mkdir(parents=True,exist_ok=False)
 def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
 cfg=mods/'Together/config.json';settings=json.loads(cfg.read_text()) if cfg.exists() else {};settings.update(RecordModelTrace=True,Autonomy=False,SinglePlayerAutoplay=True,ModelTokenBudgetPerDay=0,EnableMemoryReflections=False);cfg.write_text(json.dumps(settings,ensure_ascii=False,indent=2))
 log=(out/'game.log').open('w');proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--keep-window','--mods-dir',str(mods),'--port',str(a.port)],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True,start_new_session=True);write('process.json',dict(pid=proc.pid))
 b=None;new=False;end=time.monotonic()+120
 while time.monotonic()<end:
  try:
   c=json.loads((mods/'AgentBridge/config.json').read_text());private=out/'bridge-private.json';fd=os.open(private,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
   with os.fdopen(fd,'w') as f:json.dump(dict(url=f'http://127.0.0.1:{a.port}',token=c['Token']),f)
   b=Bridge(private);h=b.request('GET','/health')
   if h['api_connected'] and not new:proc.stdin.write('agent_new\n');proc.stdin.flush();new=True
   if h['ready']:break
  except (OSError,ValueError,RuntimeError):pass
  time.sleep(.5)
 if b is None:raise RuntimeError('bridge_missing')
 assert b.state()['player']['name']=='AgentLab'
 def sc(scenario,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=scenario,**kw))
 def qr(mode,**kw):return sc('query_repair',mode=mode,**kw)
 def tool(name,**kw):return sc('agent_tool',tool=name,args=kw)
 def task_id(t):return t.get('id',t.get('spec',{}).get('id'))
 if a.probe_only:
  qr('route_site');time.sleep(2);write('collision.json',qr('collision_probe'));proc.stdin.close();log.close();return
 def wait(r,name,seconds=150):
  end=time.monotonic()+seconds
  while r.get('status')=='running' and time.monotonic()<end:
   time.sleep(.5);r=tool('action.status',id=r['command_id'])
  write(name+'.json',r);return r
 checks=[]
 def run(name,fn):
  try:fn();checks.append(dict(name=name,passed=True));print('PASS '+name,flush=True)
  except Exception as e:checks.append(dict(name=name,passed=False,error=str(e)));print('FAIL '+name+': '+str(e),flush=True)
  write('checks.json',checks)
 def query():
  r=qr('queries');write('queries.json',r);assert 0<len(r['pending'])<=6
  assert len(qr('read')['queries'])==6
  qr('model_query');end=time.monotonic()+80
  while time.monotonic()<end:
   r=qr('model_result')
   if r['status']!='running':break
   time.sleep(1)
  write('query-model.json',r);assert r['status']=='succeeded'
  for batch in range(6):
   if all(q['Delivered'] for q in qr('read')['queries']):break
   qr('model_query');end=time.monotonic()+80
   while time.monotonic()<end:
    r=qr('model_result')
    if r['status']!='running':break
    time.sleep(1)
   write(f'query-model-{batch+2}.json',r);assert r['status']=='succeeded'
  assert all(q['Delivered'] for q in qr('read')['queries'])
 run('six-native-queries-delivered-to-real-model',query)
 def operating():
  r=sc('operating_loop_probe');write('operating-loop-probe.json',r);assert all(r['checks'].values()),r['checks']
 run('operating-loop-runtime-contracts',operating)
 if a.zero_energy_return:
  def zero_energy_return():
   write('zero-energy-setup.json',qr('zero_energy_return_setup'));time.sleep(2)
   r=wait(tool('player.travel',location='FarmHouse'),'zero-energy-native-return',150)
   assert r['status']=='succeeded',r
   assert r['after']['location']=='FarmHouse' and r['after']['stamina']==0,r
   assert any(e.get('kind')=='clearance_shortcut_rejected' and e.get('reason')=='energy_reserve_reached' for e in r['effects']),r
   assert not any(e.get('kind')=='native_route_clear' for e in r['effects']),r
  run('zero-energy-native-return-uses-existing-detour',zero_energy_return)
 if a.contracts_only:
  write('result.json',dict(passed=all(c['passed'] for c in checks),checks=checks,scope='targeted runtime contracts, not a continuous trial'))
  proc.stdin.write('agent_quit\n');proc.stdin.flush();proc.stdin.close();log.close()
  if not all(c['passed'] for c in checks):raise SystemExit(1)
  return
 def route():
  qr('route_site');time.sleep(2);r=qr('route');write('town-route.json',r)
  assert not r['Reachable'] and r['Reason']=='route_origin_inside_static_collision',r
  # A tile warp into the house is an invalid pose, not the old player's pixel
  # reproduction. Preserve its diagnosis; validate real walking from open road.
  sc('warp_player',location='Town',x=15,y=86);time.sleep(2)
  stale=qr('stale_route_probe');write('stale-route.json',stale);assert stale['ended'] and stale['position_unchanged'],stale
  r=qr('route');write('road-route.json',r)
  assert r['Reachable'],r
  r=wait(tool('player.travel',location='Farm'),'town-return',180);assert r['status']=='succeeded',r
 run('invalid-pose-rejected-stale-controller-safe-road-native-return',route)
 def empty_plan():
  sc('preparation_probe')
  call=dict(tool='farm.plan',args=dict(seed='(O)499',count=1))
  first=sc('decision_chain',turn=dict(plan='核验不存在的自有种子',calls=[call]));second=sc('decision_chain',turn=dict(plan='同条件空方案去重',calls=[call]))
  state=qr('read');rules=[r for r in state['memory']['recent_failure_rules'] if r['Reason']=='farm_plan_no_feasible_option']
  write('empty-plan-merged.json',dict(first=first,second=second,rules=rules));assert len(rules)==1 and rules[0]['Suppressed']==1,rules
 run('same-empty-plan-merged-before-repeat-work',empty_plan)
 def mixed():
  qr('seed_setup');time.sleep(2);r=tool('farm.plan',seed='(O)770',count=3);write('mixed-plan.json',r);assert r['options'],r
  plan=r['options'][0]['plan_id'];sc('preparation_probe')
  r=sc('decision_chain',turn=dict(plan='种完这批混合种子；资料查询不替换劳动',calls=[dict(tool='work.run',args=dict(goal='plant',plan_id=plan,until=2400))]));assert not r['Error'],r
  before=qr('read');ids={task_id(t) for t in before['schedule']['tasks']}
  read=sc('decision_chain',turn=dict(plan='只查询，为后续准备',calls=[dict(tool='knowledge.search',args=dict(query='箱子'))]));assert not read['Error'],read
  after=qr('read');write('query-with-active-queue.json',dict(before=before,after=after,read=read));assert ids and ids<={task_id(t) for t in after['schedule']['tasks']+after['schedule']['recent_results']}
  end=time.monotonic()+240
  while time.monotonic()<end:
   d=qr('read');tasks=[t for t in d['schedule']['tasks']+d['schedule']['recent_results'] if task_id(t) in ids]
   if tasks and all(t['state'] in ('succeeded','failed','cancelled') for t in tasks):break
   time.sleep(.5)
  write('mixed-plant.json',tasks);assert all(t['state']=='succeeded' for t in tasks),tasks
  result=qr('read');write('mixed-native-state.json',result);p=next(p for p in result['plans'] if p['Id']==plan);assert len(p['verified_tiles'])==3,p
 run('mixed-seed-native-plant-water',mixed)
 def craft():
  qr('craft_setup');sc('preparation_resume')
  r=sc('decision_chain',turn=dict(plan='已知箱子配方，直接原生制作',calls=[dict(tool='player.craft',args=dict(recipe='Chest',count=1))]));write('craft-submission.json',r);assert not r['Error'],r
  end=time.monotonic()+90
  while time.monotonic()<end:
   d=qr('read');tasks=d['schedule']['tasks']+d['schedule']['recent_results'];write('craft-queue.json',d)
   if tasks and all(t['state'] in ('succeeded','failed','cancelled') for t in tasks):break
   time.sleep(.5)
  assert any(t.get('tool',t.get('spec',{}).get('tool'))=='player.craft' and t['state']=='succeeded' for t in tasks),tasks
 run('known-craft-no-preflight',craft)
 def shop():
  qr('shop_site');time.sleep(2);sc('preparation_resume');deadline=time.monotonic()+65
  while time.monotonic()<deadline and qr('read')['snapshot']['time']<930:time.sleep(1)
  r=wait(tool('player.service',location='SeedShop',shop='SeedShop',service='shop'),'native-shop',120);assert r['status']=='succeeded',r
  quote=tool('shop.read');write('shop-quote.json',quote);decision=quote['seed_decision'];spent_before=qr('read')['actual_spent']
  key=next(x.split('=',1)[1].strip().strip('"').strip("'") for x in (ROOT/'.env').read_text().splitlines() if x.startswith('DEEPSEEK_API_KEY='))
  request=dict(model=settings.get('Model','deepseek-flash'),messages=[dict(role='system',content='根据给定原生报价，选择一种当季能成熟的种子购买2包用于组合测试，说明理由。只输出JSON {"item":"真实QID","unit_price":报价,"reason":"理由"}。不猜价格，不选超预算物品。'),dict(role='user',content=json.dumps(decision,ensure_ascii=False))],response_format=dict(type='json_object'),thinking=dict(type='disabled'),max_tokens=500)
  write('selection-request.json',request);req=urllib.request.Request('https://api.deepseek.com/chat/completions',data=json.dumps(request).encode(),headers={'Content-Type':'application/json','Authorization':'Bearer '+key})
  with urllib.request.urlopen(req,timeout=60) as response:reply=json.load(response)
  write('selection-response.json',reply);choice=json.loads(reply['choices'][0]['message']['content']);r=wait(tool('player.buy',shop='SeedShop',item=choice['item'],count=2,max_unit_price=choice['unit_price'],budget=2*choice['unit_price'],keep_gold=0),'purchase',45);assert r['status']=='succeeded',r
  assert qr('read')['actual_spent']-spent_before==2*choice['unit_price'],'native purchase ledger mismatch'
  tool('menu.close') # player.buy may already close its own native menu.
  r=wait(tool('player.travel',location='Farm'),'purchase-return',180);assert r['status']=='succeeded',r
  plan=tool('farm.plan',seed=choice['item'],count=2);write('purchase-plan.json',plan);assert plan['options'],plan
  r=wait(tool('work.run',goal='plant',plan_id=plan['options'][0]['plan_id'],until=2400),'purchase-plant',180);assert r['status']=='succeeded',r
 run('native-quote-model-selection-buy-plant',shop)
 def night():
  sc('preparation_resume');before=qr('night_setup');write('night-before.json',before)
  r=sc('decision_chain',turn=dict(plan='可选出货暂缓，直接回家睡觉',calls=[dict(tool='player.sleep',args=dict(reason='今晚直接返家休息，鱼留到明天处理'))]));write('night-submit.json',r);assert not r['Error'],r
  end=time.monotonic()+150
  while time.monotonic()<end:
   d=b.request('GET','/lab/together')['autoplay'];write('night-after.json',d)
   if d['state']['SleepDays']>before['SleepDays']:break
   time.sleep(1)
  assert d['state']['SleepDays']==before['SleepDays']+1,d['state']['Status']
  assert not any(e['Kind']=='closing_shipment' for e in d['state']['Journal'])
  assert any(x.get('item',{}).get('id')=='(O)131' for x in qr('read')['inventory']['items'])
  diary=qr('read')['diary'];write('cross-day-diary.json',diary)
  assert diary['today_day']==diary['previous_day']+1
  assert sum(r['count'] for r in diary['previous'] if r['activity']=='种下')==5,diary
  assert sum(r['count'] for r in diary['previous'] if r['activity']=='浇水')==5,diary
 run('explicit-bedtime-no-shipping-and-native-save',night)
 write('final-state.json',qr('read'));write('result.json',dict(passed=all(c['passed'] for c in checks),checks=checks,scope='native combined fixtures, not seven day acceptance'))
 # Preserve failed scenes for inspection. Successful fixture session can be closed before fresh trial.
 if all(c['passed'] for c in checks):proc.stdin.write('agent_quit\n');proc.stdin.flush()
 proc.stdin.close();log.close()
 if not all(c['passed'] for c in checks):raise SystemExit(1)
if __name__=='__main__':main()
