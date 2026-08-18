using System.Text.Json;

namespace Together;

public sealed class AgentTaskSpec {
    public string id {get;set;}="";
    public string actor {get;set;}="player";
    public string tool {get;set;}="";
    [Newtonsoft.Json.JsonConverter(typeof(AgentArgumentsConverter))]
    public JsonElement args {get;set;}=JsonSerializer.SerializeToElement(new{});
    public List<string> after {get;set;}=new();
    public string location {get;set;}="";
    public int day {get;set;}=-1;
    public int not_before {get;set;}=600;
    public int deadline {get;set;}=2500;
    public string purpose {get;set;}="";
    public string goal_id {get;set;}="";
}
public sealed class ScheduledAgentTask {
    public AgentTaskSpec spec {get;set;}=new();
    public string state {get;set;}="queued";
    public string? command_id {get;set;}
    public string? error {get;set;}
    public string? receipt {get;set;}
    public bool Terminal=>state is "succeeded" or "failed" or "cancelled" or "blocked";
}
public sealed class AgentSchedule {
    public int Revision {get;set;}
    public long EventVersion {get;set;}
    public List<ScheduledAgentTask> Tasks {get;set;}=new();
    public Dictionary<string,string> Submissions {get;set;}=new();
    public static bool Queueable(string tool)=>tool is "work.run" or "player.equip" or "player.accept_quest" or "player.animal" or "player.geodes" or "player.buy_animal" or "player.upgrade_house" or "player.mine_access" or "player.bundle" or "player.build" or "player.donate_museum" or "player.collect_reward" or "player.service" or "player.machine" or "player.claim_reward" or "player.care" or "player.social" or "player.combat" or "player.mine_descend" or "player.fish" or "player.buy" or "player.craft" or "player.cook" or "player.eat" or "player.work" or "player.move" or "player.travel" or "player.use_tool" or "player.interact" or "player.place" or "player.ship" or "player.sleep" or "companion.assign";
    private static bool IdValid(string? id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=80 && id.All(c=>char.IsLetterOrDigit(c)||c is '_' or '-' or ':' or '.');
    private static bool TimeValid(int time)=>time>=600&&time<=2600&&time%100<60;
    public bool Submit(string submission,int expected,List<AgentTaskSpec> specs,int day) {
        string fingerprint=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AgentJson.Encode(specs))));
        if(Submissions.TryGetValue(submission,out string? previous)) {
            if(previous!=fingerprint)throw new InvalidOperationException("submission_id_reused_with_different_tasks");return false;
        }
        if(expected!=Revision)throw new InvalidOperationException("plan_revision_changed_read_again");
        if(!IdValid(submission)||specs.Count is <1 or >24 || Tasks.Count(t=>!t.Terminal)+specs.Count>48)throw new InvalidOperationException("invalid_plan_size_or_id");
        var staged=new List<ScheduledAgentTask>();var known=Tasks.ToDictionary(t=>t.spec.id);
        foreach(var s in specs) {
            if(s==null||s.goal_id==null||s.goal_id.Length>80||s.goal_id.Length>0&&!IdValid(s.goal_id)||!IdValid(s.id)||known.ContainsKey(s.id)||!IdValid(s.actor)||!Queueable(s.tool)||s.args.ValueKind!=JsonValueKind.Object || s.after==null || s.after.Count>16 || s.after.Any(d=>!IdValid(d)) || s.location==null||s.location.Length>120||s.purpose==null||s.purpose.Length>200 || !TimeValid(s.not_before)||!TimeValid(s.deadline)||s.not_before>s.deadline || s.day < -1 || s.day>day+7 || s.day>=0&&s.day<day)
                throw new InvalidOperationException("invalid_plan_task");
            if(s.tool.StartsWith("player.") && s.actor!="player" || s.tool=="companion.assign" && (s.actor=="player" || !s.args.TryGetProperty("actor_id",out var actor)||actor.GetString()!=s.actor))throw new InvalidOperationException("task_actor_mismatch");
            if(s.tool=="work.run" && (s.args.TryGetProperty("actor_id",out var worker)?worker.GetString():"player")!=s.actor)throw new InvalidOperationException("task_actor_mismatch");
            // Clone inputs: normalization must not change the idempotency fingerprint.
            var clone=JsonSerializer.Deserialize<AgentTaskSpec>(JsonSerializer.Serialize(s))!;if(clone.day<0)clone.day=day;
            var predecessor=Tasks.Concat(staged).LastOrDefault(t=>!t.Terminal && t.spec.actor==clone.actor);
            if(predecessor!=null && predecessor.state=="needs_review")throw new InvalidOperationException("cancel_interrupted_tasks_before_resubmitting");
            if(predecessor!=null && !clone.after.Contains(predecessor.spec.id))clone.after.Add(predecessor.spec.id);
            var task=new ScheduledAgentTask{spec=clone};staged.Add(task);known.Add(clone.id,task);
        }
        foreach(var t in staged)if(t.spec.after.Any(id=>!known.ContainsKey(id)))throw new InvalidOperationException("unknown_task_dependency");
        var visiting=new HashSet<string>();var visited=new HashSet<string>();
        // Include implicit FIFO edges in cycle detection (B before A on the same actor cannot depend on A).
        var order=Tasks.Concat(staged).ToList();var prior=new Dictionary<string,string>();var fifo=new Dictionary<string,string>();
        foreach(var t in order){if(prior.TryGetValue(t.spec.actor,out var p))fifo[t.spec.id]=p;prior[t.spec.actor]=t.spec.id;}
        void Visit(string id){if(visited.Contains(id))return;if(!visiting.Add(id))throw new InvalidOperationException("cyclic_plan_dependencies");foreach(string d in known[id].spec.after)Visit(d);if(fifo.TryGetValue(id,out var prev))Visit(prev);visiting.Remove(id);visited.Add(id);}
        foreach(var t in staged)Visit(t.spec.id);
        // Keep task receipts bounded without deleting dependencies still referenced by active plans.
        if(Tasks.Count+staged.Count>192)throw new InvalidOperationException("plan_history_full_archive_terminal_tasks");
        Tasks.AddRange(staged);Submissions[submission]=fingerprint;
        foreach(var key in Submissions.Keys.Take(Math.Max(0,Submissions.Count-96)).ToArray())Submissions.Remove(key);
        Revision++;EventVersion++;return true;
    }
    public List<ScheduledAgentTask> Ready(int day,int time) {
        foreach(var t in Tasks.Where(t=>t.state=="queued")) {
            if(t.spec.day<day || t.spec.day==day&&time>t.spec.deadline){t.state="blocked";t.error="task_window_expired_replan";EventVersion++;}
            else if(t.spec.after.Any(id=>Tasks.FirstOrDefault(x=>x.spec.id==id) is not {} d || d.state is "failed" or "blocked" or "cancelled" or "needs_review")){t.state="blocked";t.error="dependency_not_completed_replan";EventVersion++;}
        }
        return Tasks.Where(t=>!t.Terminal).GroupBy(t=>t.spec.actor).Select(g=>g.First()).Where(t=>t.state=="queued" && t.spec.day==day && time>=t.spec.not_before && t.spec.after.All(id=>Tasks.Any(d=>d.spec.id==id&&d.state=="succeeded"))).ToList();
    }
    public void Started(ScheduledAgentTask t,string command){t.state="running";t.command_id=command;t.error=null;EventVersion++;}
    public void Finish(ScheduledAgentTask t,string status,string? error,string receipt){t.state=status;t.error=error;t.receipt=receipt.Length<=10000?receipt:AgentJson.Encode(new{truncated=true,prefix=receipt[..9000]});EventVersion++;}
    public void Suspend() {
        foreach(var t in Tasks.Where(t=>!t.Terminal)){t.state="needs_review";t.error="interrupted_read_world_before_replacing";}
        EventVersion++;Revision++;
    }
    public void CancelPending(IEnumerable<string> ids) {
        var wanted=ids.Distinct().Select(id=>Tasks.FirstOrDefault(t=>t.spec.id==id)??throw new InvalidOperationException("unknown_task_id")).ToArray();
        if(wanted.Any(t=>t.state=="running"))throw new InvalidOperationException("running_task_cancel_requires_executor");
        foreach(var t in wanted.Where(t=>!t.Terminal)){t.state="cancelled";t.error="cancelled_by_model";}
        Revision++;EventVersion++;
    }
    public int Archive() {
        var retained=new HashSet<string>(Tasks.Where(t=>!t.Terminal).Select(t=>t.spec.id));
        void Keep(string id){var t=Tasks.FirstOrDefault(t=>t.spec.id==id);if(t==null)return;foreach(var d in t.spec.after)if(retained.Add(d))Keep(d);}
        foreach(var id in retained.ToArray())Keep(id);
        int n=Tasks.RemoveAll(t=>t.Terminal&&!retained.Contains(t.spec.id));if(n>0){Revision++;EventVersion++;}return n;
    }
}

