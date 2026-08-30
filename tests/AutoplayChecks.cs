using System.Text.Json;
using Together;
public static class AutoplayChecks {
    public static void Run(Action<bool,string> check) {
        AgentScheduleChecks.Run(check);
        var access=new[]{new WaterAccessCell(new(0,0),true,0),new WaterAccessCell(new(1,0),false,4),new WaterAccessCell(new(2,0),true,0)};
        var banks=new HashSet<FarmCell>{new(2,0)};
        check(WaterAccessPlan.Find(access,new(0,0),banks,4).Count==2,"water access includes a clearable blocker when labor fits the budget");
        check(WaterAccessPlan.Find(access,new(0,0),banks,3).Count==0,"water access cannot spend crop-care stamina reserve");
        check(WaterAccessPlan.Find(access.Select(c=>c.Tile==new FarmCell(1,0)?c with{ClearEnergy=0}:c),new(0,0),banks,40).Count==0,"protected or nonclearable obstacles are never removed for water access");
        var pace=new AgentDecisionPacing();
        pace.Observe(0,false,false);pace.Observe(0,false,false);pace.Observe(0,false,false);
        check(pace.Observe(0,false,false)==8,"repeated queries without game progress use a bounded backoff");
        pace.Observe(0,false,false);pace.Observe(0,false,false);
        check(pace.Observe(0,false,false)==20,"long query loops cannot issue a paid request each frame");
        check(pace.Observe(1,false,false)==1&&pace.QueriesWithoutProgress==0,"verified progress restores responsive planning");
        check(pace.Observe(1,true,false)==1,"a submitted action ends a query-only episode");
        check(AgentDecisionPacing.RoutineWake("actor_ready:custom")&&!AgentDecisionPacing.RoutineWake("task_failed:water")&&!AgentDecisionPacing.RoutineWake("danger"),"routine completion can defer but real failures and danger cannot");
        var catalog=new Dictionary<string,string>{{"world.read","{}: current state"},{"player.build","{blueprint,budget,keep_gold}: 原生建筑"},{"player.arcade","{game}: 街机"}};
        check(AgentToolDiscovery.Core(catalog).Count==1&&!AgentToolDiscovery.Core(catalog).ContainsKey("player.arcade"),"unrelated special gameplay is omitted from the stable common catalog");
        var lookup=JsonSerializer.Serialize(AgentToolDiscovery.Lookup(catalog,JsonSerializer.SerializeToElement(new{names=new[]{"player.build"}})));
        check(lookup.Contains("blueprint,budget,keep_gold"),"on-demand tool discovery preserves full parameter contract");
        var failureState=new FailureKnowledge();var failureKey=FailureKnowledge.Key("player","interact","{}");
        string at1=FailureKnowledge.Hash(JsonSerializer.Serialize(new{tile=new[]{3,11}})),at2=FailureKnowledge.Hash(JsonSerializer.Serialize(new{tile=new[]{3,8}}));
        failureState.Record(failureKey,"player","interact","adjacent",at1,"attempt",0,360);
        check(failureState.Block(failureKey,at1,0,361)!=null&&failureState.Block(failureKey,at2,0,361)==null,"explicit coordinate evidence invalidates a spatial failure after movement");
        var failures=new AgentFailureTracker();
        for(int i=0;i<5;i++)failures.Failed("player","no_path");failures.Progress("npc");
        check(failures.Failed("player","no_path"),"companion progress cannot hide a stuck player lane");
        failures.Progress("player");check(!failures.Failed("player","no_path"),"actual player progress resets old failures instead of pausing after unrelated errors all day");
        check(AgentCallContract.CompanionError(JsonSerializer.SerializeToElement(new{actor_id="npc",skill="mine",destination="Mountain"}))=="companion_target_required_travel_then_read_candidates","remote labor without a real target gives an actionable travel/read error");
        check(AgentCallContract.CompanionError(JsonSerializer.SerializeToElement(new{actor_id="npc",skill="travel",destination="Farm"}))==null && AgentCallContract.CompanionError(JsonSerializer.SerializeToElement(new{actor_id="npc",skill="mine",target_id="observed"}))==null,"travel and observed-target labor have separate valid contracts");
        check(AutoplaySpeed.Clock(double.NaN)==1 && AutoplaySpeed.Clock(double.PositiveInfinity)==1,"nonfinite clock settings cannot corrupt native timer");
        check(AutoplaySpeed.Clock(200)==1 && AutoplaySpeed.Clock(-3)==1,"legacy multiplier settings cannot enable acceleration");
        check(AutoplaySpeed.ExtraMilliseconds(8,16,false)==0,"no acceleration during protected movement/model/menu state");
        check(AutoplaySpeed.ExtraMilliseconds(4,16,true)==0 && AutoplaySpeed.ExtraMilliseconds(8,400,true)==0,"no extra time is injected even while idle");
        check(AutoplaySpeed.DecisionDelay(-10)==100 && AutoplaySpeed.DecisionDelay(100000)==10000,"decision intervals cannot become hot loops");
        var turn=AgentTurn.Parse("{\"plan\":\"准备种植\",\"calls\":[{\"tool\":\"inventory.read\",\"args\":{}},{\"tool\":\"knowledge.search\",\"args\":{\"query\":\"防风草\"}}]}");
        check(turn.calls.Count==2,"one decision can request independent state and encyclopedia evidence");
        foreach(var bad in new[]{"{\"calls\":[]}","{\"calls\":null}","{\"calls\":[{\"tool\":\"world.read\",\"args\":null}]}","{\"plan\":null,\"calls\":[{\"tool\":\"world.read\",\"args\":{}}]}"}) {
            bool rejected=false;try{AgentTurn.Parse(bad);}catch(InvalidOperationException){rejected=true;}check(rejected,"malformed model turn rejected before execution");
        }
        check(DailyBudget.WorkMinutes(1200,90)==570 && DailyBudget.WorkMinutes(2250,45)==0,"normal-time work budget preserves return-home reserve");
        check(!DailyBudget.Fits(1200,16,45,10,2) && !DailyBudget.Fits(2250,200,45,10,0),"tasks must fit both energy and return budget");
        check(DailyBudget.SleepBlock(900,200,false,true,false,true,"种植完成")!=null,"planting done is not permission to waste useful daylight");
        check(DailyBudget.SleepBlock(900,10,true,true,false,true,"体力不足")!=null,"low energy still considers free forage");
        check(DailyBudget.SleepBlock(2100,10,false,false,true,true,"体力不足")==null && DailyBudget.SleepBlock(2230,200,false,true,true,false,"安全返家")==null,"exhaustion and late return can end work safely");
        check(DailyBudget.Fits(1000,10,45,10,0),"free forage remains feasible below the tool-energy reserve");
        var agenda=new DailyAgenda();agenda.EnterDay(1);agenda.Priorities.Add("给鸡舍留木材");agenda.CompletedBatches=3;agenda.EnterDay(2);agenda.EnterDay(2);
        check(agenda.History.Count==1 && agenda.CompletedBatches==0 && agenda.Priorities.Count==1,"daily work resets once while commitments carry forward");
        var checkpoint=new AutoplayState{NativeSleepRequestedDay=23};
        check(!checkpoint.ReconcileSleep(23) && checkpoint.ReconcileSleep(24) && !checkpoint.ReconcileSleep(24) && checkpoint.SleepDays==1,"native saved sleep counted exactly once after reload or day-start");
        check(!new AutoplayState().ReconcileSleep(24),"date change without an observed native sleep request is not normal sleep proof");
        var state=new AutoplayState{Goal="种养与共同经营",Status="running",Plan="每天浇水；别忘记收获"};
        for(int i=0;i<40;i++)state.Record("observation","day"+i);
        var restored=JsonSerializer.Deserialize<AutoplayState>(JsonSerializer.Serialize(state))!;
        check(restored.Journal.Count==32 && restored.Journal[0].Text=="day8" && restored.Plan==state.Plan,"bounded episode evidence and continuing plan survive save serialization");
    }
}
