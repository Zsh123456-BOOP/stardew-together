using System.Text.Json;

namespace Together;

// Serializable checkpoints contain observations and intentions, never an API credential.
public sealed class AutoplayState {
    public string RunId {get;set;}="";
    public int StartDay {get;set;}
    public int VerifiedActions {get;set;}
    public string Goal {get;set;}="";
    public string Status {get;set;}="stopped";
    public string Plan {get;set;}="";
    public string Detail {get;set;}="";
    public int Decisions {get;set;}
    public int SleepDays {get;set;}
    public List<AgentEvent> Journal {get;set;}=new();
    public void Record(string kind,string text) {
        Journal.Add(new(kind,text.Length>12000?text[..12000]:text));
        if(Journal.Count>32)Journal.RemoveRange(0,Journal.Count-32);
    }
}
public static class AgentJson {
    public static readonly JsonSerializerOptions Options=new(){Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
    public static string Encode(object? value)=>JsonSerializer.Serialize(value,Options);
}
public sealed record AgentEvent(string Kind,string Text);
public sealed class AgentCall {
    public string tool {get;set;}="";
    public JsonElement args {get;set;}
}
public sealed class AgentTurn {
    public string plan {get;set;}="";
    public string speech {get;set;}="";
    public List<AgentCall> calls {get;set;}=new();
    public static AgentTurn Parse(string json) {
        var turn=JsonSerializer.Deserialize<AgentTurn>(json)??throw new InvalidOperationException("empty_turn");
        if(turn.plan==null || turn.plan.Length>1200 || turn.speech==null || turn.speech.Length>300 || turn.calls==null || turn.calls.Count is <1 or >6)
            throw new InvalidOperationException("invalid_turn");
        foreach(var c in turn.calls)
            if(c==null || c.tool==null || c.tool.Length>60 || c.args.ValueKind!=JsonValueKind.Object)
                throw new InvalidOperationException("invalid_tool_call");
        return turn;
    }
}
public static class AutoplaySpeed {
    public static double Clock(double requested)=>double.IsFinite(requested)?Math.Clamp(requested,1,8):1;
    public static int ExtraMilliseconds(double requested,double elapsed,bool eligible)=>eligible?(int)Math.Clamp((Clock(requested)-1)*elapsed,0,250):0;
    public static int DecisionDelay(int value)=>Math.Clamp(value,100,10000);
}
