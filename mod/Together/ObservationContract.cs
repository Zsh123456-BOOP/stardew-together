using System.Text.Json;
using System.Text.Json.Nodes;
namespace Together;

// One explicit contract for observation capture, stale-read handling and discovery.
public static class ObservationContract {
    public static readonly HashSet<string> ReadTools=new(StringComparer.Ordinal){"tools.lookup","beach.read","family.read","agent.status","usage.read","farm.business_status","shop.sources","services.read","progress.dependencies","capabilities.read","memory.search","memory.evidence","map.scan","shop.read","crab_pots.read","fishing.options","transport.read","orchard.read","mastery.read","island.walnuts","volcano.read","island.upgrades","forge.read","arcade.read","joja.read","inventory.read","inventory.capacity","world.read","map.read","farm.economy_status","perfection.read","progress.catalog","plan.read","day.read","progress.read","progress.roadmap","knowledge.search","knowledge.get","goal.requirements","menu.read","action.status","query.read","context.read","quest_board.read","order_donations.read","equipment.read","animals.read","animal_shop.read","construction.read","orders.read"};
    public static bool IsRead(string tool)=>ReadTools.Contains(tool);
    public static bool IsRead(string tool,JsonElement args)=>IsRead(tool)||tool=="farm.business"&&args.ValueKind==JsonValueKind.Object&&!args.EnumerateObject().Any()||tool=="farm.production"&&(!args.TryGetProperty("action",out var action)||action.GetString() is "read" or "uses" or "coverage");
    public static JsonElement Observation(JsonElement value,string kind) {
        if(value.ValueKind!=JsonValueKind.Object)return Summary(value);
        var core=new JsonObject();
        if(kind=="outcome") {
            foreach(string key in new[]{"status","task_id","command_id","intent_id","actor","executed","requested","completed","remaining","gained","deposited","refills","stop_reason","resume_policy","error","reason","subject","opens","closes","deadline","cancellation_requested_by"})
                if(value.TryGetProperty(key,out var v))core[key]=JsonNode.Parse(Summary(v,160).GetRawText());
            core["full_receipt_available"]=true;
            return JsonSerializer.SerializeToElement(core);
        }
        if(kind=="quote"&&value.TryGetProperty("seed_decision",out var decision)) {
            foreach(string key in new[]{"shop","location","currency"})if(value.TryGetProperty(key,out var v))core[key]=JsonNode.Parse(v.GetRawText());
            var seeds=new JsonObject();
            foreach(string key in new[]{"quote_token","budget","keep_gold","fastest_available","algorithm_recommendation","next"})if(decision.TryGetProperty(key,out var v))seeds[key]=JsonNode.Parse(v.GetRawText());
            if(decision.TryGetProperty("candidates",out var rows)){seeds["candidates"]=JsonSerializer.SerializeToNode(rows.EnumerateArray().Take(3).ToArray());seeds["candidate_total"]=rows.GetArrayLength();}
            core["seed_decision"]=seeds;core["validity"]="observed_at_receipt_time; recheck_before_purchase";
            return Summary(JsonSerializer.SerializeToElement(core),1300);
        }
        return Summary(value);
    }
    private static readonly string[] Important={"status","state","error","stop_reason","reason","disposition","resume_policy","executed","completed","requested","remaining","gained","deposited","refills","task_id","command_id","intent_id","id","result_id","tool","actor","day","time","observed_day","observed_time","quote_token","cash","budget","keep_gold","seed_decision","items","options","recent_results","tasks","today_day","previous_day","daily_activity"};
    public static JsonElement Summary(JsonElement value,int budget=700) {
        if(value.ValueKind is JsonValueKind.Undefined)return JsonSerializer.SerializeToElement(new{});
        if(ContextBudget.Estimate(value.GetRawText())<=budget)return value.Clone();
        JsonNode? Copy(JsonElement v,int depth,int take) {
            if(v.ValueKind==JsonValueKind.Object) {
                var o=new JsonObject();var properties=v.EnumerateObject().OrderBy(p=>{int n=Array.IndexOf(Important,p.Name);return n<0?100:n;}).ToArray();
                foreach(var p in properties.Take(take))o[p.Name]=depth>0?Copy(p.Value,depth-1,take):p.Value.ValueKind is JsonValueKind.Object or JsonValueKind.Array?JsonSerializer.SerializeToNode(new{omitted=true}):Copy(p.Value,0,take);
                if(properties.Length>take)o["fields_omitted"]=properties.Length-take;return o;
            }
            if(v.ValueKind==JsonValueKind.Array){var a=new JsonArray();foreach(var item in v.EnumerateArray().Take(take))a.Add(Copy(item,Math.Max(0,depth-1),take));return new JsonObject{["items"]=a,["total"]=v.GetArrayLength(),["summary"]=true};}
            if(v.ValueKind==JsonValueKind.String&&v.GetString() is {} text&&text.Length>160)return JsonSerializer.SerializeToNode(new{preview=text[..(char.IsHighSurrogate(text[159])?159:160)],characters=text.Length,omitted=true});
            return JsonNode.Parse(v.GetRawText());
        }
        for(int take=8;take>=1;take/=2) {
            var summary=Copy(value,2,take);string json=summary!.ToJsonString(AgentJson.Options);
            if(ContextBudget.Estimate(json)<=budget)return JsonSerializer.Deserialize<JsonElement>(json);
        }
        return JsonSerializer.SerializeToElement(new{omitted=true,note="内容已归档；按result_id或context.read补读，省略不代表不存在"});
    }
}