// SMAPI saves use Newtonsoft; the model protocol uses System.Text.Json. Preserve
// the actual argument object across both serializers, not JsonElement.ValueKind.
public sealed class AgentArgumentsConverter : Newtonsoft.Json.JsonConverter {
    public override bool CanConvert(Type type)=>type==typeof(JsonElement);
    public override void WriteJson(Newtonsoft.Json.JsonWriter writer,object? value,Newtonsoft.Json.JsonSerializer serializer) {
        writer.WriteRawValue(value is JsonElement e && e.ValueKind!=JsonValueKind.Undefined?e.GetRawText():"{}");
    }
    public override object ReadJson(Newtonsoft.Json.JsonReader reader,Type type,object? existing,Newtonsoft.Json.JsonSerializer serializer) {
        var token=Newtonsoft.Json.Linq.JToken.Load(reader);
        // Earlier development saves lost their args as {ValueKind:1}. They remain
        // needs_review on load; do not invent or automatically replay those args.
        if(token is Newtonsoft.Json.Linq.JObject obj && obj.Count==1 && obj["ValueKind"]!=null)return JsonSerializer.SerializeToElement(new{});
        using var document=JsonDocument.Parse(token.ToString(Newtonsoft.Json.Formatting.None));
        return document.RootElement.Clone();
    }
}
