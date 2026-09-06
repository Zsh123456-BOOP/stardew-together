using System.Text.Json;

namespace Together;

// Serializable checkpoints contain observations and intentions, never an API credential.
public sealed class AutoplayState {
    public RoutinePolicy Routine {get;set;}=new();
    public ProgressCampaign Campaign {get;set;}=new();
    public Dictionary<string,int> ProfessionChoices {get;set;}=new();
    public FamilyPolicy Family {get;set;}=new();
    public FailureKnowledge Failures {get;set;}=new();
    public MemoryCheckpoint Memory {get;set;}=new();
    [System.Text.Json.Serialization.JsonIgnore,Newtonsoft.Json.JsonIgnore]
    public Action<string,string>? Archive {get;set;}
    public OperationsState Operations {get;set;}=new();
    public AgentSchedule Schedule {get;set;}=new();
    public DailyAgenda Agenda {get;set;}=new();
    public string RunId {get;set;}="";
    public int StartDay {get;set;}
    public int VerifiedActions {get;set;}
    public string Goal {get;set;}="";
    public string Status {get;set;}="stopped";
    public string Plan {get;set;}="";
    public string Detail {get;set;}="";
    public int Decisions {get;set;}
    public int SleepDays {get;set;}
    public int NativeSleepRequestedDay {get;set;}=-1;
    public int NativeSleepCountedDay {get;set;}=-1;
    public bool ReconcileSleep(int currentDay) {
        if(NativeSleepRequestedDay<0 || currentDay!=NativeSleepRequestedDay+1 || NativeSleepCountedDay>=NativeSleepRequestedDay)return false;
        SleepDays++;NativeSleepCountedDay=NativeSleepRequestedDay;return true;
    }
    public List<AgentEvent> Journal {get;set;}=new();
    public void Record(string kind,string text) {
        Archive?.Invoke(kind,text);
        Journal.Add(new(kind,text.Length>16000?AgentJson.Encode(new{truncated=true,prefix=text[..15000],note="观察过长已截断，未列出不代表不存在；用更小范围重新查询"}):text));
        if(Journal.Count>32)Journal.RemoveRange(0,Journal.Count-32);
    }
}
public sealed class FamilyPolicy {
    public string Partner {get;set;}="";
    public bool? AcceptChildren {get;set;}
    public int TargetChildren {get;set;}=2;
    public List<string> ChildNames {get;set;}=new();
    public bool AutoNameAnimals {get;set;}=true;
}
public sealed class RoutinePolicy {
    public bool Enabled {get;set;}
    public int Version {get;set;}
    public int SubmittedDay {get;set;}=-1;
    public string LastError {get;set;}="";
    public Dictionary<string,string> Assignments {get;set;}=new();
}
public static class AgentJson {
    public static readonly JsonSerializerOptions Options=new(){DefaultIgnoreCondition=System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull,Encoder=System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping};
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
    public static double Clock(double requested)=>1;
    public static int ExtraMilliseconds(double requested,double elapsed,bool eligible)=>0;
    public static int DecisionDelay(int value)=>Math.Clamp(value,100,10000);
}

public static class AgentCallContract {
    public static string? CompanionError(JsonElement args) {
        string Text(string key)=>args.TryGetProperty(key,out var e)&&e.ValueKind==JsonValueKind.String?e.GetString()??"":"";
        if(Text("actor_id").Length==0)return "companion_actor_id_required";
        string skill=Text("skill");
        if(skill=="travel")return Text("destination").Length==0?"companion_travel_destination_required":null;
        if(skill is "follow" or "stay" or "guard" or "rest" or "fish" or "dismiss")return null;
        if(skill is not ("buy" or "ship" or "clear" or "till" or "plant" or "feed" or "tend" or "forage" or "gift" or "refill" or "deposit" or "mine" or "water" or "harvest" or "pet" or "collect"))return "unsupported_companion_skill";
        return Text("target_id").Length==0?"companion_target_required_travel_then_read_candidates":null;
    }
}

public sealed class AgentFailureTracker {
    private readonly Dictionary<string,Dictionary<string,int>> attempts=new();
    public bool Failed(string actor,string code) {
        if(!attempts.TryGetValue(actor,out var errors))attempts[actor]=errors=new();
        int count=errors.GetValueOrDefault(code)+1;errors[code]=count;return count>=6;
    }
    public void Progress(string actor)=>attempts.Remove(actor);
    public void Clear()=>attempts.Clear();
}

public static class AgentPollingPolicy {
    public static bool Defer(bool allActorsHaveWork,bool error,IEnumerable<string> tools)=>allActorsHaveWork&&!error&&tools.All(t=>AgentSchedule.Queueable(t)||t is "plan.submit" or "world.read" or "day.read" or "plan.read" or "inventory.read" or "action.status" or "progress.read" or "progress.roadmap");
}
