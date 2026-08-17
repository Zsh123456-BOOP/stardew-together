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
        fields["resume_policy"]=status=="running"?"poll_existing_command":"reobserve_then_submit_remaining_work_no_blind_replay";
        fields["actual_progress"]=new{completed=raw.TryGetProperty("completed",out var c)?(int?)c.GetInt32():null,gained=raw.TryGetProperty("gained",out var g)?(int?)g.GetInt32():null};
        return fields;
    }
}
