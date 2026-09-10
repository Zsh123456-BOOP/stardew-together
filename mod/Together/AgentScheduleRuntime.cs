using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private bool agentLabProbe;
    private string decisionIntent="";
    private readonly List<string> agentWakeReasons=new();
    private long agentRequestEpoch;
    private int agentRequestDay;
    private bool agentNeedsDecision=true;
    private string agentEventSignature="";
    private DateTime agentObserveAt;
    private bool agentWasInDanger;
    private readonly HashSet<string> agentKnownActors=new(){"player"};
    private string agentIdleSignature="";
    private bool AgentActorHasWork(string actor)=>OperationActorOccupied(actor);
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
        note="同一角色只能执行一个任务；after声明真正的前置条件，未来时间的任务不会挡住其他就绪工作。actor统一选择角色，args.actor_id省略时自动继承，显式冲突才拒绝。排队不是成功。换日/中断/失败须核验真实状态，不能重放旧坐标或菜单。"
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
            if(result.TryGetProperty("status",out var s)&&s.GetString()=="running"){t.error="cancellation_pending_native_action";continue;}
            string status=result.TryGetProperty("status",out var ended)&&ended.GetString()=="succeeded"?"succeeded":"cancelled";
            Data.Autoplay.Schedule.Finish(t,status,"model_cancel_requested",AgentJson.Encode(result));agentClaims.Remove(t.command_id!);
        }
        Data.Autoplay.Schedule.CancelPending(tasks.Where(t=>t.state!="running").Select(t=>t.spec.id));return new{status=tasks.Any(t=>t.state=="running")?"cancelling_native_action":"cancelled_pending",revision=Data.Autoplay.Schedule.Revision};
    }
    internal object AgentPlanArchive()=>new{archived=Data.Autoplay.Schedule.Archive(),revision=Data.Autoplay.Schedule.Revision};
    private object QueueLegacyAction(AgentCall call) {
        string actor=call.tool is "companion.assign" or "work.run"?AgentToolRegistry.Text(call.args,"actor_id","player"):"player";
        var previous=Data.Autoplay.Schedule.Tasks.LastOrDefault(t=>t.spec.actor==actor&&!t.Terminal&&t.spec.intent_id==decisionIntent);
        var duplicate=Data.Autoplay.Schedule.Tasks.LastOrDefault(t=>!t.Terminal&&t.spec.actor==actor&&FailureKnowledge.Key(actor,t.spec.tool,t.spec.args.GetRawText())==FailureKnowledge.Key(actor,call.tool,call.args.GetRawText()));
        if(duplicate!=null)return new{status="already_pending",task_id=duplicate.spec.id,duplicate.wait_reason,duplicate.spec.not_before,note="原目标已排队，未重复派单"};
        string location=actor=="player"?ToolLocationContract.Bind(call.tool,Game1.currentLocation.NameOrUniqueName,previous?.spec.tool,previous?.spec.location??"",previous==null?"":AgentToolRegistry.Text(previous.spec.args,"location"),AgentToolRegistry.Text(call.args,"location")):"";
        var task=new AgentTaskSpec{intent_id=decisionIntent,source="model",id="step-"+Guid.NewGuid().ToString("N"),actor=actor,tool=call.tool,args=call.args.Clone(),location=location,day=Game1.Date.TotalDays,purpose=Data.Autoplay.Plan[..Math.Min(160,Data.Autoplay.Plan.Length)]};
        if(previous!=null)task.after.Add(previous.spec.id);
        Data.Autoplay.Schedule.Submit(task.id,Data.Autoplay.Schedule.Revision,new(){task},Game1.Date.TotalDays);
        return new{status="queued",task_id=task.id,actor,revision=Data.Autoplay.Schedule.Revision};
    }
    private void RecordAgentFailure(string code,string actor="decision") {
        if(SurvivalState.Fatal(code)){PauseAutoplay(code);return;}
        if(CapacityState.IsConstraint(code)){RecordCapacityConstraint(code,actor);return;}
        if(RecoveryPolicy.CanWait(code))return;
        SurvivalRecord("local_failure",new{actor,code,day=Game1.Date.TotalDays});
        if(agentFailures.Failed(actor,code))EnterSurvival("sleep","repeated_failure_six:"+actor+":"+code);
    }
    private void CompleteScheduled(ScheduledAgentTask task,JsonElement result) {
        string state=result.TryGetProperty("status",out var status)?status.GetString()??"failed":"failed";
        string? error=result.TryGetProperty("error",out var e)&&e.ValueKind==JsonValueKind.String?e.GetString():null;
        if(state!="succeeded" && state!="cancelled")state="failed";
        var outcome=OperationsPolicy.Outcome(task.spec.tool,result);
        if(CapacityState.IsConstraint(error))RecordCapacityConstraint(error!,task.spec.actor);
        if(!RecoveryPolicy.CanWait(error))LearnActionResult(task,state,error);
        LearnServiceConstraint(task,state,error);
        Data.Autoplay.Operations.LastOutcomes[task.spec.intent_id]=AgentJson.Encode(outcome);
        foreach(var key in Data.Autoplay.Operations.LastOutcomes.Keys.Take(Math.Max(0,Data.Autoplay.Operations.LastOutcomes.Count-64)).ToArray())Data.Autoplay.Operations.LastOutcomes.Remove(key);
        Data.Autoplay.Record("goal_progress",AgentJson.Encode(new{task.spec.id,task.spec.intent_id,task.spec.source,task.spec.actor,outcome}));
        if(state!="succeeded"&&task.spec.goal_id.Length>0) {
            var goal=Data.SharedGoals.FirstOrDefault(g=>g.Id==task.spec.goal_id);
            if(goal!=null){goal.AutoExecute=RecoveryPolicy.CanWait(error);goal.AutoBlockedReason="task_failed:"+task.spec.id+":"+error;
                if(goal.AutoExecute){RefreshFacts(true);goal.AutoBlockedConditions=GoalCondition(goal);goal.AutoReviewDay=Game1.Date.TotalDays;goal.AutoReviewMinute=DailyBudget.Minutes(Game1.timeOfDay);}
            }
        }
        Data.Autoplay.Schedule.Finish(task,state,error,AgentJson.Encode(result));
        if(state=="failed"&&!RecoveryPolicy.CanWait(error))Data.Autoplay.Survival.Abandoned.Add(FailureKnowledge.Key(task.spec.actor,task.spec.tool,task.spec.args.GetRawText()));
        if(task.command_id!=null)agentClaims.Remove(task.command_id);
        Data.Autoplay.Record("action_result",AgentJson.Encode(result));
        Data.Autoplay.Record("task_finished",AgentJson.Encode(new{id=task.spec.id,actor=task.spec.actor,state,error,command_id=task.command_id,capacity_version=Data.Autoplay.Capacity.Version}));
        if(state=="succeeded") {
            bool emptyWork=result.TryGetProperty("deferred",out var deferred)&&deferred.ValueKind==JsonValueKind.True || task.spec.tool=="work.run"&&new[]{"completed","gained","deposited","refills"}.All(k=>!result.TryGetProperty(k,out var n)||n.GetInt32()==0);
            if(outcome.BusinessProgress){agentFailures.Progress(task.spec.actor);agentFailures.Progress("decision");Data.Autoplay.VerifiedActions++;Data.Autoplay.Agenda.EnterDay(Game1.Date.TotalDays);Data.Autoplay.Agenda.CompletedBatches++;}
            else Data.Autoplay.Record(emptyWork?"no_effect_action":"support_action_completed",AgentJson.Encode(new{task.spec.id,task.spec.tool,note="请求已处理，但未增加实际劳动进展"}));
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
        if(TickTaskPreparation())return;
        var schedule=Data.Autoplay.Schedule;
        foreach(var task in schedule.Tasks.Where(t=>t.state=="running").ToArray()) {
            JsonElement result;
            try{result=JsonSerializer.SerializeToElement(AgentReceipt(task.command_id!,false),AgentJson.Options);}
            catch{result=JsonSerializer.SerializeToElement(new{status="failed",error="receipt_unavailable_replan"});}
            if(result.TryGetProperty("status",out var s)&&s.GetString()=="running")continue;
            CompleteScheduled(task,result);
        }
        PrepareServiceWindows();
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
                if(Data.Autoplay.Survival.Abandoned.Contains(FailureKnowledge.Key(task.spec.actor,task.spec.tool,task.spec.args.GetRawText())))throw new InvalidOperationException("known_failure_conditions_unchanged:target_abandoned_today");
                if(task.spec.actor=="player"&&ToolLocationContract.RequiresObservedLocation(task.spec.tool)&&task.spec.location.Length>0 && task.spec.location!=Game1.currentLocation.NameOrUniqueName)throw new InvalidOperationException("planned_location_changed_replan");
                if(task.spec.tool=="player.sleep" && schedule.Tasks.Any(t=>t.state=="running"&&t.spec.actor!="player")) {
                    if(task.wait_reason!="companion_finishing_before_sleep")Data.Autoplay.Record("intent_deferred",AgentJson.Encode(new{task.spec.id,reason="companion_finishing_before_sleep",resume_when="companion_active_work_finished"}));
                    task.wait_reason="companion_finishing_before_sleep";continue;
                }
                if(task.wait_reason=="companion_finishing_before_sleep")task.wait_reason=null;
                if(task.spec.tool=="player.sleep"&&QueueClosingShipment(task))continue;
                if(Data.Business.Enabled&&task.spec.tool is "player.ship" or "player.ship_items"&&!task.spec.id.StartsWith("closing-")&&Game1.timeOfDay<1700&&CapacityAdapter.Of(Game1.player).FreeSlots>0) {
                    CompleteScheduled(task,JsonSerializer.SerializeToElement(new{status="succeeded",deferred=true,note="未出货、未移动；可售物品留到晚间或收工统一交付，不重复请求"}));continue;
                }
                if(!PrepareTaskKit(task))continue;
                if(!AdmitOperation(task))continue;
                CheckKnownFailure(task);
                var result=JsonSerializer.SerializeToElement(agentTools.Execute(task.spec.tool,task.spec.args),AgentJson.Options);
                Data.Autoplay.Record("task_started",AgentJson.Encode(new{id=task.spec.id,task.spec.intent_id,task.spec.source,task.spec.purpose,actor=task.spec.actor,tool=task.spec.tool,result}));
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
        if(NeedsAgentMenuDecision)WakeAgent("menu_choice_required");
    }
}
