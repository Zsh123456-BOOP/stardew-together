using System.Text.Json;
namespace Together;

// Derived search documents. Raw archive entries remain the authority and survive eviction.
public sealed class MemoryDocument {
    public int ProjectionVersion {get;set;}
    public string Evidence {get;set;}="";
    public string Actor {get;set;}="";
    public int Generation {get;set;}
    public int Day {get;set;}
    public string Kind {get;set;}="";
    public string Tool {get;set;}="";
    public string Status {get;set;}="";
    public string Reason {get;set;}="";
    public string[] Entities {get;set;}=Array.Empty<string>();
    public string Summary {get;set;}="";
}
public static class MemoryProjector {
    public static MemoryDocument? Project(ArchivedMemory entry) {
        if(entry.Kind is not ("action_result" or "tool_result" or "profession_choice" or "experience" or "daily_activity"))return null;
        try {
            using var doc=JsonDocument.Parse(entry.Text);var root=doc.RootElement;
            if(root.ValueKind!=JsonValueKind.Object)return null;
            var result=root.TryGetProperty("result",out var r)&&r.ValueKind==JsonValueKind.Object?r:root;
            string Text(JsonElement e,string key)=>e.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
            string tool=Text(root,"tool");if(tool.Length==0)tool=Text(result,"skill");
            // Repeated read-only world snapshots are observations, not durable lessons.
            if(entry.Kind=="tool_result"&&(tool.EndsWith(".read")||tool.StartsWith("knowledge.")||tool.StartsWith("memory.")||tool=="action.status"))return null;
            var entities=new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            void Targets(JsonElement value) {
                if(value.ValueKind!=JsonValueKind.Object)return;
                foreach(string key in new[]{"npc","item","entity","recipe","location","goal","target","quest_id"})if(value.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.String&&v.GetString() is {Length:>0} entity)entities.Add(entity);
            }
            Targets(result);if(root.TryGetProperty("args",out var args))Targets(args);
            if(result.TryGetProperty("effects",out var effects)&&effects.ValueKind==JsonValueKind.Array)foreach(var effect in effects.EnumerateArray())if(effect.ValueKind==JsonValueKind.Object&&Text(effect,"kind").StartsWith("native_"))Targets(effect);
            var fields=new Dictionary<string,JsonElement>();
            foreach(string key in new[]{"status","error","stop_reason","disposition","resume_policy","completed","gained","deposited","requested","skill","goal","location","profession","level"})if(result.TryGetProperty(key,out var v))fields[key]=v.Clone();
            string summary=AgentJson.Encode(fields);
            return new(){ProjectionVersion=2,Evidence=entry.Id,Actor=entry.Actor,Generation=entry.ActorGeneration,Day=entry.Day,Kind=entry.Kind,Tool=tool,Status=Text(result,"status"),Reason=Text(result,"error") is {Length:>0} error?error:Text(result,"stop_reason"),Entities=entities.Take(16).ToArray(),Summary=summary.Length<=1800?summary:AgentJson.Encode(new{tool,status=Text(result,"status"),detail="memory.evidence",entry.Id})};
        }catch(JsonException){return null;}
    }
}
public static class MemoryRetriever {
    public static IEnumerable<(MemoryDocument Document,int Score)> Rank(IEnumerable<MemoryDocument> documents,string query,int day) {
        var terms=MemoryRecall.SearchTerms(query).Concat(query.Split(new[]{' ',':','/',',','，'},StringSplitOptions.RemoveEmptyEntries)).Where(t=>t.Length>1).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        return documents.Where(d=>d.Day<=day).Select(d=>{
            int exact=d.Entities.Count(e=>terms.Contains(e,StringComparer.OrdinalIgnoreCase));
            int related=terms.Count(t=>d.Entities.Any(e=>e.Contains(t,StringComparison.OrdinalIgnoreCase))||d.Summary.Contains(t,StringComparison.OrdinalIgnoreCase)||d.Tool.Contains(t,StringComparison.OrdinalIgnoreCase));
            return (Document:d,Score:exact*200+related*30+Math.Max(0,14-(day-d.Day)));
        }).Where(x=>query.Length==0||x.Score>=30).OrderByDescending(x=>x.Score).ThenByDescending(x=>x.Document.Day);
    }
}
