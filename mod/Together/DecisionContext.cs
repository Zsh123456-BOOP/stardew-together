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
        var nativeResults=new Dictionary<string,string>();
        if(root["native_tool_exchange"] is JsonArray messages)foreach(var message in messages.OfType<JsonObject>().Where(m=>Text(m["role"])=="tool")) {
            if(JsonNode.Parse(Text(message["content"])) is JsonObject receipt&&receipt["result"]!=null)nativeResults[Text(receipt["result_id"])]=Text(message["tool_call_id"]);
        }
        root.Remove("native_tool_exchange");
        if(root["now"]==null)return; // Non-game prompts and legacy unit contracts are unchanged.
        if(root["day"] is JsonObject day)foreach(string key in day.Select(p=>p.Key).ToArray())if(key is not ("day" or "time" or "location" or "budget" or "chores" or "routine" or "priorities" or "resource_targets" or "today_completed_batches"))day.Remove(key);
        if(root["service_hours"] is JsonArray services)foreach(var service in services.OfType<JsonObject>())foreach(string key in service.Select(p=>p.Key).ToArray())if(key is not ("location" or "reason" or "opens" or "closes" or "can_enter_now" or "closed_today" or "recheck_at"))service.Remove(key);
        if(root["goals"] is JsonObject currentGoals)currentGoals.Remove("note");
        var unchanged=new Dictionary<string,string>();
        if(root["pending_queries"] is JsonArray queries)foreach(var q in queries.OfType<JsonObject>()) {
            if(nativeResults.TryGetValue(Text(q["result_id"]),out string? callId)){q["result"]=new JsonObject{["delivered_in"]="tool_message",["tool_call_id"]=callId};continue;}
            string section=Text(q["args"]?["section"]);
            if(Text(q["tool"])=="context.read"&&section is "assets" or "labor_budget" or "operating_candidates" or "prerequisites"&&root[section] is {} current&&q["result"] is {} result&&current.ToJsonString()==result.ToJsonString())
                q["result"]=new JsonObject{["status"]="same_as_current",["current_field"]=section};
            if(Text(q["kind"])=="query"&&q["result"] is {} snapshot) {
                string key=Text(q["tool"])+":"+q["args"]?.ToJsonString()+":"+snapshot.ToJsonString();
                if(unchanged.TryGetValue(key,out var prior))q["result"]=new JsonObject{["status"]="unchanged_read",["same_as_result_id"]=prior};
                else unchanged[key]=Text(q["result_id"]);
            }
        }
        root["view_contract"]=new JsonObject{["schema"]=1,["facts"]="now/inventory/schedule为当前状态；pending_queries为带时间的事件摘要，不能覆盖当前状态。原文按result_id用query.read；历史计划不是当前队列。",["history"]="daily_activity为已发生事件，不累加成当前库存；memory细节通过context.read section=memory补读。"};
        root.Remove("equipped_tools");
        if(root["task_card"] is JsonObject card&&root["goal"]!=null)card.Remove("Goal");
        if(root["schedule"] is JsonObject schedule&&schedule["tasks"] is JsonArray) {
            schedule.Remove("recent_results");schedule.Remove("note");schedule["history_read_via"]="plan.read";
            if(root["task_card"] is JsonObject tasks){tasks.Remove("active");tasks["active_field"]="schedule.tasks";}
        }
        // Narrative plans and old query snapshots are the common stale-state trap.
        root.Remove("plan"); // Optional history stays available, not a mandatory unread task.
        if(root["farm_cleanup"] is JsonObject cleanup&&cleanup["areas"] is JsonArray areas) {
            int Number(JsonObject o,string key)=>o[key] is JsonValue v&&v.TryGetValue<int>(out var n)?n:0;
            root["farm_work"]=new JsonObject{["location"]="Farm",["revision"]=Copy(cleanup["revision"]),["resources"]=JsonSerializer.SerializeToNode(areas.OfType<JsonObject>().GroupBy(r=>Text(r["kind"])).Select(g=>new{kind=g.Key,total=g.Sum(r=>Number(r,"count")),routine_allowed=g.Sum(r=>Number(r,"routine_allowed"))})),["orders"]=Copy(cleanup["orders"]),["note"]="农场统计不是当前位置；跨图劳动明确location。树木/保留区仍按工具契约核验。"};
        }
        foreach(string key in new[]{"farm_cleanup","companions"})Readback(root,key);
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
                foreach(string field in new[]{"today","previous"})if(diary[field] is JsonArray rows)foreach(var row in rows.OfType<JsonObject>()){row.Remove("evidence");row.Remove("supporting_evidence");}
            }
        }
        if(root["progression"] is JsonObject progress) {
            progress.Remove("changes");
            if(progress["active_quests"] is JsonArray quests)foreach(var quest in quests.OfType<JsonObject>())quest.Remove("description");
        }
        // Historical snapshots may contain whole not-found knowledge responses.
        if(root["prerequisites"] is JsonObject prerequisites&&prerequisites["development"] is JsonArray development)foreach(var row in development.OfType<JsonObject>()) {
            if(row["knowledge"] is JsonObject knowledge&&Text(knowledge["Status"])=="not_found") {row.Remove("knowledge");row["knowledge_status"]="not_found_in_current_scope";}
            row.Remove("note");
            if(row["known"]?.ToString()=="true")row.Remove("Unlock");
            if(Text(row["UnsupportedReason"])=="")row.Remove("UnsupportedReason");
            row.Remove("dependencies"); // The common read instruction below supplies the same endpoint.
        }
        if(root["prerequisites"] is JsonObject prereq)prereq["dependency_read"]="progress.dependencies id=<development.id>；资料不代表已解锁";
        if(root["goals"] is JsonObject goals&&goals["goals"] is JsonArray active)foreach(var goal in active.OfType<JsonObject>()) {
            goal.Remove("history");
            if(goal["steps"] is JsonArray steps)foreach(var step in steps.OfType<JsonObject>())foreach(string field in new[]{"Source","Owner","DependsOn"})step.Remove(field);
        }
        OnDemandContext.Project(root);
    }
    private static HashSet<string> pendingIds(JsonObject root)=>(root["pending_queries"] as JsonArray)?.OfType<JsonObject>().Select(q=>Text(q["result_id"])).Where(id=>id.Length>0).ToHashSet()??new();
}
