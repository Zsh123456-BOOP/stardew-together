using System.Text.Json;
using System.Text.Json.Nodes;

namespace Together;
public static class ContextCompression {
    public static string Pack(object value,int maxCharacters=40000) {
        var root=JsonSerializer.SerializeToNode(value,AgentJson.Options)!.AsObject();
        var reductions=new List<string>();
        void Limit(JsonObject? parent,string key,int keep) {
            if(parent?[key] is not JsonArray rows||rows.Count<=keep)return;
            int total=rows.Count;while(rows.Count>keep)rows.RemoveAt(rows.Count-1);
            parent[key+"_omitted"]=total-keep;reductions.Add(key);
        }
        int Size()=>root.ToJsonString(AgentJson.Options).Length;
        if(Size()>maxCharacters) {
            if(root["companions"] is JsonArray companions)foreach(var actor in companions.OfType<JsonObject>())Limit(actor,"candidates",8);
            Limit(root["progression"] as JsonObject,"missing_achievements",6);
            Limit(root["day"] as JsonObject,"options",8);
            if(root["recent"] is JsonArray recent) {
                bool Error(JsonNode? n) {
                    string text=n?.ToJsonString()??"";
                    return text.Contains("error",StringComparison.OrdinalIgnoreCase)||text.Contains("failed",StringComparison.OrdinalIgnoreCase)||text.Contains("blocked",StringComparison.OrdinalIgnoreCase);
                }
                while(recent.Count>2&&Size()>maxCharacters) {
                    int disposable=Enumerable.Range(0,recent.Count-2).FirstOrDefault(i=>!Error(recent[i]),-1);
                    if(disposable<0)break;
                    recent.RemoveAt(disposable);reductions.Add("older_non_error_events");
                }
            }
        }
        root["context_budget"]=JsonSerializer.SerializeToNode(new{unit="characters_not_tokens",soft_limit=maxCharacters,omitted=reductions.Distinct().ToArray(),over_budget=Size()>maxCharacters,
            details="省略表示摘要，不代表没有目标；完整目标用progress.catalog，行动用action.status，历史用memory.search。当前任务/承诺/错误不做字符截断。"});
        return root.ToJsonString(AgentJson.Options);
    }
}
