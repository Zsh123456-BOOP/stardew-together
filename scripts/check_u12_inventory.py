"""Grouped U12 native disposal, goal identity and capacity recovery checks; no fabricated stock."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--port',type=int,default=18794);p.add_argument('--mods-dir',type=Path,default=ROOT/'work/U12VerifiedMods');p.add_argument('--social-checks',action='store_true');p.add_argument('--service-checks',action='store_true');p.add_argument('--handoff-checks',action='store_true');p.add_argument('--decision-facts-checks',action='store_true');a=p.parse_args()
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

 probe=sc('inventory_contract_probe');write('probe-before.json',probe)
 check('shop-is-not-a-native-fishing-site',not probe['fish_shop_has_site'],probe['fish_shop_has_site'])
 nofarm=tool('farm.plan',seed='(O)472',count=2)
 check('off-farm-plan-exposes-travel-dependency',nofarm.get('status')=='blocked' and nofarm.get('prerequisites',[{}])[0].get('tool')=='player.travel',nofarm)
 r=wait(tool('player.travel',location='Farm'),'farm',240);check('native-travel',r.get('status')=='succeeded',r)
 r=wait(tool('work.run',goal='fiber',location='Farm',count=5,reserve_stamina=0),'gather-fiber',300);check('native-source-for-disposal',r.get('status')=='succeeded',r)
 def row(item,source='backpack'):
  c=tool('inventory.capacity');write('capacity-latest.json',c)
  rows=c['backpack'] if source=='backpack' else [v for box in c['storage'] for v in box['items']]
  return next(v for v in rows if v['item']==item and v['disposable']>0)
 fiber=row('(O)771');before=tool('inventory.capacity')['free_slots'];args=dict(fiber['action']['args']);args['count']=1;args['reason']='AgentLab部分销毁应不释放格子'
 r=wait(tool('player.discard',**args),'partial-discard');check('partial-native-trash-does-not-free-slot',r.get('status')=='succeeded' and tool('inventory.capacity')['free_slots']==before,r)
 r=tool('player.discard',**args);check('stale-stack-cannot-destroy-more',r.get('status')=='failed' and 'stock_changed' in r.get('error',''),r)
 fiber=row('(O)771');r=wait(tool('player.discard',**fiber['action']['args']),'full-discard');check('full-native-trash-frees-slot',r.get('status')=='succeeded' and tool('inventory.capacity')['free_slots']==before+1,r)
 c=tool('inventory.capacity');axe=next(v for v in c['backpack'] if v['item'].startswith('(T)'))
 r=tool('player.discard',source='backpack',slot=axe['slot'],item=axe['item'],quality=axe['quality'],count=1,expected_stack=axe['count'],reason='AgentLab工具保护检查')
 check('tools-rejected-without-disposal',r.get('status')=='failed' and r.get('error')=='discard_protected_item',r)
 g1=tool('goal.create',request_id='u12-chest',entity='craft:Chest',count=1,completion='placed',run=False)
 g2=tool('goal.create',request_id='u12-chest-retry',entity='craft:Chest',count=1,completion='placed',run=False)
 check('different-request-reuses-unfinished-goal',g1.get('Id') is not None and g1.get('Id')==g2.get('Id'),dict(first=g1,retry=g2))
 r=wait(tool('work.run',goal='wood',location='Farm',count=55,reserve_stamina=0),'gather-wood',500);check('native-chest-materials',r.get('status')=='succeeded',r)
 r=wait(tool('work.run',goal='fiber',location='Farm',count=12,reserve_stamina=0),'fiber-for-native-split',300)
 split=sc('inventory_split_probe');write('native-full-bag.json',split);check('full-bag-prepared-by-native-split-not-generated-stock',split['free_slots']==0,split)
 blocked=tool('goal.prepare',id=g1['Id']);write('blocked-goal.json',blocked)
 check('full-bag-goal-waits-without-new-material-demand',not blocked['tasks'] and bool(blocked['gaps']),blocked)
 retry=tool('goal.create',request_id='u12-chest-retry-again',entity='craft:Chest',count=1,completion='placed',run=False)
 check('full-bag-retry-still-same-goal',retry.get('Id')==g1['Id'],retry)
 r=wait(tool('player.craft',recipe='Chest',count=1,goal_id=g1['Id']),'blocked-native-craft',90)
 check('native-craft-capacity-refusal-before-consumption',r.get('status')=='failed' and r.get('error')=='capacity_no_free_slot',r)
 fiber=row('(O)771');r=wait(tool('player.discard',**fiber['action']['args']),'release-for-chest');check('native-disposal-releases-capacity-for-original-goal',r.get('status')=='succeeded',r)
 prep=tool('goal.prepare',id=g1['Id']);write('goal-batch.json',prep)
 check('one-goal-no-duplicate-material-demand',len([g for g in sc('inventory_contract_probe')['goals'] if g['Entity']=='craft:Chest'])==1,prep)
 wood=next(v for v in tool('inventory.capacity')['backpack'] if v['item']=='(O)388')
 check('committed-material-protected',wood['protected_count']>=50,wood)
 r=wait(tool('player.craft',recipe='Chest',count=1,goal_id=g1['Id']),'native-craft',180);check('native-chest-crafted',r.get('status')=='succeeded',r)
 r=wait(tool('player.place_facility',item='(BC)130',location='Farm',goal_id=g1['Id']),'native-place',300);check('native-chest-placed',r.get('status')=='succeeded',r)
 r=wait(tool('work.run',goal='fiber',location='Farm',count=5,reserve_stamina=0),'storage-material',240)
 r=wait(tool('work.run',goal='store',required_free_slots=0,until=2500),'native-store',300);check('native-storage-transfer',r.get('status')=='succeeded',r)
 stored=row('(O)771','storage');bag=tool('inventory.capacity')['free_slots'];args=dict(stored['action']['args']);args['count']=1;args['reason']='AgentLab箱子部分销毁'
 r=wait(tool('player.discard',**args),'storage-partial',240);check('storage-disposal-does-not-claim-bag-capacity',r.get('status')=='succeeded' and tool('inventory.capacity')['free_slots']==bag,r)
 stored=row('(O)771','storage');r=wait(tool('player.discard',**stored['action']['args']),'storage-full',240);check('storage-full-stack-native-trash',r.get('status')=='succeeded',r)
 final=sc('inventory_contract_probe');write('final-probe.json',final)
 check('discard-receipts-account-exact-native-destruction',all(e.get('verified') for fn in ['partial-discard.json','full-discard.json','storage-partial.json','storage-full.json'] for e in json.loads((out/fn).read_text()).get('effects',[]) if e.get('kind')=='native_discard'))
 sc('agent_pause');write('result.json',dict(passed=all(c['passed'] for c in checks),checks=checks))
except Exception as e:
 write('result.json',dict(passed=False,error=str(e),checks=checks));print(type(e).__name__,str(e),flush=True)
 if b:
  try:sc('agent_pause')
  except Exception:pass
finally:
 if initial:
  source=mods/'Together/logs'/str(initial['save_id'])
  if source.exists():shutil.copytree(source,out/'native-logs',dirs_exist_ok=True)
 write('process.json',dict(pid=proc.pid,alive=proc.poll() is None,window_preserved=True))
 proc.stdin.close();log.close()
