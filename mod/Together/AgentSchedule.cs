using System.Text.Json;

namespace Together;

[System.Text.Json.Serialization.JsonNumberHandling(System.Text.Json.Serialization.JsonNumberHandling.AllowReadingFromString)]
public sealed class AgentTaskSpec {
    public string id {get;set;}="";
    public string actor {get;set;}="player";
    public string tool {get;set;}="";
    [Newtonsoft.Json.JsonConverter(typeof(AgentArgumentsConverter))]
    public JsonElement args {get;set;}=JsonSerializer.SerializeToElement(new{});
    public List<string> after {get;set;}=new();
    public List<string> sequence_after {get;set;}=new();
    public string sleep_review_basis {get;set;}="";
    public int sleep_review_day {get;set;}=-1;
    public int sleep_review_time {get;set;}=-1;
    public long sleep_review_progress {get;set;}=-1;
    public string location {get;set;}="";
    public int day {get;set;}=-1;
    public int not_before {get;set;}=600;
    public int deadline {get;set;}=2500;
    public string purpose {get;set;}="";
    public string goal_id {get;set;}="";
    public string intent_id {get;set;}="";
    public string source {get;set;}="model";
    public int priority {get;set;}=50;
}
public sealed class ScheduledAgentTask {
    public AgentTaskSpec spec {get;set;}=new();
    public string state {get;set;}="queued";
    public string? command_id {get;set;}
    public string cancellation_requested_by {get;set;}="";
    public string? error {get;set;}
    public string? wait_reason {get;set;}
    public string? receipt {get;set;}
    public bool Terminal=>state is "succeeded" or "failed" or "partial" or "cancelled" or "blocked";
}
public sealed class PlanStepRejected : InvalidOperationException {
    public string TaskId {get;}
    public PlanStepRejected(string taskId,string reason):base(reason){TaskId=taskId;}
}
public sealed class AgentSchedule {
    [System.Text.Json.Serialization.JsonIgnore,Newtonsoft.Json.JsonIgnore]
    public Action<AgentTaskSpec>? Prepare {get;set;}
    [System.Text.Json.Serialization.JsonIgnore,Newtonsoft.Json.JsonIgnore]
    public Action<ScheduledAgentTask>? Finished {get;set;}
    public int Revision {get;set;}
    public long EventVersion {get;set;}
    public List<ScheduledAgentTask> Tasks {get;set;}=new();
    public Dictionary<string,string> Submissions {get;set;}=new();
    public static bool Queueable(string tool)=>tool is "player.discard" or "player.collect_home_gifts" or "player.recruit_companion" or "player.tap_tree" or "player.acquire_animal" or "player.procure" or "player.find_lost_item" or "work.run" or "player.beach" or "player.crab_pots" or "player.treasure" or "player.walnuts" or "player.volcano_step" or "player.forge" or "player.island_upgrade" or "player.arcade" or "player.read_mail" or "player.watch_tv" or "player.transport" or "player.repair_boat" or "player.read_book" or "player.mastery" or "player.orchard" or "player.joja" or "player.place_facility" or "player.ship_items" or "player.order_donate" or "player.equip" or "player.attach" or "player.accept_quest" or "player.animal" or "player.geodes" or "player.buy_animal" or "player.upgrade_house" or "player.mine_access" or "player.bundle" or "player.build" or "player.donate_museum" or "player.collect_reward" or "player.service" or "player.machine" or "player.claim_reward" or "player.care" or "player.social" or "player.combat" or "player.mine_descend" or "player.fish" or "player.buy" or "player.craft" or "player.cook" or "player.eat" or "player.work" or "player.move" or "player.travel" or "player.use_tool" or "player.interact" or "player.place" or "player.ship" or "player.sleep" or "companion.assign";
    private static bool IdValid(string? id)=>!string.IsNullOrWhiteSpace(id)&&id.Length<=80 && id.All(c=>char.IsLetterOrDigit(c)||c is '_' or '-' or ':' or '.');
    private static bool TimeValid(int time)=>time>=600&&time<=2600&&time%100<60;
    public bool Submit(string submission,int expected,List<AgentTaskSpec> specs,int day,bool ordered=false,bool retainIndependent=false) {
        string fingerprint=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AgentJson.Encode(new{specs,ordered}))));
        if(Submissions.TryGetValue(submission,out string? previous)) {
            if(previous!=fingerprint)throw new InvalidOperationException("submission_id_reused_with_different_tasks");return false;
        }
        if(expected!=Revision)throw new InvalidOperationException("plan_revision_changed_read_again");
        if(!IdValid(submission)||specs.Count is <1 or >24 || Tasks.Count(t=>!t.Terminal)+specs.Count>48)throw new InvalidOperationException("invalid_plan_size_or_id");
        var staged=new List<ScheduledAgentTask>();var known=Tasks.ToDictionary(t=>t.spec.id);
        foreach(var s in specs) {
            if(s==null||s.intent_id==null||s.intent_id.Length>80||s.source==null||s.source.Length>60||s.priority is <0 or >100||s.goal_id==null||s.goal_id.Length>80||s.goal_id.Length>0&&!IdValid(s.goal_id)||!IdValid(s.id)||known.ContainsKey(s.id)||!IdValid(s.actor)||!Queueable(s.tool)||s.args.ValueKind!=JsonValueKind.Object || s.after==null || s.after.Count>16 || s.after.Any(d=>!IdValid(d)) || s.sequence_after==null || s.sequence_after.Count>16 || s.sequence_after.Any(d=>!IdValid(d)) || s.location==null||s.location.Length>120||s.purpose==null||s.purpose.Length>200 || !TimeValid(s.not_before)||!TimeValid(s.deadline)||s.not_before>s.deadline || s.day < -1 || s.day>day+7 || s.day>=0&&s.day<day)
                throw new InvalidOperationException("invalid_plan_task");
            if(s.tool.StartsWith("player.") && s.actor!="player" || s.tool=="companion.assign" && (s.actor=="player" || s.args.TryGetProperty("actor_id",out var actor)&&actor.GetString()!=s.actor))throw new InvalidOperationException("task_actor_mismatch");
            if(s.tool=="work.run" && s.args.TryGetProperty("actor_id",out var worker)&&worker.GetString()!=s.actor)throw new InvalidOperationException("task_actor_mismatch");
            // Clone inputs: normalization must not change the idempotency fingerprint.
            var clone=JsonSerializer.Deserialize<AgentTaskSpec>(JsonSerializer.Serialize(s))!;if(clone.day<0)clone.day=day;
            if(clone.tool is "work.run" or "companion.assign" && !clone.args.TryGetProperty("actor_id",out _)) {
                var normalized=clone.args.Deserialize<Dictionary<string,JsonElement>>()!;normalized["actor_id"]=JsonSerializer.SerializeToElement(clone.actor);clone.args=JsonSerializer.SerializeToElement(normalized);
            }
            if(Tasks.Any(t=>t.spec.actor==clone.actor&&t.state=="needs_review"))throw new InvalidOperationException("cancel_interrupted_tasks_before_resubmitting");
            if(clone.intent_id.Length==0)clone.intent_id=submission;
            var predecessor=ordered?staged.LastOrDefault(t=>t.spec.actor==clone.actor):null;
            if(predecessor!=null&&!clone.after.Contains(predecessor.spec.id))clone.after.Add(predecessor.spec.id);
            string? rejection=null;
            try{Prepare?.Invoke(clone);}catch(InvalidOperationException e){if(!retainIndependent)throw new PlanStepRejected(clone.id,e.Message);rejection=e.Message;}
            var task=new ScheduledAgentTask{spec=clone,state=rejection==null?"queued":"blocked",error=rejection};staged.Add(task);known.Add(clone.id,task);
        }
        foreach(var t in staged)if(t.spec.after.Concat(t.spec.sequence_after).Any(id=>!known.ContainsKey(id)))throw new InvalidOperationException("unknown_task_dependency");
        var visiting=new HashSet<string>();var visited=new HashSet<string>();
        // Only explicit business edges participate in the DAG. Actor exclusion
        // is enforced at dispatch, never by making unrelated work depend on it.
        void Visit(string id){if(visited.Contains(id))return;if(!visiting.Add(id))throw new InvalidOperationException("cyclic_plan_dependencies");foreach(string d in known[id].spec.after.Concat(known[id].spec.sequence_after))Visit(d);visiting.Remove(id);visited.Add(id);}
        foreach(var t in staged)Visit(t.spec.id);
        // Keep task receipts bounded without deleting dependencies still referenced by active plans.
        if(Tasks.Count+staged.Count>192)throw new InvalidOperationException("plan_history_full_archive_terminal_tasks");
        Tasks.AddRange(staged);
        foreach(var blocked in staged.Where(t=>t.Terminal)){blocked.receipt=AgentJson.Encode(new{status="blocked",error=blocked.error,stop_reason=blocked.error,executed=false,completed=0});Finished?.Invoke(blocked);}
        Submissions[submission]=fingerprint;
        foreach(var key in Submissions.Keys.Take(Math.Max(0,Submissions.Count-96)).ToArray())Submissions.Remove(key);
        Revision++;EventVersion++;return true;
    }
    public static JsonElement RebaseOwnTurn(JsonElement args,int turnRevision,int currentRevision) {
        if(turnRevision==currentRevision||!args.TryGetProperty("expected_revision",out var expected))return args;
        int value=expected.ValueKind==JsonValueKind.Number?expected.GetInt32():expected.ValueKind==JsonValueKind.String&&int.TryParse(expected.GetString(),out int parsed)?parsed:-1;
        if(value!=turnRevision)return args; // Never rebase an already-stale observation.
        var copy=args.Deserialize<Dictionary<string,JsonElement>>()!;copy["expected_revision"]=JsonSerializer.SerializeToElement(currentRevision);
        return JsonSerializer.SerializeToElement(copy);
    }
    public (string State,string? Reason,int? Next) Readiness(ScheduledAgentTask t,int day,int time) {
        if(t.Terminal)return ("terminal",t.error,null);
        if(t.state=="running")return ("running",null,null);
        if(t.state=="needs_review")return ("waiting_condition",t.error,null);
        if(t.spec.day<day||t.spec.day==day&&time>t.spec.deadline)return ("blocked","task_window_expired_replan",null);
        if(t.spec.after.Any(id=>Tasks.FirstOrDefault(x=>x.spec.id==id) is not {} d||d.Terminal&&d.state!="succeeded"||d.state=="needs_review"))return ("blocked","dependency_not_completed_replan",null);
        if(t.spec.day>day)return ("waiting_until","future_day",t.spec.not_before);
        if(t.spec.sequence_after.Any(id=>!Tasks.Any(d=>d.spec.id==id)))return ("blocked","sequence_dependency_missing_replan",null);
        if(!t.spec.after.All(id=>Tasks.Any(d=>d.spec.id==id&&d.state=="succeeded"))||!t.spec.sequence_after.All(id=>Tasks.Any(d=>d.spec.id==id&&d.Terminal)))return ("waiting_condition","dependencies_pending",null);
        if(time<t.spec.not_before)return ("waiting_until",t.wait_reason??"before_start",t.spec.not_before);
        if(t.wait_reason!=null)return ("waiting_condition",t.wait_reason,null);
        return ("runnable",null,null);
    }
    public bool Covers(string actor,int day,int time)=>Tasks.Any(t=>t.spec.actor==actor&&Readiness(t,day,time).State is "running" or "runnable");
    public List<ScheduledAgentTask> Ready(int day,int time,Func<IEnumerable<ScheduledAgentTask>,IEnumerable<ScheduledAgentTask>>? order=null,Func<string,bool>? externallyBusy=null) {
        foreach(var t in Tasks.Where(t=>t.state=="queued").ToArray()) {var readiness=Readiness(t,day,time);if(readiness.State=="blocked")Finish(t,"blocked",readiness.Reason,AgentJson.Encode(new{status="blocked",stop_reason=readiness.Reason,executed=false,completed=0}));}
        var busy=Tasks.Where(t=>t.state=="running").Select(t=>t.spec.actor).ToHashSet();
        IEnumerable<ScheduledAgentTask> eligible=Tasks.Where(t=>Readiness(t,day,time).State=="runnable"&&!busy.Contains(t.spec.actor)&&externallyBusy?.Invoke(t.spec.actor)!=true)
            .OrderByDescending(t=>t.spec.priority).ThenByDescending(t=>Tasks.Any(p=>p.spec.intent_id==t.spec.intent_id&&p.state=="succeeded"));
        return (order==null?eligible:order(eligible)).GroupBy(t=>t.spec.actor).Select(g=>g.First()).ToList();
    }
    public void Started(ScheduledAgentTask t,string command){t.state="running";t.command_id=command;t.error=null;t.wait_reason=null;EventVersion++;}
    public void Finish(ScheduledAgentTask t,string status,string? error,string receipt){bool changed=!t.Terminal||t.state!=status||t.receipt!=receipt;t.state=status;t.error=error;t.receipt=receipt;t.wait_reason=null;if(changed){EventVersion++;Finished?.Invoke(t);}}
    public void Suspend() {
        foreach(var t in Tasks.Where(t=>!t.Terminal)){t.state="needs_review";t.error="interrupted_read_world_before_replacing";}
        EventVersion++;Revision++;
    }
    public void CancelPending(IEnumerable<string> ids,string reason="cancelled_by_model") {
        var wanted=ids.Distinct().Select(id=>Tasks.FirstOrDefault(t=>t.spec.id==id)??throw new InvalidOperationException("unknown_task_id")).ToArray();
        if(wanted.Any(t=>t.state=="running"))throw new InvalidOperationException("running_task_cancel_requires_executor");
        foreach(var t in wanted.Where(t=>!t.Terminal))Finish(t,"cancelled",reason,AgentJson.Encode(new{status="cancelled",stop_reason=reason,executed=false,completed=0}));
        Revision++;EventVersion++;
    }
    public int Archive() {
        var retained=new HashSet<string>(Tasks.Where(t=>!t.Terminal).Select(t=>t.spec.id));
        void Keep(string id){var t=Tasks.FirstOrDefault(t=>t.spec.id==id);if(t==null)return;foreach(var d in t.spec.after.Concat(t.spec.sequence_after))if(retained.Add(d))Keep(d);}
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
