"""Fourth-round native seam suite in one AgentLab process; no resource/progress seeding."""
import argparse,json,os,subprocess,sys,time,shutil,hashlib
from pathlib import Path
ROOT=Path(__file__).resolve().parents[1];sys.path.insert(0,str(ROOT))
from agent.client import Bridge,BridgeError
p=argparse.ArgumentParser();p.add_argument('--output',type=Path,required=True);p.add_argument('--save',default='AgentLab_449657228');a=p.parse_args()
out=a.output.resolve();out.mkdir(parents=True,exist_ok=False);mods=ROOT/'work/Round4Mods';b=None;checks=[]
def write(n,v):(out/n).write_text(json.dumps(v,ensure_ascii=False,indent=2))
def check(n,ok,data=None):
 checks.append(dict(name=n,passed=bool(ok),data=data));write('checks.json',checks);print(n,ok,flush=True)
 if not ok:raise RuntimeError(n)
write('manifest.json',dict(commit=subprocess.check_output(['git','rev-parse','HEAD'],cwd=ROOT,text=True).strip(),dll_sha256=hashlib.sha256((mods/'Together/Together.dll').read_bytes()).hexdigest(),save=a.save,normal_time=True))
with (out/'game.log').open('w') as log:
 proc=subprocess.Popen([sys.executable,'scripts/launch.py','--companion','--lab','--mods-dir',str(mods),'--port','18775'],cwd=ROOT,stdin=subprocess.PIPE,stdout=log,stderr=subprocess.STDOUT,text=True)
 try:
  deadline=time.monotonic()+120;commanded=False
  while time.monotonic()<deadline:
   if proc.poll() is not None:raise RuntimeError('owned_game_exited')
   try:
    c=json.loads((mods/'AgentBridge/config.json').read_text());local=out/'bridge-private.json';fd=os.open(local,os.O_CREAT|os.O_TRUNC|os.O_WRONLY,0o600)
    with os.fdopen(fd,'w') as f:json.dump(dict(url='http://127.0.0.1:18775',token=c['Token']),f)
    b=Bridge(local);h=b.request('GET','/health')
    if h['api_connected'] and not commanded:proc.stdin.write('agent_load '+a.save+'\n');proc.stdin.flush();commanded=True
    if h['ready']:break
   except (OSError,ValueError,RuntimeError):pass
   time.sleep(1)
  initial=b.state();check('AgentLab-isolated',initial['player']['name']=='AgentLab')
  def sc(n,**kw):return b.request('POST','/lab/together',dict(session_id=b.session,scenario=n,**kw))
  def tool(n,**kw):
   try:r=sc('agent_tool',tool=n,args=kw)
   except BridgeError as e:r=dict(status='failed',error=str(e))
   with (out/'calls.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),tool=n,args=kw,result=r),ensure_ascii=False)+'\n')
   return r
  def wait(r,label,seconds=400):
   until=time.monotonic()+seconds
   while r.get('status')=='running' and time.monotonic()<until:
    time.sleep(.5);r=tool('action.status',id=r['command_id']);write('latest-action.json',r)
   write(label+'.json',r);return r
  def scene():return sc('round3',mode='read')
  def events():
   paths=(mods/'Together/logs'/str(initial['save_id'])).glob('*/day-*.jsonl')
   return [json.loads(x) for pth in paths for x in pth.read_text().splitlines() if x.endswith('}')]
  sc('agent_pause');sc('preparation_probe');sc('round3',mode='bootstrap')
  until=time.monotonic()+240
  while time.monotonic()<until:
   d=scene()
   if d['stores']:break
   time.sleep(1)
  check('native-warehouse-available',bool(d['stores']),d)
  # Run care once; investment won't steal the test's pending crop energy.
  r=wait(tool('work.run',goal='water',count=0),'care',240);check('initial-care',r['status']=='succeeded',r)
  tool('farm.autonomy',enabled=True,budget_per_day=300,keep_gold=200,plots=24,max_daily_manual_water=24,priority='cashflow')
  until=time.monotonic()+420
  while time.monotonic()<until:
   d=sc('round4',mode='read');write('latest.json',d)
   if d['investment']['Phase']=='awaiting_selection':break
   if d['investment']['Phase']=='blocked':raise RuntimeError('investment_blocked:'+d['investment']['Error'])
   time.sleep(1)
  check('investment-awaits-model-selection',d['investment']['Phase']=='awaiting_selection',d)
  quote=tool('shop.read');write('quote.json',quote);decision=quote['seed_decision']
  check('quote-native-decision-basis',len(decision['candidates'])>=2 and all('Maximum' in x and 'Water' in x and 'Sale' in x for x in decision['candidates']))
  # A bounded real Flash choice: select two distinct feasible seed types for the
  # native chain test, without prescribing products or inventing any quote.
  import urllib.request
  key=next(x.split('=',1)[1].strip().strip('"').strip("'") for x in (ROOT/'.env').read_text().splitlines() if x.startswith('DEEPSEEK_API_KEY='))
  request=dict(model='deepseek-flash',messages=[dict(role='system',content='你是经营选品助手。根据真实报价与条件，从可成熟且Maximum>=1的种子中选择两个不同品种，每种1包用于小规模试种。总额不得超过budget。说明生长、回款和劳动取舍，不凭记忆补充。只返回JSON {"quote_token":"原值","items":[{"item":"QID","count":1}],"reason":"中文理由"}。'),dict(role='user',content=json.dumps(decision,ensure_ascii=False))],response_format=dict(type='json_object'),thinking=dict(type='disabled'),max_tokens=800)
  write('model-request.json',request)
  req=urllib.request.Request('https://api.deepseek.com/chat/completions',data=json.dumps(request).encode(),headers={'Content-Type':'application/json','Authorization':'Bearer '+key})
  with urllib.request.urlopen(req,timeout=60) as response:reply=json.load(response)
  write('model-response.json',reply);choice=json.loads(reply['choices'][0]['message']['content']);check('model-chose-two-products',len(choice['items'])==2 and len({x['item'] for x in choice['items']})==2,choice)
  chosen=tool('farm.select_seeds',**choice);write('selection.json',chosen);check('model-selection-accepted',chosen.get('status')=='selection_approved_not_purchased',chosen)
  until=time.monotonic()+600;net=None
  while time.monotonic()<until:
   d=sc('round4',mode='read');write('latest.json',d)
   if d['investment']['Phase']=='executing' and net is None:
    net=sc('round4',mode='net');write('net-kit.json',net)
   with (out/'chain-samples.jsonl').open('a') as f:f.write(json.dumps(dict(utc=time.time(),data=d),ensure_ascii=False)+'\n')
   if d['investment']['Phase'] in ('done','blocked'):break
   time.sleep(1)
  check('two-seeds-purchase-plant-water-chain',d['investment']['Phase']=='done' and all(x['water']==1 for x in d['farm']['farm']),d)
  check('native-purchase-ledger',d['ledger']['SpentToday']>0 and all(d['ledger']['PurchasedItems'].get(x['item'],0)>=1 for x in choice['items']),d['ledger'])
  overlay=sc('round4',mode='overlay');write('overlay.json',overlay);check('human-prose-survives-cleaning',any('种子' in x for x in overlay['lines']) and not any('stock_target' in x or 'request_id' in x for x in overlay['lines']))
  tool('farm.autonomy',enabled=False)
  # Dispatch a real trip so normal preparation unloads tools, not a fixture
  # inventory mutation. The subsequent bare-hand empty-target action is native.
  snapshot=d['snapshot'];tasks=[dict(id='r4-travel',tool='player.travel',args=dict(location='Town'),purpose='原生裸手采集验证',day=snapshot['day']),dict(id='r4-forage',tool='work.run',args=dict(goal='forage',location='Town',count=0),after=['r4-travel'],purpose='采集途中可见野生物',day=snapshot['day'])]
  plan=tool('plan.read');r=tool('plan.submit',submission_id='r4-unload',expected_revision=plan['revision'],tasks=tasks);write('trip.json',r)
  until=time.monotonic()+360
  while time.monotonic()<until:
   d=sc('round4',mode='read');write('trip-latest.json',d)
   results=d['schedule']['recent_results']
   if any(x['id']=='r4-forage' for x in results):break
   time.sleep(1)
  check('trip-after-unloading',any(x['id']=='r4-forage' and x['state']=='succeeded' for x in results),d)
  r=sc('round4',mode='selection');write('selection-invariant.json',r);check('illegal-slot-rejected-native-state-valid',r['rejected'] and r['valid'],r)
  check('hoe-not-forced-in-loadout',not any('Hoe' in str(x) or '锄头' in str(x) for x in d['inventory']['items']),d['inventory'])
  tile=d['snapshot']['tile'];r=wait(tool('player.work',skill='forage',tiles=[dict(x=tile[0],y=tile[1])]),'no-target-barehand',60)
  after=sc('round4',mode='read');write('no-target-after.json',after);check('no-target-next-tick-render-survives',0<=after['selected']<after['slots'])
  r=wait(tool('work.run',goal='forage',location='Town',count=0),'already-satisfied',180)
  check('already-satisfied-receipt-visible',r.get('disposition')=='already_satisfied' and r.get('resume_policy')=='do_not_retry_until_world_condition_changes',r)
  write('result.json',dict(passed=True,checks=checks))
 except Exception as e:
  write('result.json',dict(passed=False,error=str(e),checks=checks));print(type(e).__name__,str(e),flush=True)
 finally:
  if b:
   try:write('final.json',sc('preparation_read'));sc('agent_pause')
   except Exception:pass
  if proc.poll() is None:
   proc.stdin.write('agent_quit\n');proc.stdin.flush()
   try:proc.wait(timeout=30)
   except subprocess.TimeoutExpired:print('owned_exit_pending',flush=True)
  if 'initial' in locals():
   source=mods/'Together/logs'/str(initial['save_id'])
   if source.exists():shutil.copytree(source,out/'native-logs',dirs_exist_ok=True)
sys.exit(0 if json.loads((out/'result.json').read_text())['passed'] else 1)
