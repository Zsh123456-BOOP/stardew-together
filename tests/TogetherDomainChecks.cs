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
Check(person.Energy==0 && person.Bond==0,"forced labor affects bounded personal state");
person.Energy=99;person.Outcome("fish",false);Check(person.Energy==100,"leisure restores energy without overflow");
var save=new SaveData {People=new(){["Abigail"]=new Companion{Proposal=proposal,Job=restored,Profile=Profile.Preset("钓鱼搭子")}}};
var loaded=JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(save))!;
Check(loaded.People["Abigail"].Job!.Status=="fulfilled" && loaded.People["Abigail"].Proposal!.decision=="negotiate","persona, proposal and memories serialize together");
person.Energy=20;person.NewDay(5);person.NewDay(5);
Check(person.Energy==20,"reloading the same day does not refill energy");
person.NewDay(6);person.NewDay(6);
Check(person.Energy==55,"new-day energy recovery happens once");
