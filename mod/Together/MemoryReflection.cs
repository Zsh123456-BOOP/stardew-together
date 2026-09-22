using System.Text.Json;
namespace Together;
public sealed class MemoryReflection {
    public int Day {get;set;}
    public string Summary {get;set;}="";
    public string[] Evidence {get;set;}=Array.Empty<string>();
    public string Authority {get;set;}="model_derived_not_native_state";
    public static MemoryReflection Parse(string json,int day,IReadOnlySet<string> allowed) {
        var turn=AgentTurn.Parse(json);
        if(turn.calls.Count!=1||turn.calls[0].tool!="memory.summary")throw new InvalidOperationException("invalid_memory_summary_envelope");
        var args=turn.calls[0].args;
        string summary=args.GetProperty("summary").GetString()??"";
        var ids=args.GetProperty("evidence").EnumerateArray().Select(e=>e.GetString()??"").Distinct().ToArray();
        if(summary.Length is <1 or >1200||ids.Length is <1 or >12||ids.Any(id=>!allowed.Contains(id)))throw new InvalidOperationException("memory_summary_requires_known_evidence");
        return new(){Day=day,Summary=summary,Evidence=ids};
    }
}
