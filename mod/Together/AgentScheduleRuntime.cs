using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private bool agentLabProbe;
    private string decisionIntent="";
    private readonly List<string> agentWakeReasons=new();
    private long agentRequestEpoch;
    private int agentRequestDay;
    private int agentRequestQueueRevision;
    private bool agentNeedsDecision=true;
    private string agentEventSignature="";
    private DateTime agentObserveAt;
    private bool agentWasInDanger;
    private readonly HashSet<string> agentKnownActors=new(){"player"};
    private string agentIdleSignature="";
    private bool AgentActorHasWork(string actor)=>OperationActorOccupied(actor);
    private bool AgentPlayerCovered()=>AgentActorHasWork("player")||Data.FarmInvestment.Enabled&&Data.FarmInvestment.Phase=="planning";
    private bool AgentWorkCovered()=>AgentPlayerCovered()&&ActiveActors.Where(a=>a!="player").All(AgentActorHasWork);
    private void WakeAgent(string reason) {
        if(!agentWakeReasons.Contains(reason))agentWakeReasons.Add(reason);
        if(agentWakeReasons.Count>16)agentWakeReasons.RemoveAt(0);
        agentNeedsDecision=true;agentRequestedWait=DateTime.MinValue;agentNext=DateTime.UtcNow;
    }
    private object TaskReadiness(ScheduledAgentTask task) {var r=Data.Autoplay.Schedule.Readiness(task,Game1.Date.TotalDays,Game1.timeOfDay);return new{state=r.State,reason=r.Reason,next_time=r.Next,day=task.spec.day};}
    internal object AgentPlanRead(bool compact=false)=>new {
        revision=Data.Autoplay.Schedule.Revision,event_version=Data.Autoplay.Schedule.EventVersion,
        tasks=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).Select(t=>new{spec=compact?(object)new{t.spec.id,t.spec.actor,t.spec.tool,t.spec.args,t.spec.after,t.spec.sequence_after,t.spec.not_before,t.spec.deadline,t.spec.day}:t.spec,t.state,t.command_id,t.error,t.wait_reason,readiness=TaskReadiness(t)}),
        waiting_query_drafts=compact?null:Data.Autoplay.Memory.QueryDrafts,
        recent_results=Data.Autoplay.Schedule.Tasks.Where(t=>t.Terminal).TakeLast(8).Select(t=>new{id=t.spec.id,actor=t.spec.actor,tool=t.spec.tool,t.state,t.error,receipt=ReceiptSummary(t.receipt),details=new{tool="action.status",args=new{id=t.spec.id}}}),
        note="同一角色只能执行一个任务；after声明真正的前置条件；sequence_after仅保持执行顺序，前一步失败不影响独立工作。未来时间的任务不会挡住其他就绪工作。actor统一选择角色，args.actor_id省略时自动继承，显式冲突才拒绝。排队不是成功。换日/中断/失败须核验真实状态，不能重放旧坐标或菜单。"
    };
    internal object AgentPlanSubmit(JsonElement args) {
        var list=args.TryGetProperty("tasks",out var tasks)?JsonSerializer.Deserialize<List<AgentTaskSpec>>(tasks.GetRawText()):null;
        if(list==null)throw new InvalidOperationException("tasks_required");
        if(list.Any(s=>s?.tool=="plan.submit"))return new{status="failed",error="invalid_plan_task_nested_submit",template=new{tool="player.procure",args=new{location="observed location",shop="observed shop id",item="quoted QID",count=1,max_unit_price="observed price",budget="explicit allowance",keep_gold="explicit reserve"}},note="tasks 中放实际执行工具，不能把 plan.submit 再嵌套为动作；先 player.service→shop.read 取得真实报价"};
        foreach(var spec in list.Where(s=>s!=null)) {
            if(!AgentSchedule.Queueable(spec.tool))return new{status="failed",error="plan_query_requires_observation",rejected_task=new{spec.id,spec.tool},submitted_count=0,revision=Data.Autoplay.Schedule.Revision,note="该工具返回需要读取的观察结果，不能放入动作队列。先直接调用查询，按真实结果提交执行工具；本次没有提交任何步骤。"};
            if(SinglePlayerMode&&spec.actor!="player")throw new InvalidOperationException("single_player_actor_required");
            if(spec.tool=="companion.assign" || spec.tool=="work.run"&&spec.actor!="player") {
                // All NPC task lanes must name an actor actually observed in this save.
                if(!World().GetProperty("actors").EnumerateArray().Any(a=>a.GetProperty("id").GetString()==spec.actor))throw new InvalidOperationException("actor_not_recruited");
            }
        }
        bool added;try{added=Data.Autoplay.Schedule.Submit(AgentToolRegistry.Text(args,"submission_id"),AgentToolRegistry.Number(args,"expected_revision",-1),list,Game1.Date.TotalDays,retainIndependent:true);}
        catch(PlanStepRejected e) {
            var rejected=list.First(t=>t.id==e.TaskId);
            return new{status="failed",error=e.Message,rejected_task=new{rejected.id,rejected.tool,rejected.args,rejected.after,rejected.purpose},unsubmitted=list.Select(t=>new{t.id,t.tool,t.after}),revision=Data.Autoplay.Schedule.Revision,submitted_count=0,
                note="整份提交未执行、未占用物资。仅该步骤的前置检查失败；保留独立步骤重新提交，真实after依赖不能删除冒充满足。按原始阻碍条件变化后重查。"};
        }
        var submitted=Data.Autoplay.Schedule.Tasks.Where(t=>list.Any(s=>s.id==t.spec.id)).ToArray();
        foreach(var rejected in submitted.Where(t=>t.state=="blocked"&&t.error!=null))if(added) {
            RefreshCapacityVersion();
            Data.Autoplay.Record("task_declaration_rejected",AgentJson.Encode(new{attempt_id=rejected.spec.id,rejected.spec.tool,rejected.error}));
            ObserveExecutionFailure(rejected.spec.id,rejected.spec.actor,rejected.error,"declaration");
        }
        return new{status=added?(submitted.Any(t=>t.state=="blocked")?"queued_with_rejections":"queued"):"already_submitted",revision=Data.Autoplay.Schedule.Revision,
            ids=submitted.Where(t=>t.state=="queued").Select(t=>t.spec.id),rejected=submitted.Where(t=>t.state=="blocked").Select(t=>new{t.spec.id,t.spec.tool,t.error,t.spec.after}),
            note="排队不是完成；独立任务保留，受阻步骤及真实after后继不得冒充满足。sequence_after只表示执行顺序，前步结束后可检查自身条件继续。"};
    }
    internal object AgentPlanCancel(JsonElement args) {
        var ids=args.TryGetProperty("ids",out var raw)?JsonSerializer.Deserialize<List<string>>(raw.GetRawText()):null;
        if(ids==null||ids.Count is <1 or >48)throw new InvalidOperationException("task_ids_required");
        var tasks=ids.Distinct().Select(id=>Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==id)??throw new InvalidOperationException("unknown_task_id")).ToArray();
        if(decisionIntent.Length>0&&tasks.Any(t=>t.state=="running")&&!agentWasInDanger)throw new InvalidOperationException("active_plan_requires_changed_precondition_or_user_cancellation");
        string cancelledBy=decisionIntent.Length>0?"model":"user_or_external_request";
        foreach(var t in tasks.Where(t=>t.state=="running")) {
            t.cancellation_requested_by=cancelledBy;
            var result=JsonSerializer.SerializeToElement(AgentReceipt(t.command_id!,true),AgentJson.Options);
            if(result.TryGetProperty("status",out var s)&&s.GetString()=="running"){t.error="cancellation_pending_native_action";continue;}
            string status=result.TryGetProperty("status",out var ended)&&ended.GetString()=="succeeded"?"succeeded":"cancelled";
            Data.Autoplay.Schedule.Finish(t,status,"model_cancel_requested",AgentJson.Encode(result));agentClaims.Remove(t.command_id!);
        }
        Data.Autoplay.Schedule.CancelPending(tasks.Where(t=>t.state!="running").Select(t=>t.spec.id),"cancelled_by_"+cancelledBy);return new{status=tasks.Any(t=>t.state=="running")?"cancelling_native_action":"cancelled_pending",revision=Data.Autoplay.Schedule.Revision};
    }
    internal object AgentPlanArchive()=>new{archived=Data.Autoplay.Schedule.Archive(),revision=Data.Autoplay.Schedule.Revision};
    private object QueueLegacyAction(AgentCall call) {
        if(call.tool is "player.buy" or "player.procure"&&decisionIntent.Length>0&&seedSelectionIntent==decisionIntent&&HasSeedSelection&&selectedSeeds.TryGetValue(AgentToolRegistry.Text(call.args,"item"),out int selected)&&AgentToolRegistry.Number(call.args,"count",1)<=selected&&Data.FarmInvestment.Phase is "start_planning" or "planning" or "executing") {
            var receipt=new{status="already_pending",source="same_decision_seed_selection",purchased=false,selected_count=selected,note="本轮选品已经创建采购播种链；未重复购买。进度由farm.business_status返回。"};
            Data.Autoplay.Record("purchase_duplicate_coalesced",AgentJson.Encode(receipt));return receipt;
        }
        string actor=call.tool is "companion.assign" or "work.run"?AgentToolRegistry.Text(call.args,"actor_id","player"):"player";
        var previous=Data.Autoplay.Schedule.Tasks.LastOrDefault(t=>t.spec.actor==actor&&!t.Terminal&&t.spec.intent_id==decisionIntent);
        var duplicate=Data.Autoplay.Schedule.Tasks.LastOrDefault(t=>!t.Terminal&&t.spec.actor==actor&&FailureKnowledge.Key(actor,t.spec.tool,t.spec.args.GetRawText())==FailureKnowledge.Key(actor,call.tool,call.args.GetRawText()));
        if(duplicate!=null)return new{status="already_pending",task_id=duplicate.spec.id,duplicate.wait_reason,duplicate.spec.not_before,note="原目标已排队，未重复派单"};
        string location=actor=="player"?ToolLocationContract.Bind(call.tool,Game1.currentLocation.NameOrUniqueName,previous?.spec.tool,previous?.spec.location??"",previous==null?"":AgentToolRegistry.Text(previous.spec.args,"location"),AgentToolRegistry.Text(call.args,"location")):"";
        var task=new AgentTaskSpec{intent_id=decisionIntent,source="model",id="step-"+Guid.NewGuid().ToString("N"),actor=actor,tool=call.tool,args=call.args.Clone(),location=location,day=Game1.Date.TotalDays,purpose=Data.Autoplay.Plan[..Math.Min(160,Data.Autoplay.Plan.Length)]};
        if(previous!=null){if(DecisionBarrier.CanFollowFailure(call.tool))task.sequence_after.Add(previous.spec.id);else task.after.Add(previous.spec.id);}
        Data.Autoplay.Schedule.Submit(task.id,Data.Autoplay.Schedule.Revision,new(){task},Game1.Date.TotalDays);
        return new{status="queued",task_id=task.id,actor,revision=Data.Autoplay.Schedule.Revision};
    }
    private void RecordAgentFailure(string code,string actor="decision") {
        if(SurvivalState.Fatal(code)){PauseAutoplay(code);return;}
        if(CapacityState.IsConstraint(code)){RecordCapacityConstraint(code,actor);return;}
        if(RecoveryPolicy.CanWait(code))return;
        SurvivalRecord("local_failure",new{actor,code,day=Game1.Date.TotalDays});
        WakeAgent("local_failure_reobserve:"+actor+":"+code);
    }
    private void CaptureScheduledOutcome(ScheduledAgentTask task) {
        var receipt=string.IsNullOrEmpty(task.receipt)?new System.Text.Json.Nodes.JsonObject():System.Text.Json.Nodes.JsonNode.Parse(task.receipt!) as System.Text.Json.Nodes.JsonObject??new();
        receipt["status"]=task.state;receipt["task_id"]=task.spec.id;receipt["intent_id"]=task.spec.intent_id;receipt["actor"]=task.spec.actor;
        if(task.cancellation_requested_by.Length>0)receipt["cancellation_requested_by"]=task.cancellation_requested_by;
        if(receipt["stop_reason"]==null)receipt["stop_reason"]=task.error;
        if(receipt["resume_policy"]==null)receipt["resume_policy"]="reobserve_then_submit_remaining_work_no_blind_replay";
        FinalizeCashReservation(task,JsonSerializer.SerializeToElement(receipt));
        CaptureObservation(task.spec.tool,JsonSerializer.SerializeToElement(receipt),"outcome",args:task.spec.args);
        WakeAgent("task_terminal:"+task.spec.id);
    }
    private void CompleteScheduled(ScheduledAgentTask task,JsonElement result) {
        if(task.spec.goal_id.Length>0)goalAutomationAt=DateTime.MinValue;
        string state=result.TryGetProperty("status",out var status)?status.GetString()??"failed":"failed";
        string? error=result.TryGetProperty("error",out var e)&&e.ValueKind==JsonValueKind.String?e.GetString():null;
        if(state is not("succeeded" or "cancelled" or "partial" or "blocked"))state="failed";
        if(state=="failed")RefreshCapacityVersion();
        var outcome=OperationsPolicy.Outcome(task.spec.tool,result);
        if(CapacityState.IsConstraint(error))RecordCapacityConstraint(error!,task.spec.actor);
        if(state!="partial")LearnActionResult(task,state,error);
        LearnServiceConstraint(task,state,error);
        Data.Autoplay.Operations.LastOutcomes[task.spec.intent_id]=AgentJson.Encode(outcome);
        foreach(var key in Data.Autoplay.Operations.LastOutcomes.Keys.Take(Math.Max(0,Data.Autoplay.Operations.LastOutcomes.Count-64)).ToArray())Data.Autoplay.Operations.LastOutcomes.Remove(key);
        Data.Autoplay.Record("goal_progress",AgentJson.Encode(new{task.spec.id,task.spec.intent_id,task.spec.source,task.spec.actor,outcome}));
        if(state!="succeeded"&&task.spec.goal_id.Length>0) {
            var goal=Data.SharedGoals.FirstOrDefault(g=>g.Id==task.spec.goal_id);
            if(goal!=null){if(CapacityState.IsCapacity(error)){goal.CapacityBlockedTool=task.spec.tool;goal.CapacityBlockedArgs=task.spec.args.GetRawText();}
                goal.AutoBlockedReason="task_failed:"+task.spec.id+":"+error;
                if(goal.AutoExecute){RefreshFacts(true);goal.AutoBlockedConditions=GoalCondition(goal);goal.AutoReviewDay=Game1.Date.TotalDays;goal.AutoReviewMinute=DailyBudget.Minutes(Game1.timeOfDay);}
            }
        }
        result=WithBlockedAlternatives(result);
        Data.Autoplay.Schedule.Finish(task,state,error,AgentJson.Encode(result));
        // FailureKnowledge is the sole retry authority; a day-long hash blacklist cannot observe release conditions.
        if(task.command_id!=null)agentClaims.Remove(task.command_id);
        Data.Autoplay.Record("action_result",AgentJson.Encode(result));
        Data.Autoplay.Record("task_finished",AgentJson.Encode(new{attempt_id=task.spec.id,id=task.spec.id,actor=task.spec.actor,state,error,command_id=task.command_id,capacity_version=Data.Autoplay.Capacity.Version}));
        if(state=="failed")ObserveExecutionFailure(task.spec.id,task.spec.actor,error,"task_finished");
        if(!AutoplayRunning)return;
        if(state is "succeeded" or "partial") {
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
        Data.Autoplay.Schedule.Finished=CaptureScheduledOutcome;
        long stage=System.Diagnostics.Stopwatch.GetTimestamp();
        bool preparing=TickTaskPreparation();FrameStage("schedule.preparation",ref stage);if(preparing)return;
        var schedule=Data.Autoplay.Schedule;
        foreach(var task in schedule.Tasks.Where(t=>t.state=="running").ToArray()) {
            JsonElement result;
            try{result=JsonSerializer.SerializeToElement(AgentReceipt(task.command_id!,false),AgentJson.Options);}
            catch{result=JsonSerializer.SerializeToElement(new{status="failed",error="receipt_unavailable_replan"});}
            if(result.TryGetProperty("status",out var s)&&s.GetString()=="running")continue;
            CompleteScheduled(task,result);
        }
        FrameStage("schedule.receipts",ref stage);PrepareServiceWindows();FrameStage("schedule.services",ref stage);
        if(!AutoplayRunning || Game1.eventUp || Game1.fadeToBlack || Game1.locationRequest!=null)return;
        foreach(var sleeping in schedule.Tasks.Where(t=>t.wait_reason=="companion_finishing_before_sleep"))if(!schedule.Tasks.Any(t=>t.state=="running"&&t.spec.actor!="player"))sleeping.wait_reason=null;
        long version=schedule.EventVersion;
        var ready=schedule.Ready(Game1.Date.TotalDays,Game1.timeOfDay,SpatialOrder,WorkActorBusy);
        FrameStage("schedule.readiness_routes",ref stage);
        if(schedule.EventVersion!=version)WakeAgent("expired_or_failed_dependency");
        foreach(var task in ready) {
            if(!AutoplayRunning)break;
            // Maintenance and cargo-support jobs may own an actor outside this
            // queue. Wait for the whole semantic job, not just its current swing.
            if(WorkActorBusy(task.spec.actor))continue;
            CloseShopForDeparture(task);
            bool purchasing=PlayerExecutor.AcceptsNativeMenu(task.spec.tool);
            if(task.spec.actor=="player" && (playerExecutor.Busy || Game1.activeClickableMenu!=null&&!purchasing || !Game1.player.CanMove&&!purchasing || Game1.player.UsingTool))continue;
            try {
                if(task.spec.actor=="player"&&ToolLocationContract.RequiresObservedLocation(task.spec.tool)&&task.spec.location.Length>0 && task.spec.location!=Game1.currentLocation.NameOrUniqueName)throw new InvalidOperationException("planned_location_changed_replan");
                if(task.spec.tool=="player.sleep" && schedule.Tasks.Any(t=>t.state=="running"&&t.spec.actor!="player")) {
                    if(task.wait_reason!="companion_finishing_before_sleep")Data.Autoplay.Record("intent_deferred",AgentJson.Encode(new{task.spec.id,reason="companion_finishing_before_sleep",resume_when="companion_active_work_finished"}));
                    task.wait_reason="companion_finishing_before_sleep";continue;
                }
                if(task.wait_reason=="companion_finishing_before_sleep")task.wait_reason=null;
                if(ReviewQueuedSleep(task))continue;
                if(task.spec.tool=="player.sleep")sleepReview=null;
                if(task.spec.tool=="work.run"&&AgentToolRegistry.Text(task.spec.args,"goal")=="water"&&WateringNoWork(task.spec.args) is {} noWater) {CompleteScheduled(task,JsonSerializer.SerializeToElement(noWater,AgentJson.Options));continue;}
                if(task.spec.tool=="player.social"&&SocialPreflight(task.spec.args) is {} access) {CompleteScheduled(task,JsonSerializer.SerializeToElement(access,AgentJson.Options));continue;}
                FrameStage("schedule.preflight",ref stage);
                if(!PrepareTaskKit(task)){FrameStage("schedule.kit",ref stage);continue;}
                FrameStage("schedule.kit",ref stage);
                if(!AdmitOperation(task))continue;
                CheckKnownFailure(task);
                RecordSpatialDispatch(task);FrameStage("schedule.admission",ref stage);
                var result=JsonSerializer.SerializeToElement(agentTools.Execute(task.spec.tool,task.spec.args),AgentJson.Options);
                FrameStage("schedule.native_dispatch",ref stage);
                Data.Autoplay.Record("task_started",AgentJson.Encode(new{id=task.spec.id,task.spec.intent_id,task.spec.source,task.spec.purpose,actor=task.spec.actor,tool=task.spec.tool,result}));
                if(result.TryGetProperty("status",out var s)&&s.GetString()=="running"&&result.TryGetProperty("command_id",out var id))schedule.Started(task,id.GetString()!);
                else CompleteScheduled(task,result);
            }catch(Exception e){CompleteScheduled(task,JsonSerializer.SerializeToElement(new{status="failed",error=e is InvalidOperationException?e.Message:"executor_"+e.GetType().Name}));}
        }
    }
    private void ObserveAgentEvents() {
        if(DateTime.UtcNow<agentObserveAt)return;agentObserveAt=DateTime.UtcNow.AddSeconds(1);
        string idle=string.Join(",",ActiveActors.OrderBy(x=>x).Where(actor=>!AgentActorHasWork(actor)));
        if(idle!=agentIdleSignature){agentIdleSignature=idle;if(idle.Length>0)WakeAgent("idle_actors:"+idle);}
        bool danger=Game1.player.health<35;
        if(danger&&!agentWasInDanger){agentGeneration++;WakeAgent("danger");} // Discard an in-flight plan based on a previously safe state.
        agentWasInDanger=danger;
        string signature=$"{Game1.Date.TotalDays}:{Game1.timeOfDay>=2200}:{Game1.player.health<35}:{Game1.player.Stamina<20}:{(playerExecutor.Busy?"owned":Game1.activeClickableMenu?.GetType().Name)}:{Game1.eventUp}:{Game1.currentMinigame?.GetType().Name}";
        if(signature!=agentEventSignature){agentEventSignature=signature;WakeAgent("environment_changed");}
        if(NeedsAgentMenuDecision)WakeAgent("menu_choice_required");
    }
}
