using System.Text.Json;

namespace Together;

public sealed class ServiceConstraint {
    public string Subject {get;set;}="";
    public string Reason {get;set;}="";
    public string Condition {get;set;}="";
    public string Evidence {get;set;}="";
    public int Day {get;set;}
    public int RetryTime {get;set;}
}
public sealed class OperationsState {
    public int SchemaVersion {get;set;}=1;
    public List<ServiceConstraint> Constraints {get;set;}=new();
    public Dictionary<string,string> LastOutcomes {get;set;}=new();
    public void Observe(ServiceConstraint fact) {
        Constraints.RemoveAll(c=>c.Subject==fact.Subject);
        Constraints.Add(fact);
        if(Constraints.Count>64)Constraints.RemoveRange(0,Constraints.Count-64);
    }
    public ServiceConstraint? Blocking(string subject,string condition,int day,int time)=>Constraints.LastOrDefault(c=>c.Subject==subject&&c.Condition==condition&&c.Day==day&&time<c.RetryTime);
}
public sealed record WorkOutcome(string Disposition,string StopReason,bool BusinessProgress,int Completed,int Gained,int Deposited,int Remaining);
public static class OperationsPolicy {
    public static WorkOutcome Outcome(string tool,JsonElement result) {
        int Count(string key)=>result.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out var n)?n:0;
        string Text(string key)=>result.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
        string status=Text("status"),reason=Text("stop_reason");if(reason.Length==0)reason=Text("error");
        bool changed=new[]{"completed","gained","deposited","refills"}.Any(k=>Count(k)>0);
        bool navigation=tool is "player.move" or "player.travel" or "player.service" or "player.interact";
        bool deferred=result.TryGetProperty("deferred",out var d)&&d.ValueKind==JsonValueKind.True;
        string disposition=deferred?"deferred":status=="succeeded"?changed?"completed":"already_satisfied_or_observed":RecoveryPolicy.CanWait(reason)?changed?"partial":"waiting_condition":status=="cancelled"?"cancelled":"failed";
        bool progress=!navigation&&!deferred&&(changed||status=="succeeded"&&tool is "player.sleep" or "player.read_mail" or "player.collect_reward");
        return new(disposition,reason,progress,Count("completed"),Count("gained"),Count("deposited"),Math.Max(0,Count("requested")-Math.Max(Count("completed"),Count("gained"))));
    }
    public static int RequiredFreeSlots(string goal,int requested=0)=>CapacityPlan.RequiredSlots(goal,requested);
    public static bool CapacityReady(int free,int required)=>CapacityPlan.FreeFits(free,required);
}

public static class WorkCapabilities {
    public static readonly string[] CompanionGoals={"cleanup","water","harvest","forage","wood","stone","fiber","resource","store","pet","feed","tend","collect","process"};
    public static string? Validate(string actor,string goal)=>actor!="player"&&!CompanionGoals.Contains(goal)
        ?"work_goal_unavailable:actor="+actor+":goal="+goal+":supported="+string.Join(",",CompanionGoals)+";assign_player_for_fish_or_plant":null;
}
