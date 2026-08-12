using System.Text.Json;
using Together;
public static class AutoplayChecks {
    public static void Run(Action<bool,string> check) {
        check(AutoplaySpeed.Clock(double.NaN)==1 && AutoplaySpeed.Clock(double.PositiveInfinity)==1,"nonfinite clock settings cannot corrupt native timer");
        check(AutoplaySpeed.Clock(200)==8 && AutoplaySpeed.Clock(-3)==1,"clock acceleration is bounded");
        check(AutoplaySpeed.ExtraMilliseconds(8,16,false)==0,"no acceleration during protected movement/model/menu state");
        check(AutoplaySpeed.ExtraMilliseconds(4,16,true)==48 && AutoplaySpeed.ExtraMilliseconds(8,400,true)==250,"extra native time uses multiplier and limits frame stalls");
        check(AutoplaySpeed.DecisionDelay(-10)==100 && AutoplaySpeed.DecisionDelay(100000)==10000,"decision intervals cannot become hot loops");
        var turn=AgentTurn.Parse("{\"plan\":\"准备种植\",\"calls\":[{\"tool\":\"inventory.read\",\"args\":{}},{\"tool\":\"knowledge.search\",\"args\":{\"query\":\"防风草\"}}]}");
        check(turn.calls.Count==2,"one decision can request independent state and encyclopedia evidence");
        foreach(var bad in new[]{"{\"calls\":[]}","{\"calls\":null}","{\"calls\":[{\"tool\":\"world.read\",\"args\":null}]}","{\"plan\":null,\"calls\":[{\"tool\":\"world.read\",\"args\":{}}]}"}) {
            bool rejected=false;try{AgentTurn.Parse(bad);}catch(InvalidOperationException){rejected=true;}check(rejected,"malformed model turn rejected before execution");
        }
        var state=new AutoplayState{Goal="种养与共同经营",Status="running",Plan="每天浇水；别忘记收获"};
        for(int i=0;i<40;i++)state.Record("observation","day"+i);
        var restored=JsonSerializer.Deserialize<AutoplayState>(JsonSerializer.Serialize(state))!;
        check(restored.Journal.Count==32 && restored.Journal[0].Text=="day8" && restored.Plan==state.Plan,"bounded episode evidence and continuing plan survive save serialization");
    }
}
