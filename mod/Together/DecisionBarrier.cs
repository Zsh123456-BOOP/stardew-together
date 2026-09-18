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
    // Only self-contained operations or observations can survive a failed earlier call.
    // Raw local interactions/buy/place keep their prerequisite; explicit plans own after edges.
    public static bool CanFollowFailure(string tool)=>Control(tool)||tool.EndsWith(".read")||tool.StartsWith("knowledge.")||tool.StartsWith("tools.")||tool is "plan.submit" or "farm.plan" or "progress.roadmap" or "goal.requirements" or "work.run" or "player.travel" or "player.procure" or "player.ship_items" or "player.social";
    public static bool Control(string tool)=>tool is "agent.pause" or "action.cancel" or "action.status" or "plan.cancel";
}
