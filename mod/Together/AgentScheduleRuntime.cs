using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private bool agentLabProbe;
    private readonly List<string> agentWakeReasons=new();
    private long agentRequestEpoch;
    private int agentRequestDay;
    private bool agentNeedsDecision=true;
    private string agentEventSignature="";
    private DateTime agentObserveAt;
    private bool agentWasInDanger;
    private readonly HashSet<string> agentKnownActors=new(){"player"};
    private string agentIdleSignature="";
    private bool AgentActorHasWork(string actor)=>WorkActorBusy(actor)||actor=="player"&&playerExecutor.Busy||Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.actor==actor&&t.state is "queued" or "running");
    private bool AgentPlayerCovered()=>AgentActorHasWork("player")||Data.FarmInvestment.Enabled&&Data.FarmInvestment.Phase=="planning";
    private bool AgentWorkCovered()=>AgentPlayerCovered()&&agentKnownActors.Where(a=>a!="player").All(a=>AgentActorHasWork(a)||Data.Business.Enabled&&Data.Partner.Enabled&&a.EndsWith(":"+PartnerName));
    private void WakeAgent(string reason) {
        if(!agentWakeReasons.Contains(reason))agentWakeReasons.Add(reason);
        if(agentWakeReasons.Count>16)agentWakeReasons.RemoveAt(0);
        agentNeedsDecision=true;agentRequestedWait=DateTime.MinValue;agentNext=DateTime.UtcNow;
    }
    internal object AgentPlanRead(bool compact=false)=>new {
        revision=Data.Autoplay.Schedule.Revision,event_version=Data.Autoplay.Schedule.EventVersion,
        tasks=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).Select(t=>new{t.spec,t.state,t.command_id,t.error}),
        recent_results=Data.Autoplay.Schedule.Tasks.Where(t=>t.Terminal).TakeLast(8).Select(t=>new{id=t.spec.id,actor=t.spec.actor,tool=t.spec.tool,t.state,t.error,receipt=compact?ReceiptSummary(t.receipt):(object?)t.receipt}),
        note="同一角色按队列顺序执行，角色之间独立；after指定跨角色依赖。排队不是成功。换日/中断/失败须核验真实状态，不能重放旧坐标或菜单。"
    };
    internal object AgentPlanSubmit(JsonElement args) {
        var list=args.TryGetProperty("tasks",out var tasks)?JsonSerializer.Deserialize<List<AgentTaskSpec>>(tasks.GetRawText()):null;
        if(list==null)throw new InvalidOperationException("tasks_required");
        foreach(var spec in list.Where(s=>s!=null)) {
            if(spec.tool=="companion.assign" || spec.tool=="work.run"&&spec.actor!="player") {
                // All NPC task lanes must name an actor actually observed in this save.
                if(!World().GetProperty("actors").EnumerateArray().Any(a=>a.GetProperty("id").GetString()==spec.actor))throw new InvalidOperationException("actor_not_recruited");
            }
        }
        bool added=Data.Autoplay.Schedule.Submit(AgentToolRegistry.Text(args,"submission_id"),AgentToolRegistry.Number(args,"expected_revision",-1),list,Game1.Date.TotalDays);
        return new{status=added?"queued":"already_submitted",revision=Data.Autoplay.Schedule.Revision,ids=list.Select(t=>t.id),note="任务尚未执行；只以任务回执作为完成证据"};
    }
    internal object AgentPlanCancel(JsonElement args) {
        var ids=args.TryGetProperty("ids",out var raw)?JsonSerializer.Deserialize<List<string>>(raw.GetRawText()):null;
        if(ids==null||ids.Count is <1 or >48)throw new InvalidOperationException("task_ids_required");
        var tasks=ids.Distinct().Select(id=>Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==id)??throw new InvalidOperationException("unknown_task_id")).ToArray();
        foreach(var t in tasks.Where(t=>t.state=="running")) {
            var result=JsonSerializer.SerializeToElement(AgentReceipt(t.command_id!,true),AgentJson.Options);
            if(result.TryGetProperty("status",out var s)&&s.GetString()=="running")throw new InvalidOperationException("native_operation_cannot_cancel_yet");
            string status=result.TryGetProperty("status",out var ended)&&ended.GetString()=="succeeded"?"succeeded":"cancelled";
            Data.Autoplay.Schedule.Finish(t,status,"model_cancel_requested",AgentJson.Encode(result));agentClaims.Remove(t.command_id!);
        }
        Data.Autoplay.Schedule.CancelPending(ids);return new{status="cancelled_pending",revision=Data.Autoplay.Schedule.Revision};
    }
    internal object AgentPlanArchive()=>new{archived=Data.Autoplay.Schedule.Archive(),revision=Data.Autoplay.Schedule.Revision};
    private object QueueLegacyAction(AgentCall call) {
        string actor=call.tool is "companion.assign" or "work.run"?AgentToolRegistry.Text(call.args,"actor_id","player"):"player";
        var previous=Data.Autoplay.Schedule.Tasks.LastOrDefault(t=>t.spec.actor==actor&&!t.Terminal);
        string location=actor=="player"?ToolLocationContract.Bind(call.tool,Game1.currentLocation.NameOrUniqueName,previous?.spec.tool,previous?.spec.location??"",previous==null?"":AgentToolRegistry.Text(previous.spec.args,"location"),AgentToolRegistry.Text(call.args,"location")):"";
        var task=new AgentTaskSpec{id="step-"+Guid.NewGuid().ToString("N"),actor=actor,tool=call.tool,args=call.args.Clone(),location=location,day=Game1.Date.TotalDays,purpose=Data.Autoplay.Plan[..Math.Min(160,Data.Autoplay.Plan.Length)]};
        if(previous!=null)task.after.Add(previous.spec.id);
        Data.Autoplay.Schedule.Submit(task.id,Data.Autoplay.Schedule.Revision,new(){task},Game1.Date.TotalDays);
        return new{status="queued",task_id=task.id,actor,revision=Data.Autoplay.Schedule.Revision};
    }
    private void RecordAgentFailure(string code,string actor="decision") {
        if(agentFailures.Failed(actor,code))PauseAutoplay("同一角色无成功进展期间反复失败6次，计划已保留："+code);
    }
    private void CompleteScheduled(ScheduledAgentTask task,JsonElement result) {
        string state=result.TryGetProperty("status",out var status)?status.GetString()??"failed":"failed";
        string? error=result.TryGetProperty("error",out var e)&&e.ValueKind==JsonValueKind.String?e.GetString():null;
        if(state!="succeeded" && state!="cancelled")state="failed";
        LearnActionResult(task,state,error);
        if(state!="succeeded"&&task.spec.goal_id.Length>0) {
            var goal=Data.SharedGoals.FirstOrDefault(g=>g.Id==task.spec.goal_id);
            if(goal!=null){goal.AutoExecute=RecoveryPolicy.CanWait(error);goal.AutoBlockedReason="task_failed:"+task.spec.id+":"+error;
                if(goal.AutoExecute){RefreshFacts(true);goal.AutoBlockedConditions=GoalCondition(goal);goal.AutoReviewDay=Game1.Date.TotalDays;goal.AutoReviewMinute=DailyBudget.Minutes(Game1.timeOfDay);}
            }
        }
        Data.Autoplay.Schedule.Finish(task,state,error,AgentJson.Encode(result));
        if(task.command_id!=null)agentClaims.Remove(task.command_id);
        Data.Autoplay.Record("action_result",AgentJson.Encode(result));
        Data.Autoplay.Record("task_finished",AgentJson.Encode(new{id=task.spec.id,actor=task.spec.actor,state,error}));
        if(state=="succeeded") {
            agentFailures.Progress(task.spec.actor);agentFailures.Progress("decision");
            Data.Autoplay.VerifiedActions++;Data.Autoplay.Agenda.EnterDay(Game1.Date.TotalDays);Data.Autoplay.Agenda.CompletedBatches++;
            var cleanup=task.spec.tool=="work.run"&&AgentToolRegistry.Text(task.spec.args,"goal")=="cleanup"
                ?Data.Maintenance.Orders.FirstOrDefault(o=>o.Id==AgentToolRegistry.Text(task.spec.args,"cleanup_id")):null;
            bool cleanupContinues=cleanup is {Status:"active"}&&Game1.timeOfDay<cleanup.Until&&FarmCleanupRules.RemainingBudget(cleanup)>0&&CleanupAllowanceFor(cleanup).Available>=4&&CleanupTargets().Any(t=>CleanupMatches(cleanup,t));
            if(cleanupContinues)maintenanceAt=DateTime.MinValue;
            else if(!Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.actor==task.spec.actor&&t.state=="queued"&&t.spec.day==Game1.Date.TotalDays))WakeAgent("actor_ready:"+task.spec.actor);
        } else {
            if(RecoveryPolicy.CanWait(error)){if(!Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.actor==task.spec.actor&&!t.Terminal))WakeAgent("work_yielded:"+task.spec.id);}
            else {WakeAgent("task_failed:"+task.spec.id);RecordAgentFailure(error??"action_failed",task.spec.actor);}
        }
    }
    private void TickAgentSchedule() {
        var schedule=Data.Autoplay.Schedule;
        foreach(var task in schedule.Tasks.Where(t=>t.state=="running").ToArray()) {
            JsonElement result;
            try{result=JsonSerializer.SerializeToElement(AgentReceipt(task.command_id!,false),AgentJson.Options);}
            catch{result=JsonSerializer.SerializeToElement(new{status="failed",error="receipt_unavailable_replan"});}
            if(result.TryGetProperty("status",out var s)&&s.GetString()=="running")continue;
            CompleteScheduled(task,result);
        }
        if(!AutoplayRunning || Game1.eventUp || Game1.fadeToBlack || Game1.locationRequest!=null)return;
        long version=schedule.EventVersion;
        var ready=schedule.Ready(Game1.Date.TotalDays,Game1.timeOfDay);
        if(schedule.EventVersion!=version)WakeAgent("expired_or_failed_dependency");
        foreach(var task in ready) {
            if(!AutoplayRunning)break;
            // Maintenance and cargo-support jobs may own an actor outside this
            // queue. Wait for the whole semantic job, not just its current swing.
            if(WorkActorBusy(task.spec.actor))continue;
            bool purchasing=PlayerExecutor.AcceptsNativeMenu(task.spec.tool);
            if(task.spec.actor=="player" && (playerExecutor.Busy || Game1.activeClickableMenu!=null&&!purchasing || !Game1.player.CanMove&&!purchasing || Game1.player.UsingTool))continue;
            try {
                if(task.spec.actor=="player"&&ToolLocationContract.RequiresObservedLocation(task.spec.tool)&&task.spec.location.Length>0 && task.spec.location!=Game1.currentLocation.NameOrUniqueName)throw new InvalidOperationException("planned_location_changed_replan");
                if(task.spec.tool=="player.sleep" && schedule.Tasks.Any(t=>t.state=="running"&&t.spec.actor!="player"))throw new InvalidOperationException("finish_or_cancel_companion_work_before_sleep");
                CheckKnownFailure(task);
                var result=JsonSerializer.SerializeToElement(agentTools.Execute(task.spec.tool,task.spec.args),AgentJson.Options);
                Data.Autoplay.Record("task_started",AgentJson.Encode(new{id=task.spec.id,actor=task.spec.actor,tool=task.spec.tool,result}));
                if(result.TryGetProperty("status",out var s)&&s.GetString()=="running"&&result.TryGetProperty("command_id",out var id))schedule.Started(task,id.GetString()!);
                else CompleteScheduled(task,result);
            }catch(Exception e){CompleteScheduled(task,JsonSerializer.SerializeToElement(new{status="failed",error=e is InvalidOperationException?e.Message:"executor_"+e.GetType().Name}));}
        }
    }
    private void ObserveAgentEvents() {
        if(DateTime.UtcNow<agentObserveAt)return;agentObserveAt=DateTime.UtcNow.AddSeconds(1);
        string idle=string.Join(",",agentKnownActors.OrderBy(x=>x).Where(actor=>!AgentActorHasWork(actor)));
        if(idle!=agentIdleSignature){agentIdleSignature=idle;if(idle.Length>0)WakeAgent("idle_actors:"+idle);}
        bool danger=Game1.player.health<35;
        if(danger&&!agentWasInDanger){agentGeneration++;WakeAgent("danger");} // Discard an in-flight plan based on a previously safe state.
        agentWasInDanger=danger;
        string signature=$"{Game1.Date.TotalDays}:{Game1.timeOfDay>=2200}:{Game1.player.health<35}:{Game1.player.Stamina<20}:{(playerExecutor.Busy?"owned":Game1.activeClickableMenu?.GetType().Name)}:{Game1.eventUp}:{Game1.currentMinigame?.GetType().Name}";
        if(signature!=agentEventSignature){agentEventSignature=signature;WakeAgent("environment_changed");}
        if(playerExecutor.NeedsMenuChoice)WakeAgent("night_menu_choice");
    }
}
