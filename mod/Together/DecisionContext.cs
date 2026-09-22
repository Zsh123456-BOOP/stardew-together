using System.Text.Json;
using System.Text.Json.Nodes;
namespace Together;

// This is a read projection, never the saved event log. ContextBudget owns the
// single projection/packing pass; callers retain the frozen source for auditing.
public static class DecisionContext {
    private static JsonNode? Copy(JsonNode? n)=>n==null?null:JsonNode.Parse(n.ToJsonString());
    private static string Text(JsonNode? n)=>n is JsonValue v&&v.TryGetValue<string>(out var s)?s:"";
    private static void Readback(JsonObject root,string key) {if(root[key]!=null)root[key]=new JsonObject{["read_via"]="context.read",["read_args"]=new JsonObject{["section"]=key}};}
    public static void Project(JsonObject root) {
        if(root["now"]==null)return; // Non-game prompts and legacy unit contracts are unchanged.
        root["view_contract"]=new JsonObject{["schema"]=1,["facts"]="now/inventory/schedule为当前状态；pending_queries为带时间的事件摘要，不能覆盖当前状态。原文按result_id用query.read；历史计划不是当前队列。",["history"]="daily_activity为已发生事件，不累加成当前库存；memory细节通过context.read section=memory补读。"};
        root.Remove("equipped_tools");
        if(root["task_card"] is JsonObject card&&root["goal"]!=null)card.Remove("Goal");
        if(root["schedule"] is JsonObject schedule&&schedule["tasks"] is JsonArray) {
            schedule.Remove("recent_results");schedule.Remove("note");schedule["history_read_via"]="plan.read";
            if(root["task_card"] is JsonObject tasks){tasks.Remove("active");tasks["active_field"]="schedule.tasks";}
        }
        // Narrative plans and old query snapshots are the common stale-state trap.
        Readback(root,"plan");
        foreach(string key in new[]{"farm_cleanup","companions","service_hours"})Readback(root,key);
        if(root["pending_queries"] is JsonArray pending)foreach(var q in pending.OfType<JsonObject>()) {
            q.Remove("note");q.Remove("delivery");q.Remove("details");
            string tool=Text(q["tool"]),kind=Text(q["kind"]),section=Text(q["args"]?["section"]);
            string covered=tool switch {"inventory.read"=>"inventory","plan.read"=>"schedule","context.read" when section is "plan" or "schedule"=>"schedule",_=>""};
            bool available=covered=="schedule"?root["schedule"]?["tasks"] is JsonArray:covered=="inventory"&&root["inventory"]?["items"] is JsonArray;
            if(kind=="query"&&available)q["result"]=new JsonObject{["status"]="superseded_snapshot",["current_field"]=covered};
            if(tool=="tools.lookup"&&q["result"] is JsonObject lookup&&lookup["definitions"] is JsonObject definitions)
                q["result"]=new JsonObject{["equipped_names"]=new JsonArray(definitions.Select(p=>(JsonNode?)JsonValue.Create(p.Key)).ToArray()),["definitions_in"]= "system_tools",["unknown"]=Copy(lookup["unknown"])};
            if(kind=="outcome"&&q["result"] is JsonObject outcome)outcome.Remove("full_receipt_available");
        }
        if(root["recent"] is JsonArray recent) {
            var delivered=pendingIds(root);
            for(int i=recent.Count-1;i>=0;i--)if(recent[i] is JsonObject entry) {
                var data=entry["data"] as JsonObject??entry;
                if(delivered.Contains(Text(data["result_id"]))||Text(data["tool"]) is "context.read" or "plan.read" or "inventory.read" or "tools.lookup")recent.RemoveAt(i);
            }
        }
        if(root["memory"] is JsonObject memory) {
            foreach(string field in new[]{"reflections","relevant","days","seasons","note"})memory.Remove(field);
            memory["read_via"]="context.read section=memory";
            if(memory["recent_failure_rules"] is JsonArray failures) {
                foreach(var row in failures.OfType<JsonObject>())row.Remove("invalidates");
                if(failures.Count>0)memory["failure_release"]="仅同一主体和相关条件约束重试；换措辞、数量或日期不解除。不同goal/item/location不能合并成全局禁令。";
            }
            if(memory["daily_activity"] is JsonObject diary) {
                diary.Remove("today_maintenance");
                foreach(string field in new[]{"today","previous"})if(diary[field] is JsonArray rows)foreach(var row in rows.OfType<JsonObject>()){row.Remove("evidence");row.Remove("batches");}
            }
        }
        if(root["progression"] is JsonObject progress&&progress["active_quests"] is JsonArray quests)foreach(var quest in quests.OfType<JsonObject>())quest.Remove("description");
        // Historical snapshots may contain whole not-found knowledge responses.
        if(root["prerequisites"] is JsonObject prerequisites&&prerequisites["development"] is JsonArray development)foreach(var row in development.OfType<JsonObject>()) {
            if(row["knowledge"] is JsonObject knowledge&&Text(knowledge["Status"])=="not_found") {row.Remove("knowledge");row["knowledge_status"]="not_found_in_current_scope";}
            row.Remove("note");
        }
    }
    private static HashSet<string> pendingIds(JsonObject root)=>(root["pending_queries"] as JsonArray)?.OfType<JsonObject>().Select(q=>Text(q["result_id"])).Where(id=>id.Length>0).ToHashSet()??new();
}
