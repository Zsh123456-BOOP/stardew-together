using System.Text.Json;
using Together;
public static class AutoplayChecks {
    public static void Run(Action<bool,string> check) {
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
