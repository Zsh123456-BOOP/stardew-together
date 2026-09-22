using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
namespace Together;

public static class ContextRevisions {
    public static string Canonical(JsonElement value) {
        JsonNode? Sort(JsonElement e)=>e.ValueKind switch {
            JsonValueKind.Object=>new JsonObject(e.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).Select(p=>new KeyValuePair<string,JsonNode?>(p.Name,Sort(p.Value)))),
            JsonValueKind.Array=>new JsonArray(e.EnumerateArray().Select(Sort).ToArray()),_=>JsonNode.Parse(e.GetRawText())
        };
        return Sort(value)?.ToJsonString(AgentJson.Options)??"null";
    }
    public static string Version(JsonElement value)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(Canonical(value))))[..16];
    public static Dictionary<string,string> Fields(JsonElement source)=>source.EnumerateObject().ToDictionary(p=>p.Name,p=>Version(p.Value));
}

// A content-versioned memo, not a claim that an old observation is still current.
// Callers first re-read the relevant native domain. Every hit still returns the full fact.
public sealed class QueryFactMemo {
    private readonly Dictionary<string,(string Version,JsonElement Fact)> facts=new();
    public (JsonElement Fact,string Revision,bool Repeated) Observe(string tool,JsonElement args,JsonElement fresh) {
        string key=tool+":"+ContextRevisions.Canonical(args),version=ContextRevisions.Version(fresh);
        if(facts.TryGetValue(key,out var old)&&old.Version==version)return(old.Fact.Clone(),version,true);
        if(facts.Count>=64&&!facts.ContainsKey(key))facts.Remove(facts.Keys.First());
        facts[key]=(version,fresh.Clone());return(fresh.Clone(),version,false);
    }
}
