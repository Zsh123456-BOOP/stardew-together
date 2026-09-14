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
