using System.Text.Json.Nodes;
namespace Together;

public static class OnDemandContext {
    private static JsonNode? Copy(JsonNode? n)=>n==null?null:JsonNode.Parse(n.ToJsonString());
    private static string Text(JsonNode? n)=>n is JsonValue v&&v.TryGetValue<string>(out var s)?s:"";
    private static void Keep(JsonObject o,params string[] fields){foreach(string key in o.Select(p=>p.Key).ToArray())if(!fields.Contains(key))o.Remove(key);}
    public static void Project(JsonObject root) {
        if(root["now"] is not JsonObject now)return;
        bool trading=Text(now["location"]).Contains("Shop",StringComparison.OrdinalIgnoreCase)||Text(root["ui"]?["type"])=="ShopMenu";
        var destinations=new HashSet<string>{Text(now["location"])};
        if(root["schedule"]?["tasks"] is JsonArray tasks)foreach(var task in tasks.OfType<JsonObject>()) {
            string tool=Text(task["spec"]?["tool"]??task["tool"]);var args=task["spec"]?["args"]??task["args"];
            if(tool is "player.procure" or "player.buy" or "farm.select_seeds")trading=true;
            foreach(var dest in new[]{task["spec"]?["location"],task["location"],args?["location"]})if(Text(dest).Length>0)destinations.Add(Text(dest));
        }
        var facts=new JsonObject();
        if(root["prerequisites"] is JsonObject prereq) {
            if(prereq["development"] is JsonArray dev)foreach(var row in dev.OfType<JsonObject>()) {
                string id=Text(row["id"]);if(id.Length>0)facts[id]=Copy(row);
            }
            foreach(string field in new[]{"quests","achievements","buildings","instruction","development","observed_day","observed_time","discovery"})prereq.Remove(field);
            prereq["read_via"]="progress.catalog；详情progress.dependencies id";
        }
        if(root["operating_candidates"] is JsonArray candidates)foreach(var row in candidates.OfType<JsonObject>()) {
            var args=row["Args"]??row["args"];string destination=Text(args?["location"]);if(destination.Length>0)destinations.Add(destination);
            string entity=Text(args?["entity"]??args?["recipe"]);
            if(row["Requirements"] is JsonObject req) {
                if(entity.Length>0&&req["materials"]!=null) {
                    if(facts[entity]==null)facts[entity]=Copy(req);
                    row["Requirements"]=new JsonObject{["fact_id"]=entity};
                } else {
                    if(!trading&&req["quotes"] is JsonObject quotes&&quotes["offers"] is JsonArray offers)req["quotes"]=new JsonObject{["source"]=Copy(quotes["source"]),["offer_count"]=offers.Count,["read_via"]="context.read section=operating_candidates；采购前shop.read核价"};
                    if(req["land"] is JsonObject land)land.Remove("note");
                    req.Remove("next");
                }
            }
        }
        // Current goal facts have one owner. Candidates refer to that owner by entity ID.
        if(root["prerequisites"] is JsonObject p)p["facts"]=facts;
        else if(facts.Count>0)root["prerequisites"]=new JsonObject{["facts"]=facts};
        if(root["service_hours"] is JsonArray services) {
            var relevant=services.OfType<JsonObject>().Where(s=>destinations.Contains(Text(s["location"]))).Select(s=>Copy(s)).ToArray();
            root["service_hours"]=new JsonArray(relevant);
        }
        if(root["day"] is JsonObject day){day.Remove("day");day.Remove("time");day.Remove("location");if(day["budget"] is JsonObject budget){budget.Remove("stamina");budget.Remove("estimate_note");}if(day["routine"] is JsonObject routine&&routine["Enabled"]?.ToString()=="false")day["routine"]=new JsonObject{["Enabled"]=false};}
        if(root["task_card"] is JsonObject card)Keep(card,"waiting_query_drafts","ProfessionChoices","active_field");
        if(root["assets"] is JsonObject assets){assets.Remove("observed_day");assets.Remove("observed_time");assets.Remove("rule");}
        if(root["planting_execution"] is JsonObject plant)plant.Remove("evidence");
        if(root["progression"] is JsonObject progress){progress.Remove("social");progress.Remove("date");progress.Remove("day");progress.Remove("note");progress.Remove("details");}
        if(root["memory"] is JsonObject memory) {
            memory.Remove("unfinished"); // Actual goals and task queue are supplied separately.
            if(memory["daily_activity"] is JsonObject diary) {
                diary.Remove("note");diary.Remove("previous");diary.Remove("previous_day");diary.Remove("today_maintenance");
                if(diary["today"] is JsonArray rows)foreach(var row in rows.OfType<JsonObject>()) {
                    Keep(row,"activity","item","location","count","cost","batches");
                    if(row["cost"]?.ToString()=="0")row.Remove("cost");if(row["batches"]?.ToString()=="1")row.Remove("batches");
                }
            }
        }
        root["view_contract"]=new JsonObject{["schema"]=2,["facts"]="当前now/inventory/schedule优先；候选fact_id见prerequisites.facts。候选Id不是plan_id，按Tool/Args调用，plan_id仅来自farm.plan回执。空列表表示本视图无记录，其他目录可查询。",["history"]="daily_activity是当天累计历史，不累加为库存；完整历史context.read section=memory。未列商店用services.read；未选成就/配方用progress.catalog。"};
    }
}
