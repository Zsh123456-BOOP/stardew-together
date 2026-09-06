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
            Limit(root["day"] as JsonObject,"options",5);
            Limit(root["inventory_plan"] as JsonObject,"storable",4);
            Limit(root["farm_cleanup"] as JsonObject,"areas",8);
            Limit(root["schedule"] as JsonObject,"recent_results",3);
            if(root["recent"] is JsonArray recent) {
                bool Error(JsonNode? n) {
                    if(n is JsonObject o) {
                        if(o["error"] is JsonValue err&&err.TryGetValue<string>(out var message)&&!string.IsNullOrEmpty(message))return true;
                        if(o["status"] is JsonValue state&&state.TryGetValue<string>(out var status)&&status is "failed" or "blocked")return true;
                        return o.Any(p=>Error(p.Value));
                    }
                    if(n is JsonArray a)return a.Any(Error);
                    if(n is JsonValue v&&v.TryGetValue<string>(out var text)&&text.StartsWith("{"))try{return Error(JsonNode.Parse(text));}catch(JsonException){}
                    return false;
                }
                while(recent.Count>2&&Size()>maxCharacters) {
                    int disposable=Enumerable.Range(0,recent.Count-2).FirstOrDefault(i=>!Error(recent[i]),-1);
                    if(disposable<0)break;
                    recent.RemoveAt(disposable);reductions.Add("older_non_error_events");
                }
            }
        }
        JsonNode? Copy(JsonNode? node)=>node is null?null:JsonNode.Parse(node.ToJsonString());
        if(Size()>maxCharacters) {
            // Keep the two newest tool observations complete for chained calls.
            // Older evidence remains durable in memory, not repeated raw payloads.
            if(root["recent"] is JsonArray history) {
                var earlier=new JsonArray();
                while(history.Count>2) {
                    var entry=history[0];history.RemoveAt(0);
                    JsonNode? detail=entry;
                    if(entry is JsonObject e&&e["Text"] is JsonValue text&&text.TryGetValue<string>(out var raw))try{detail=JsonNode.Parse(raw);}catch(JsonException){}
                    if(detail is JsonObject obj) {
                        var result=obj["result"] as JsonObject??obj;
                        earlier.Add(new JsonObject{["kind"]=Copy(entry is JsonObject item?item["Kind"]:null),["tool"]=Copy(obj["tool"]),["status"]=Copy(result["status"]),["error"]=Copy(result["error"]),["command_id"]=Copy(result["command_id"]),["task_id"]=Copy(result["task_id"])});
                    }
                }
                root["earlier_observation_summaries"]=earlier;reductions.Add("older_events_retrievable_from_memory");
            }
            foreach(string key in new[]{"farm_cleanup","inventory_plan","progression"}) {
                if(Size()<=maxCharacters)break;
                if(root[key] is not null){root[key]=new JsonObject{["details_available_via"]=key=="farm_cleanup"?"farm.cleanup":key=="inventory_plan"?"world.read":"progress.read"};reductions.Add(key+"_on_demand");}
            }
        }
        root["context_budget"]=JsonSerializer.SerializeToNode(new{unit="characters_not_tokens",soft_limit=maxCharacters,omitted=reductions.Distinct().ToArray(),over_budget=Size()>maxCharacters,
            details="省略表示摘要，不代表没有目标；完整目标用progress.catalog，行动用action.status，历史用memory.search。当前任务/承诺/错误不做字符截断。"});
        return root.ToJsonString(AgentJson.Options);
    }
}
