using System.Text.Json;
using Together;

AutonomyDevelopmentChecks.Run((value,label)=>{if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);});

AutoplayChecks.Run((value,label)=>{if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);});

GoalChecks.Run((value,label)=>{if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);});

KnowledgeChecks.Run((value,label)=>{if(!value)throw new Exception(label);Console.WriteLine("PASS: "+label);});

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
person.Energy=99;person.Outcome("fish",false);Check(person.Energy==94,"fishing has a physical cost even when it satisfies interest");
person.Outcome("rest",false);Check(person.Energy==100,"rest restores energy without overflow");
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


var relation=new RelationshipState();relation.Apply("promise-1","promise_kept",1);relation.Apply("promise-1","promise_kept",1);
Check(relation.Trust==36 && relation.Cooperation==36,"replayed promise result cannot duplicate relationship reward");
var comfort=relation.Comfort;relation.Apply("quiet-1","quiet",1);relation.Apply("decline-1","declined",1);relation.Apply("fail-1","external_failure",1);
Check(relation.Comfort==comfort,"quiet mode, refusal and external failure do not penalise friendship");
relation.Apply("force-1","forced",1);relation.Apply("force-1","forced",1);
Check(relation.Comfort==comfort-2,"forced commitment affects relationship once, not once per action");
var social=new SocialState();social.Open(1,"hello","source-1");social.PlaySeconds=179;
Check(!social.CanOpen(1),"ordinary sharing cannot interrupt again before three minutes of active play");
social.PlaySeconds=180;Check(social.CanOpen(1),"sharing resumes after real active-play interval");social.Open(1,"second","source-2");
social.PlaySeconds=360;social.Open(1,"third","source-3");social.PlaySeconds=1000;Check(!social.CanOpen(1),"ordinary proactive openings capped at three per day");
social.Mode="quiet";Check(!social.CanOpen(2),"quiet mode persists across days without stopping autonomous work");
social.ObserveShared("a","fish","Beach",1,true);social.ObserveShared("a","fish","Beach",1,true);social.ObserveShared("b","fish","Beach",2,false);
Check(social.Habits.Count==1 && social.Habits[0].Days.Count==1 && !social.Habits[0].Confirmed,"only voluntary distinct-day participation suggests a habit; confirmation is separate");
social.PlayerNotes[1]="今天很开心";social.CloseDay(1,new[]{new Experience{Id="real-event",Day=1,Summary="收了一株作物"}},Array.Empty<SharedProject>());
social.CloseDay(1,Array.Empty<Experience>(),Array.Empty<SharedProject>());
Check(social.Diary.Count==1 && social.Diary[0].Sources.SequenceEqual(new[]{"real-event"}) && social.Diary[0].PlayerNote=="今天很开心","diary is sourced, idempotent and separates player notes");
social.Forget();Check(social.Topics.Count+social.Habits.Count+social.Diary.Count+social.Preferences.Count+social.PlayerNotes.Count==0,"forget clears derived memories as well as raw topics");
var migrated=JsonSerializer.Deserialize<SaveData>("{\"SchemaVersion\":2,\"People\":{\"Abigail\":{\"Energy\":70}}}")!;
Check(migrated.People["Abigail"].Social.Relationship.Trust==35 && migrated.FarmPolicy.DailyBudget==0,"old saves gain safe social defaults and no spending permission");
var checkpoint=new Job{Steps=new(){new(){skill="fish"}},RemainingSeconds=7,PartialReceipts=new(){"caught-before-pause"}};
var timedRestore=JsonSerializer.Deserialize<Job>(JsonSerializer.Serialize(checkpoint))!;
Check(timedRestore.RemainingSeconds==7 && timedRestore.PartialReceipts.Count==1,"interrupted timed activity survives serialization without replaying captured partial result");

var togetherReward=new SocialState();togetherReward.Relationship.Apply("joint-event","promise_kept",3);togetherReward.ObserveShared("joint-event","fish","Beach",3,true);
Check(togetherReward.Relationship.Trust==36 && togetherReward.Relationship.Comfort==41,"keeping a promise and shared time have independent idempotent effects");
var journal=new SocialState();
journal.CloseDay(2,new[]{new Experience{Id="rest-proof",Day=2,Skill="rest",Location="Farm",PlayerParticipated=true,Summary="internal command result"},new Experience{Id="fish:partial",Day=2,Skill="fish",Summary="钓鱼进行了 5 秒，安排还未结束"}},Array.Empty<SharedProject>());
Check(journal.Diary[0].Text.Contains("歇了一会儿") && journal.Diary[0].Text.Contains("安排还未结束") && !journal.Diary[0].Text.Contains("internal command"),"journal uses grounded natural language without turning partial work into completion");
