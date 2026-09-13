using System.Text.Json;
using System.Text.Json.Nodes;

namespace Together;
public static class ContextCompression {
    public static string Pack(object value,int maxCharacters=40000) {
        var root=JsonSerializer.SerializeToNode(value,AgentJson.Options)!.AsObject();
        var reductions=new List<string>();
        var degraded=new HashSet<string>(StringComparer.Ordinal);
        int cachedSize=-1;
        bool QueryObservation(JsonNode? entry) {
            var data=entry is JsonObject obj?(obj["data"] as JsonObject??obj):null;
            return data?["tool"] is JsonValue tool&&tool.TryGetValue<string>(out var name)&&name is "world.read" or "day.read" or "day.plan" or "plan.read" or "progress.roadmap";
        }
        void Limit(JsonObject? parent,string key,int keep) {
            if(parent?[key] is not JsonArray rows||rows.Count<=keep)return;
            int total=rows.Count;while(rows.Count>keep)rows.RemoveAt(rows.Count-1);
            parent[key+"_omitted"]=total-keep;reductions.Add(key);degraded.Add(key);cachedSize=-1;
        }
        int Size()=>cachedSize>=0?cachedSize:cachedSize=root.ToJsonString(AgentJson.Options).Length;
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
                    int disposable=Enumerable.Range(0,recent.Count-2).FirstOrDefault(i=>!Error(recent[i])&&!QueryObservation(recent[i]),-1);
                    if(disposable<0)break;
                    cachedSize=Size()-(recent[disposable]?.ToJsonString(AgentJson.Options).Length??4)-1;
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
                    int index=Enumerable.Range(0,history.Count-2).FirstOrDefault(i=>!QueryObservation(history[i]),-1);
                    if(index<0)break;
                    var entry=history[index];history.RemoveAt(index);
                    JsonNode? detail=entry;
                    if(entry is JsonObject e&&e["Text"] is JsonValue text&&text.TryGetValue<string>(out var raw))try{detail=JsonNode.Parse(raw);}catch(JsonException){}
                    if(detail is JsonObject obj) {
                        obj=obj["data"] as JsonObject??obj;var result=obj["result"] as JsonObject??obj;
                        earlier.Add(new JsonObject{["kind"]=Copy(entry is JsonObject item?item["Kind"]:null),["tool"]=Copy(obj["tool"]),["status"]=Copy(result["status"]),["error"]=Copy(result["error"]),["command_id"]=Copy(result["command_id"]),["task_id"]=Copy(result["task_id"]),["disposition"]=Copy(result["disposition"]),["resume_policy"]=Copy(result["resume_policy"]),["stop_reason"]=Copy(result["stop_reason"])});
                    }
                }
                root["earlier_observation_summaries"]=earlier;cachedSize=-1;reductions.Add("older_events_retrievable_from_memory");
            }
            foreach(string key in new[]{"farm_cleanup","inventory_plan","progression"}) {
                if(Size()<=maxCharacters)break;
                if(root[key] is JsonObject original){
                    var summary=new JsonObject{["details_available_via"]=key=="farm_cleanup"?"farm.cleanup":key=="inventory_plan"?"world.read":"progress.read"};
                    if(key=="inventory_plan")foreach(string field in CapacityFields)if(original[field] is {} scalar)summary[field]=Copy(scalar);
                    root[key]=summary;degraded.Add(key);reductions.Add(key+"_on_demand");cachedSize=-1;
                }
            }
        }
        // A query can point into this exact packed snapshot only if its complete
        // result is still there. The compression pass owns the only omission set.
        if(root["recent"] is JsonArray observations)foreach(var entry in observations.OfType<JsonObject>()) {
            var data=entry["data"] as JsonObject??entry;
            if(data["tool"] is JsonValue tool&&tool.TryGetValue<string>(out var name)&&data["result"] is {} result)
                data["result"]=ModelToolObservation(name,result,root,degraded);
        }
        cachedSize=-1;
        root["context_budget"]=JsonSerializer.SerializeToNode(new{unit="characters_not_tokens",soft_limit=maxCharacters,omitted=reductions.Distinct().ToArray(),degraded_fields=degraded.OrderBy(x=>x).ToArray(),over_budget=Size()>maxCharacters,
            details="省略表示摘要，不代表没有目标；完整目标用progress.catalog，行动用action.status，历史用memory.search。当前任务/承诺/错误不做字符截断。"});
        return root.ToJsonString(AgentJson.Options);
    }
    private static readonly string[] CapacityFields={"slot_count","free_slots","occupied","player_free_slots","capacity_constraints","capacity_version","capacity_release_conditions"};
    public static JsonNode ModelToolObservation(string tool,JsonNode result,JsonObject packed,IReadOnlySet<string> degraded) {
        string? field=tool switch{"world.read"=>"inventory_plan","day.read" or "day.plan"=>"day","plan.read"=>"schedule","progress.roadmap"=>"progression",_=>null};
        // world.read includes farm/recruitment/full stocks absent from a snapshot.
        bool complete=field!=null&&tool!="world.read"&&!degraded.Contains(field)&&!degraded.Any(d=>packed[field] is JsonObject o&&o.ContainsKey(d+"_omitted"))&&result.ToJsonString()==packed[field]?.ToJsonString();
        return complete?new JsonObject{["status"]="observed",["current_context"]=field}:JsonNode.Parse(result.ToJsonString())!;
    }
}
