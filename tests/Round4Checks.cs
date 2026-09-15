using System.Text.Json;
using Together;
public static class Round4Checks {
    public static void Run(Action<bool,string> check) {
        check(SlotInvariant.Check(0,12)==0&&SlotInvariant.Check(11,12)==11,"selected slots include both boundaries");
        foreach(int n in new[]{-1,12}) {bool rejected=false;try{SlotInvariant.Check(n,12);}catch(InvalidOperationException){rejected=true;}check(rejected,"invalid selected slot rejected before write");}
        check(DecisionBarrier.State(new[]{"running","queued"})=="waiting","queued action cannot satisfy observation prerequisite");
        check(DecisionBarrier.State(new[]{"succeeded","succeeded"})=="ready","all native prerequisites completed");
        check(DecisionBarrier.State(new[]{"succeeded","failed"})=="failed"&&DecisionBarrier.State(new string?[]{null})=="failed","failed or missing dependency does not execute tail");
        check(DecisionBarrier.Control("agent.pause")&&!DecisionBarrier.Control("shop.read"),"pause is never trapped behind action barrier");
        check(StorageTiming.StoreComplete(6,3,0,84)&&StorageTiming.StoreComplete(10,8,0,4)&&StorageTiming.StoreComplete(10,0,0,15),"all three baseline unloads meet physical completion after depth reset");
        check(StorageTiming.StoreComplete(6,0,0,0),"already unloaded cargo is an idempotent successful store");
        check(!StorageTiming.StoreComplete(6,8,0,4)&&!StorageTiming.StoreComplete(6,0,2,4),"unmet explicit slots or remaining cargo do not falsely complete");
        foreach(string json in new[]{"[]","[{}]","null","42","\"value\"","{\"status\":false}"})check(DecisionBarrier.Text(JsonDocument.Parse(json).RootElement,"status")==null,"non-envelope observation stays valid: "+json);
        check(DecisionBarrier.Text(JsonDocument.Parse("{\"status\":\"running\"}").RootElement,"status")=="running","object receipt metadata remains available");
        var diary=new ActivityDiary();
        var native=JsonSerializer.SerializeToElement(new{command_id="native-one",skill="player.buy",status="succeeded",effects=new[]{new{kind="native_purchase",item="(O)472",units=3,cost=60,currency=0}}});
        diary.Native(native,0,900);diary.Native(native,0,900);
        check(diary.Rows.Single().Count==3&&diary.Rows.Single().Cost==60,"native diary replay cannot double purchase or cost");
        var savedDiary=JsonSerializer.Deserialize<ActivityDiary>(JsonSerializer.Serialize(diary))!;savedDiary.Native(native,0,910);
        check(savedDiary.Rows.Single().Count==3,"diary dedup survives checkpoint serialization");
        check(!diary.Add("zero",0,920,"采集","wood","Farm",0),"zero progress creates no fake success");
        diary.Add("other",0,930,"购买","(O)472","",2,40);
        check(diary.Rows.Single().Count==5&&diary.Rows.Single().Cost==100,"distinct native batches merge without losing quantity");
        var known=new FailureKnowledge();known.Record("target","player","work.run","remaining_targets_unreachable","map-A","attempt1",0,900,true);known.Record("target","player","work.run","remaining_targets_unreachable","map-A","attempt1",0,900,true);known.Record("target","player","work.run","remaining_targets_unreachable","map-A","attempt2",0,910,true);
        check(known.Entries.Count==1&&known.Entries[0].Attempts==2,"repeated root merges and duplicate receipt does not count twice");
        check(known.Block("target","map-A",1,600)!=null&&known.Block("target","map-B",1,600)==null,"persistent physical failure waits for relevant change across days");
        check(WorkQuantity.Resolve(0,0,30,null,true)==new WorkQuantity(30,30,null)&&WorkQuantity.Resolve(40,0,0,100,true)==new WorkQuantity(100,60,null),"model freely requests count and total stock without approved project");
        var credits=new PreparationCredits<string>();credits.Produce("seed-a",4);credits.Produce("seed-b",3);
        check(credits.Require(3,x=>x=="seed-a")==0,"earlier purchase covers later sowing");
        check(credits.Require(3,x=>x=="seed-a")==2,"future output cannot cover two successors twice");
        check(credits.Require(4,x=>x=="seed-b")==1,"different goods tracked separately");
        check(credits.Require(2,x=>x=="wood")==2,"later creation cannot supply earlier consumption");
        string cleaned=OverlayText.Clean("先购买种子 [stock_target]，再种田 request_id=123abc。",100);
        check(cleaned.Contains("购买种子")&&cleaned.Contains("种田")&&!cleaned.Contains("stock_target")&&!cleaned.Contains("request_id"),"clean technical fragments without discarding explanation");
        foreach(var pair in new[]{("succeeded","","already_satisfied"),("failed","remaining_targets_unreachable","no_candidates_found"),("failed","native_failure","failed")}) {
            var raw=new{command_id="check",status=pair.Item1,error=pair.Item2,completed=0};
            var result=JsonSerializer.SerializeToElement(ExecutionContract.Receipt(raw,"epoch",0));
            check(result.GetProperty("disposition").GetString()==pair.Item3,"disposition reaches model receipt "+pair.Item3);
        }
    }
}
