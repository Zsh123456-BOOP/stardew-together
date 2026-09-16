using System.Text.Json;
using Together;
internal static class AutonomyPolicyChecks {
    internal static void Run(Action<bool,string> check) {
        check(AutonomyPolicy.Cash(500,0,-1,240,0)==500,"unlimited policy uses actual cash, never subtracts historical spend twice");
        check(AutonomyPolicy.Cash(260,200,300,240,0)==60,"explicit policy shows the real remaining 60 gold");
        check(AutonomyPolicy.Cash(500,0,-1,0,120,30)==350,"pending purchases and named capital remain committed once");
        check(AutonomyPolicy.Cash(20,100,-1,0,0)==0,"model reserve never grants nonexistent gold");
        var choices=AutonomyPolicy.Seeds(new[]{new SeedRequest("potato",4,50,99),new SeedRequest("parsnip",10,20,99)},300);
        check(choices["potato"]==4&&choices["parsnip"]==5,"oversized quote selection shrinks quantities in model order, never changes crop");
        check(AutonomyPolicy.Seeds(new[]{new SeedRequest("free",5,0,2)},0)["free"]==2,"zero price still respects native stock");
        check(new FarmInvestmentPolicy().ManualWaterLimit==-1&&new FarmBusiness().DailyBudget==-1&&new FarmBusiness().KeepGold==0,"no default manual crop or cash ceiling");
        check(!new FarmBusiness().Automation&&!new FarmInvestmentPolicy().Enabled,"capability does not create an unrequested routine");
        check(CleanupBudget.Calculate(5,270,0,100,100,200,1000).Available==5,"care forecasts and historical work cannot silently reserve stamina");
        check(DailyBudget.Fits(1000,4,30,10,4),"last usable energy is available without a fixed reserve");
        check(AutonomyPolicy.InvestmentState("done","care_capacity_full_wait_for_harvest")=="deferred"&&AutonomyPolicy.InvestmentState("done","")=="completed","business deferral is not completion");
        var tools=new Dictionary<string,string>{{"player.social","社交、送礼"},{"knowledge.search","百科查询"},{"world.read","世界"}};
        var found=JsonSerializer.SerializeToElement(AgentToolDiscovery.Lookup(tools,JsonSerializer.SerializeToElement(new{query="greet NPC"})));
        check(found.GetProperty("definitions").TryGetProperty("player.social",out _),"English synonym resolves a real social tool");
        var missing=JsonSerializer.SerializeToElement(AgentToolDiscovery.Lookup(tools,JsonSerializer.SerializeToElement(new{names=new[]{"invented.tool"}})));
        check(missing.GetProperty("definitions").EnumerateObject().Any()&&missing.GetProperty("unknown")[0].GetString()=="invented.tool","unknown tools retain explicit error plus actionable discovery");
        var order=new FarmCleanupOrder{Day=1,Status="active",Completed=24,PendingPickup=new(){new(2,3)}};
        FarmCleanupRules.NewDay(order,2);
        check(order.Status=="paused"&&order.Completed==24&&order.PendingPickup.Count==1,"new day preserves cleanup progress and pickups without preempting model");
        var partial=JsonSerializer.SerializeToElement(new{status="partial",stop_reason="cleanup_insufficient_remaining_allowance",completed=24,requested=100});
        var outcome=OperationsPolicy.Outcome("work.run",partial);
        check(outcome.Disposition=="partial"&&outcome.BusinessProgress&&outcome.Remaining==76,"budget yield preserves verified progress without claiming completion");
        check(DecisionBarrier.State(new[]{"partial"})=="failed","partial prerequisite cannot release dependent native consumption");
        var q=new AgentSchedule();q.Submit("partial",0,new(){new(){id="first",tool="work.run",day=1},new(){id="after",tool="player.craft",day=1,after=new(){"first"}}},1);
        q.Finish(q.Tasks[0],"partial",null,"{}");q.Ready(1,800);
        check(q.Tasks[0].Terminal&&q.Tasks[1].state=="blocked","partial closes actor ownership but blocks incomplete dependencies");
        bool Walk(FarmCell c)=>c.X>=0&&c.X<6&&c.Y>=0&&c.Y<4&&c!=new FarmCell(2,1);
        var path=AutonomyPolicy.Path(new(0,1),new(5,1),Walk)!;
        check(path.Count==8&&path.All(Walk)&&path.Zip(path.Skip(1)).All(p=>FarmDistrict.Distance(p.First,p.Second)==1),"path and map use same passability without diagonals or barrier shortcuts");
        check(AutonomyPolicy.Path(new(0,1),new(2,1),Walk)==null,"blocked destination cannot enter a planned path");
        check(AutonomyPolicy.ResourceCost(3,30,15,17,50)<AutonomyPolicy.ResourceCost(12,2,1,1,50),"near tree beats distant twig per useful wood, no branch-first policy");
        var packed=JsonDocument.Parse(ContextCompression.Pack(new{progression=new{active_quests=new[]{"grow crops"},unread_mail=new[]{"Willy"},nearby_achievements=new[]{"income"}},inventory_plan=new{slot_count=12,free_slots=1,occupied=11,payload=new string('x',1000)}},20));
        check(packed.RootElement.GetProperty("progression").TryGetProperty("active_quests",out _),"daily progress remains factual under context pressure");
        string panel=OverlayText.Clean("投资Enabled且phase=done，准备买种子 [request_id]。",80);
        check(panel.Contains("准备买种子")&&!panel.Contains("Enabled")&&!panel.Contains("done"),"panel removes technical fragments while retaining explanation");
    }
}
