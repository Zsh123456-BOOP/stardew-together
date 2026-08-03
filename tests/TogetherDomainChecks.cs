using System.Text.Json;
using Together;

void Check(bool value,string label){if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);}
void Reject(string json){try{Decision.Parse(json);throw new Exception("Invalid decision accepted");}catch(InvalidOperationException){}}
Reject("{\"decision\":\"accept\",\"speech\":\"好\",\"steps\":[]}");
Reject("{\"decision\":\"accept\",\"speech\":\"好\",\"steps\":[{\"skill\":\"teleport\",\"count\":1}]}");
Reject("{\"decision\":\"accept\",\"speech\":\"好\",\"steps\":[{\"skill\":\"mine\",\"count\":1000}]}");
Reject("{\"decision\":\"chat\",\"speech\":\"好\",\"steps\":[{\"skill\":\"mine\",\"count\":1}]}");
Reject("{\"decision\":\"accept\",\"speech\":\"好\",\"title\":null,\"steps\":[{\"skill\":\"rest\",\"count\":1}]}");
Console.WriteLine("PASS: unsafe/malformed plans rejected");
var proposal=Decision.Parse("{\"decision\":\"negotiate\",\"speech\":\"先挖两块，再一起休息？\",\"steps\":[{\"skill\":\"mine\",\"count\":2},{\"skill\":\"rest\",\"count\":1}]}");
var job=new Job {Steps=proposal.steps,Command="first"};
Check(!job.Advance() && job.Index==0 && job.DoneInStep==1 && job.Command==null,"first action does not fulfill promise");
var restored=JsonSerializer.Deserialize<Job>(JsonSerializer.Serialize(job))!;
Check(!restored.Advance() && restored.Index==1 && restored.DoneInStep==0,"save/restore preserves multi-action progress");
Check(restored.Advance() && restored.Status=="fulfilled" && restored.Completed==3,"promise fulfills only after every action");
var person=new Companion {Energy=3,Bond=1};person.Outcome("mine",true);
Check(person.Energy==0 && person.Bond==1,"individual actions do not multiply forced-commitment relationship cost");
person.Energy=99;person.Outcome("fish",false);Check(person.Energy==100,"leisure restores energy without overflow");
var save=new SaveData {People=new(){["Abigail"]=new Companion{Proposal=proposal,Job=restored,Profile=Profile.Preset("钓鱼搭子")}}};
var loaded=JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(save))!;
Check(loaded.People["Abigail"].Job!.Status=="fulfilled" && loaded.People["Abigail"].Proposal!.decision=="negotiate","persona, proposal and memories serialize together");
person.Energy=20;person.NewDay(5);person.NewDay(5);
Check(person.Energy==20,"reloading the same day does not refill energy");
person.NewDay(6);person.NewDay(6);
Check(person.Energy==55,"new-day energy recovery happens once");

var fisher=new Companion{Profile=Profile.Preset("钓鱼搭子"),Energy=80};
fisher.Life.Tick(1,600,true,fisher.Profile);
var situation=new Situation{Day=1,Minute=600,Location="Beach",Fishing=true,NearPlayer=true};
Check(LifePlanner.Select(fisher,situation)!.Id=="fish","personal preference produces executable independent goal");
fisher.Energy=10;Check(LifePlanner.Select(fisher,situation)!.Id=="rest","real fatigue beats preferred leisure");
fisher.Energy=80;situation.Threat=true;Check(LifePlanner.Select(fisher,situation)!.Id=="defend","immediate danger overrides personal preference");
situation.Threat=false;fisher.Life.RetryAfter["fish"]=660;
Check(!LifePlanner.Options(fisher,situation).Any(o=>o.Id=="fish"),"failed goal is excluded until its retry window");
fisher.Life.Interest=50;fisher.Life.Tick(1,610,false,fisher.Profile);var value=fisher.Life.Interest;
fisher.Life.Tick(1,610,false,fisher.Profile);Check(value==fisher.Life.Interest,"paused clock does not inflate needs");
fisher.Life.Complete("fish",1,630,"实际钓鱼一次",true);
var roundTrip=JsonSerializer.Deserialize<Companion>(JsonSerializer.Serialize(fisher))!;
Check(roundTrip.Life.WishProgress==1 && roundTrip.Life.Experiences.Count==1,"personal wishes and evidence survive serialization");
situation.Minute=23*60;Check(!LifePlanner.Options(fisher,situation).Any(o=>o.Id=="fish"),"late night stops initiating new leisure or labor");

var traveller=new Companion{Profile=Profile.Preset("钓鱼搭子"),Energy=80};traveller.Life.Interest=90;
var travelSituation=new Situation{Day=1,Minute=900,Location="Farm",CanReachBeach=true,NearPlayer=true};
var trip=LifePlanner.Select(traveller,travelSituation)!;
Check(trip.Id=="beach_trip" && trip.Steps[0].location=="Beach","personal preference can form an independent cross-map goal");
travelSituation.CanReachBeach=false;Check(!LifePlanner.Options(traveller,travelSituation).Any(o=>o.Id=="beach_trip"),"cross-map wish requires a real route");

var worker=new Companion{Profile=Profile.Preset("农场伙伴"),Energy=80};
var farmSituation=new Situation{Day=1,Minute=600,Location="Farm",Refill=1,FarmProject=true,NearPlayer=true};
Check(LifePlanner.Select(worker,farmSituation)!.Id=="refill","available authorised machine supplies produce shared work");
farmSituation.Refill=0;farmSituation.Deposit=1;
Check(LifePlanner.Select(worker,farmSituation)!.Id=="deposit","leftover physical cargo has an executable storage goal");

var episodes=new List<Experience>{new(){Day=1,Minute=900,Summary="在海边钓鱼，收获0件"},new(){Day=20,Minute=800,Summary="浇水两次"},new(){Day=22,Minute=800,Summary="未来尚未发生的钓鱼"}};
var recall=MemoryRecall.Select(episodes,"还记得那次空军钓鱼吗",20,1);
Check(recall.Count==1 && recall[0].Day==1,"older relevant experience beats unrelated recent work and excludes future history");
