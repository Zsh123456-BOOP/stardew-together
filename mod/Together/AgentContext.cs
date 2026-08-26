using System.Text.Json;
namespace Together;
public sealed partial class ModEntry {
    private object ModelToolObservation(string tool,JsonElement result) {
        if(result.ValueKind!=JsonValueKind.Object||result.TryGetProperty("error",out _))return result;
        if(tool=="plan.read")return AgentPlanRead(true);
        if(tool is "day.read" or "day.plan" or "progress.roadmap")return new{status="observed",current_context=tool=="progress.roadmap"?"progression":"day"};
        if(tool=="action.status")return ReceiptSummary(result.GetRawText());
        if(tool=="world.read")return new{snapshot=result.GetProperty("snapshot"),farm=result.GetProperty("farm"),inventory_plan=result.GetProperty("inventory_plan"),companions="本轮companions字段提供最新角色状态、位置和可执行能力",goals="本轮day.shared_goals字段"};
        return result;
    }
    private static object ReceiptSummary(string? text) {
        if(string.IsNullOrEmpty(text))return new{};
        try {
            var r=JsonSerializer.Deserialize<JsonElement>(text);var fields=new Dictionary<string,object?>();
            foreach(string key in new[]{"command_id","skill","goal","actor","status","phase","error","stop_reason","completed","requested","gained","skipped","refills","deposited"})if(r.TryGetProperty(key,out var v))fields[key]=v.Clone();
            if(r.TryGetProperty("after",out var after))fields["after"]=after.EnumerateObject().Where(p=>p.Name!="inventory").ToDictionary(p=>p.Name,p=>p.Value.Clone());
            if(r.TryGetProperty("evidence",out var evidence)&&evidence.ValueKind==JsonValueKind.Object)fields["evidence"]=evidence.Clone();
            fields["details"]= "完整回执可用action.status查询，背包用inventory.read";
            return fields;
        }catch{return new{unreadable_receipt=true};}
    }
    private object[] RecentAgentContext()=>Data.Autoplay.Journal.Where(e=>e.Kind is "tool_result" or "invalid_model_reply" or "stale_decision").TakeLast(6).Where((e,index)=>!DuplicateSnapshot(e,index)).Select(e=>{
        try {
            var data=JsonSerializer.Deserialize<JsonElement>(e.Text);
            if(e.Kind=="tool_result"&&data.TryGetProperty("tool",out var t)) {
                string? tool=t.GetString();
                if(tool is "day.read" or "day.plan" or "plan.read" or "progress.roadmap" && data.TryGetProperty("result",out var result) && result.ValueKind==JsonValueKind.Object && !result.TryGetProperty("error",out _))return (object)new{kind=e.Kind,tool,result="读取成功；最新完整结果见本轮day/schedule/progression字段"};
                if(tool=="action.status")return new{kind=e.Kind,tool,result=ReceiptSummary(data.GetProperty("result").GetRawText())};
            }
            return new{kind=e.Kind,data};
        }catch{return (object)new{kind=e.Kind,text=e.Text};}
    }).ToArray();
    private bool DuplicateSnapshot(AgentEvent e,int index) {
        string? Name(AgentEvent ev) {try{var data=JsonSerializer.Deserialize<JsonElement>(ev.Text);return ev.Kind=="tool_result"&&data.TryGetProperty("tool",out var tool)?tool.GetString():null;}catch{return null;}}
        string? name=Name(e);if(name is not ("world.read" or "inventory.read" or "day.read" or "plan.read"))return false;
        return Data.Autoplay.Journal.Where(x=>x.Kind is "tool_result" or "invalid_model_reply" or "stale_decision").TakeLast(6).Skip(index+1).Any(x=>Name(x)==name);
    }
    private void RecordAgentUsage(ModelReply reply) {
        WriteBusinessLog("model_usage",AgentJson.Encode(new{model=reply.Model,input_tokens=reply.InputTokens,output_tokens=reply.OutputTokens,cache_hit_tokens=reply.CacheHitTokens,total_tokens=reply.Tokens,latency_ms=agentLastLatency}));
        string dir=Path.Combine(Helper.DirectoryPath,"usage");Directory.CreateDirectory(dir);
        File.AppendAllText(Path.Combine(dir,"autoplay-model-usage.jsonl"),AgentJson.Encode(new{
            utc=DateTime.UtcNow,requested_model=Settings.Model,served_model=reply.Model,input_tokens=reply.InputTokens,
            output_tokens=reply.OutputTokens,cache_hit_tokens=reply.CacheHitTokens,total_tokens=reply.Tokens,
            run_id=Data.Autoplay.RunId,latency_ms=agentLastLatency
        })+Environment.NewLine);
    }
}
