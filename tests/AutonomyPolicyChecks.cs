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
        string panel=OverlayText.Clean("投资Enabled且phase=done，准备买种子 [request_id]。",80);
        check(panel.Contains("准备买种子")&&!panel.Contains("Enabled")&&!panel.Contains("done"),"panel removes technical fragments while retaining explanation");
    }
}
