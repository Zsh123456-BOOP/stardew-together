using System.Text.Json;

namespace Together;

public static class ExecutionContract {
    // Preserve each executor's native evidence. This envelope adds a common shape,
    // never infers business success from a UI click or a missing item.
    public static object Receipt(object source,string epoch,int revision,string actor="") {
        var raw=JsonSerializer.SerializeToElement(source,AgentJson.Options);
        if(raw.ValueKind!=JsonValueKind.Object)return raw;
        var fields=raw.EnumerateObject().ToDictionary(x=>x.Name,x=>(object?)x.Value.Clone());
        if(!raw.TryGetProperty("command_id",out _)&&!raw.TryGetProperty("task_id",out _))return raw;
        fields["schema_version"]=1;fields["save_epoch"]=epoch;fields["observed_plan_revision"]=revision;
        if(!fields.ContainsKey("actor")&&actor.Length>0)fields["actor"]=actor;
        string error=raw.TryGetProperty("error",out var e)&&e.ValueKind==JsonValueKind.String?e.GetString()??"":"";
        string status=raw.TryGetProperty("status",out var st)?st.GetString()??"unknown":"unknown";
        fields.TryAdd("stop_reason",error.Length>0?error:null);
        fields["retryable"]=error is "path_stalled" or "no_path" or "stale_menu_read_again" or "target_not_available" or "storage_busy";
        string tool=raw.TryGetProperty("skill",out var skill)?skill.GetString()??"":raw.TryGetProperty("goal",out var goal)?"work.run":"";
        var outcome=OperationsPolicy.Outcome(tool,raw);fields["disposition"]=outcome.Disposition;
        fields["resume_policy"]=outcome.Disposition=="already_satisfied"?"do_not_retry_until_world_condition_changes":outcome.Disposition=="no_candidates_found"?"change_target_conditions_or_choose_other_work":status=="running"?"poll_existing_command":"reobserve_then_submit_remaining_work_no_blind_replay";
        fields["actual_progress"]=new{completed=raw.TryGetProperty("completed",out var c)?(int?)c.GetInt32():null,gained=raw.TryGetProperty("gained",out var g)?(int?)g.GetInt32():null};
        if(raw.TryGetProperty("before",out var before)&&before.ValueKind==JsonValueKind.Object&&raw.TryGetProperty("after",out var after)&&after.ValueKind==JsonValueKind.Object) {
            Dictionary<string,int> Stock(JsonElement snapshot) {
                var counts=new Dictionary<string,int>();
                if(!snapshot.TryGetProperty("inventory",out var inventory)||!inventory.TryGetProperty("items",out var items))return counts;
                foreach(var row in items.EnumerateArray())if(row.TryGetProperty("item",out var item)&&item.TryGetProperty("id",out var id)) {
                    string key=id.GetString()+":"+(item.TryGetProperty("quality",out var q)?q.GetInt32():0);
                    counts[key]=counts.GetValueOrDefault(key)+(item.TryGetProperty("count",out var n)?n.GetInt32():0);
                }
                return counts;
            }
            var old=Stock(before);var current=Stock(after);
            object? Change(string name)=>before.TryGetProperty(name,out var a)&&after.TryGetProperty(name,out var b)&&a.TryGetDouble(out double x)&&b.TryGetDouble(out double y)?y-x:null;
            fields["resource_delta"]=new{scope="observed_farmer_snapshot_window_not_exclusive_actor_attribution",items=old.Keys.Concat(current.Keys).Distinct().Select(id=>new{id,delta=current.GetValueOrDefault(id)-old.GetValueOrDefault(id)}).Where(x=>x.delta!=0),money=Change("money"),stamina=Change("stamina"),health=Change("health")};
        }
        if(raw.TryGetProperty("effects",out var effects))fields["native_progress_evidence"]=effects;
        if(raw.TryGetProperty("command_id",out var command))fields["evidence_ids"]=new[]{command.GetString()};
        return fields;
    }
}
