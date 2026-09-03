using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace Together;
public sealed class FailureExperience {
    public string Key {get;set;}="";
    public string Actor {get;set;}="";
    public string Tool {get;set;}="";
    public string Reason {get;set;}="";
    public string Conditions {get;set;}="";
    public string TaskEvidence {get;set;}="";
    public int Day {get;set;}
    public int RetryAfterMinute {get;set;}
    public int Attempts {get;set;}
}
public sealed class FailureKnowledge {
    public List<FailureExperience> Entries {get;set;}=new();
    public static string Hash(string text)=>Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(text)));
    public static string Key(string actor,string tool,string args,string locationPrecondition="") {
        // Rephrasing the bedtime justification does not create a new action.
        if(tool=="player.sleep")args="{}";
        else try {
            using var document=JsonDocument.Parse(args);
            object? Canonical(JsonElement v)=>v.ValueKind switch {
                JsonValueKind.Object=>v.EnumerateObject().OrderBy(p=>p.Name,StringComparer.Ordinal).ToDictionary(p=>p.Name,p=>Canonical(p.Value)),
                JsonValueKind.Array=>v.EnumerateArray().Select(Canonical).ToArray(),
                _=>v.Clone()
            };
            args=JsonSerializer.Serialize(Canonical(document.RootElement));
        }catch(JsonException){}
        return Hash(actor+"\n"+tool+"\n"+args+"\n"+locationPrecondition);
    }
    public static string SelectionKey(string actor,string tool,string args,string location="") {
        if(tool!="work.run")return Key(actor,tool,args,location);
        try {
            using var d=JsonDocument.Parse(args);
            var values=d.RootElement.EnumerateObject().Where(p=>p.Name is not ("count" or "stock_target")).ToDictionary(p=>p.Name,p=>p.Value.Clone());
            return Key(actor,tool,JsonSerializer.Serialize(values),location);
        }catch(JsonException){return Key(actor,tool,args,location);}
    }
    public FailureExperience? Block(string key,string conditions,int day,int minute)=>Entries.LastOrDefault(e=>e.Key==key&&e.Conditions==conditions&&e.Day==day&&minute<e.RetryAfterMinute);
    public void Record(string key,string actor,string tool,string reason,string conditions,string task,int day,int minute) {
        var last=Entries.LastOrDefault(e=>e.Key==key&&e.Conditions==conditions&&e.Day==day);
        int attempts=(last?.Attempts??0)+1;Entries.RemoveAll(e=>e.Key==key);
        Entries.Add(new(){Key=key,Actor=actor,Tool=tool,Reason=reason,Conditions=conditions,TaskEvidence=task,Day=day,RetryAfterMinute=minute+Math.Min(120,20*attempts),Attempts=attempts});
        if(Entries.Count>96)Entries.RemoveRange(0,Entries.Count-96);
    }
    public void Success(string key)=>Entries.RemoveAll(e=>e.Key==key);
}
