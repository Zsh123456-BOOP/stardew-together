using System.Text.Json;
namespace Together;
// A queued native command is not a completed prerequisite for observations.
public static class DecisionBarrier {
    public static string? Text(JsonElement receipt,string name)=>receipt.ValueKind==JsonValueKind.Object&&receipt.TryGetProperty(name,out var value)&&value.ValueKind==JsonValueKind.String?value.GetString():null;
    public static string State(IEnumerable<string?> states) {
        var values=states.ToArray();
        if(values.Any(s=>s is not ("queued" or "running" or "succeeded")))return "failed";
        return values.All(s=>s=="succeeded")?"ready":"waiting";
    }
    public static bool Control(string tool)=>tool is "agent.pause" or "action.cancel" or "action.status" or "plan.cancel";
}
