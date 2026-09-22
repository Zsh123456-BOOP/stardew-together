using System.Text;
using System.Text.Json.Nodes;
namespace Together;

public sealed record ContextBudgetResult(string Json,int EstimatedTokens,int Limit,string[] Reductions);
public static class ContextBudget {
    // Conservative, explicitly estimated. Usage telemetry calibrates this; it is not a tokenizer.
    public static int Estimate(string text)=> (int)Math.Ceiling(Encoding.UTF8.GetByteCount(text)/2.0);
    private static readonly HashSet<string> Protected=new(new[]{"pending_queries","task_card","recent","goal","now","inventory","ui","night","commitments","protection_reasons","planting_execution","prerequisites","active_actors","decision_reasons","schedule","operating_candidates","farm_work","assets","labor_budget"},StringComparer.Ordinal);
    public static ContextBudgetResult Pack(object input,int limit,int? preferred=null) {
        if(limit<256)throw new ArgumentOutOfRangeException(nameof(limit));
        int target=Math.Clamp(preferred??limit,256,limit);
        var root=System.Text.Json.JsonSerializer.SerializeToNode(input,AgentJson.Options)!.AsObject();
        root.Remove("context_budget");
        DecisionContext.Project(root);
        // Preserve the latest explicit observations and their status/error. Bulky
        // mutation receipts are already archived and current state is supplied separately.
        if(root["recent"] is JsonArray recent)foreach(var item in recent.OfType<JsonObject>()) {
            var data=item["data"] as JsonObject??item;
            string tool=data["tool"]?.GetValue<string>()??"";

            if(data["result"] is JsonObject result) {
                foreach(string field in new[]{"alternatives","capacity_recovery","evidence","before","after"})result.Remove(field);
                if(Estimate(result.ToJsonString(AgentJson.Options))>500)data["result"]=System.Text.Json.JsonSerializer.SerializeToNode(ObservationContract.Summary(System.Text.Json.JsonSerializer.SerializeToElement(result),500));
                data["details_available_via"]=ObservationContract.IsRead(tool)?tool:"action.status with command_id, or memory.search";
            }
        }

        // Old saved/requested observations can predate the bounded outbox contract.
        if(root["pending_queries"] is JsonArray pending)foreach(var entry in pending.OfType<JsonObject>())foreach(string field in new[]{"result","Result"})if(entry[field] is {} value&&Estimate(value.ToJsonString(AgentJson.Options))>700) {
            string kind=entry["kind"]?.GetValue<string>()??entry["Kind"]?.GetValue<string>()??"query";
            entry[field]=System.Text.Json.JsonSerializer.SerializeToNode(ObservationContract.Observation(System.Text.Json.JsonSerializer.SerializeToElement(value),kind));
            entry["read_via"]="query.read";entry["summary_only"]=true;
        }
        var removed=new List<string>();
        int Size()=>Estimate(root.ToJsonString(AgentJson.Options));
        // These are duplicates of the same native inventory, not historical facts.
        if(root["now"] is JsonObject now&&root["inventory"]!=null)now.Remove("inventory");
        foreach(string key in new[]{"farm_cleanup","service_hours","inventory_plan","companions","progression","business","day","schedule","earlier_observation_summaries","recent","memory","goals","operating_candidates","sleep_review","plan"}) {
            if(Size()<=target-256)break;
            if(root[key]==null||Protected.Contains(key))continue;
            var original=root[key];
            if(original is JsonArray array) {
                while(array.Count>2&&Size()>target-256){array.RemoveAt(0);removed.Add(key+":older_rows");}
            }
            if(Size()<=target-256)break;
            // Never silently hide active tasks, failure conditions, or inventory constraints.
            var summary=new JsonObject{["omitted"]=true,["read_via"]="context.read",["read_args"]=new JsonObject{["section"]=key}};
            if(original is JsonObject obj)foreach(string field in new[]{"active_tasks","unfinished","promises","recent_failure_rules","service_constraints","capacity_constraints","capacity_version","capacity_release_conditions","free_slots","slot_count","shared_storage_chests","tools","tool_upgrade","tasks","active_quests","daily_activity"})if(obj[field]!=null)summary[field]=JsonNode.Parse(obj[field]!.ToJsonString());
            root[key]=summary;removed.Add(key+":on_demand");
        }
        // Final omission ownership: no pointer is produced until every reduction is done.
        var degraded=removed.Select(x=>x.Split(':')[0]).ToHashSet(StringComparer.Ordinal);
        foreach(var pair in root)if(pair.Value is JsonObject o&&(o.ContainsKey("omitted")||o.ContainsKey("details_available_via")||o.Any(p=>p.Key.EndsWith("_omitted"))))degraded.Add(pair.Key);
        if(root["recent"] is JsonArray observations)foreach(var entry in observations.OfType<JsonObject>()) {
            var data=entry["data"] as JsonObject??entry;
            if(data["tool"] is JsonValue tool&&tool.TryGetValue<string>(out var name)&&data["result"] is {} result)data["result"]=ContextCompression.ModelToolObservation(name,result,root,degraded);
        }
        root["context_budget"]=new JsonObject{["unit"]="estimated_tokens_utf8_bytes_div_2",["limit"]=limit,["preferred_limit"]=target,["archive_preserved"]=true,["reductions"]=new JsonArray(removed.Distinct().Select(x=>(JsonNode?)JsonValue.Create(x)).ToArray())};
        string json=root.ToJsonString(AgentJson.Options);int total=Estimate(json);
        if(total>limit)throw new InvalidOperationException("context_essential_state_exceeds_budget:"+total+":"+limit);
        return new(json,total,limit,removed.Distinct().ToArray());
    }
}
