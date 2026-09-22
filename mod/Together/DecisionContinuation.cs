using System.Text.Json;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private sealed class DecisionContinuation {
        public List<AgentCall> Calls=new();public List<string> After=new();public string Intent="";
        public int Day,Revision;public bool Error,Followup;public int Index;
    }
    private DecisionContinuation? deferredDecision;
    private (bool Followup,bool Error) ApplyDecisionCalls(List<AgentCall> calls,int revision) {
        var batch=new DecisionContinuation{Calls=calls,Intent=decisionIntent,Day=Game1.Date.TotalDays,Revision=revision};
        ContinueDecision(batch);return(batch.Followup,batch.Error);
    }
    private void CancelDecisionContinuation(string reason) {
        if(deferredDecision is not {} batch)return;
        deferredDecision=null;Data.Autoplay.ToolExchange.CancelPending(reason);
        Data.Autoplay.Record("decision_continuation_cancelled",AgentJson.Encode(new{batch.Intent,reason,remaining=batch.Calls.Skip(batch.Index),batch.After}));
    }
    private void TickDecisionContinuation() {
        if(deferredDecision is not {} batch)return;
        if(batch.Day!=Game1.Date.TotalDays){CancelDecisionContinuation("day_changed_read_fresh_state");WakeAgent("deferred_day_changed");return;}
        if(NeedsAgentMenuDecision&&(playerExecutor.Busy||DecisionBarrier.State(batch.After.Select(id=>Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==id)?.state))=="waiting")){CancelDecisionContinuation("native_action_requires_menu_choice");WakeAgent("menu_choice_required");return;}
        var pending=batch.After.Select(id=>Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==id)).Where(t=>t!=null&&!t.Terminal).ToArray();
        if(AgentDecisionPacing.ReleaseWindowBarrier(pending.Select(t=>(t!.state,t.spec.not_before>Game1.timeOfDay,t.wait_reason!=null)))) {
            CancelDecisionContinuation("waiting_window_release_for_independent_planning");WakeAgent("waiting_window_needs_other_work");return;
        }
        ContinueDecision(batch);
    }
    private void ContinueDecision(DecisionContinuation batch) {
        string oldIntent=decisionIntent;decisionIntent=batch.Intent;
        try {
            while(batch.Index<batch.Calls.Count) {
                if(!AutoplayRunning||SurvivalOwnsDay){CancelDecisionContinuation("run_interrupted");return;}
                var call=batch.Calls[batch.Index];
                if(call.depends_on_query.Length>0) {
                    if(!Data.Autoplay.Memory.QueryDrafts.Any(d=>d.GetProperty("call").GetRawText()==JsonSerializer.Serialize(call)))Data.Autoplay.Memory.QueryDrafts.Add(JsonSerializer.SerializeToElement(new{batch.Intent,call,day=Game1.Date.TotalDays,executed=false}));
                    batch.Index++;batch.Followup=true;Data.Autoplay.ToolExchange.Record(call.id,new{status="draft_not_executed",reason="read_query_results_then_decide"});
                    Data.Autoplay.Record("query_dependent_draft",AgentJson.Encode(new{batch.Intent,call,reason="read_query_results_then_decide",executed=false}));continue;
                }
                if(QueryResult.IsRead(call.tool,call.args)==false&&call.uses_results.Length>0)Data.Autoplay.Memory.QueryDrafts.RemoveAll(d=>QueryResult.ResolvesDraft(d,call.tool,call.uses_results,Data.Autoplay.Memory.Queries));
                if(call.uses_results.Length>0)Data.Autoplay.Record("query_result_reference",AgentJson.Encode(new{batch.Intent,call.tool,call.uses_results,known=call.uses_results.All(id=>Data.Autoplay.Memory.Queries.Any(q=>q.Id==id&&q.Delivered))}));
                string barrier=DecisionBarrier.State(batch.After.Select(id=>Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==id)?.state));
                if(!DecisionBarrier.Control(call.tool)&&!QueryResult.IsRead(call.tool,call.args)&&!AgentSchedule.Queueable(call.tool)&&barrier=="waiting") {
                    if(deferredDecision!=batch)Data.Autoplay.Record("decision_continuation_wait",AgentJson.Encode(new{batch.Intent,call.tool,batch.After}));
                    deferredDecision=batch;return;
                }
                object result;
                var before=Data.Autoplay.Schedule.Tasks.Select(t=>t.spec.id).ToHashSet();
                try {
                    if((batch.Error||barrier=="failed")&&!DecisionBarrier.CanFollowFailure(call.tool)) {
                        batch.Index++;batch.Followup=true;Data.Autoplay.ToolExchange.Record(call.id,new{status="not_executed",reason="prior_call_failed_requires_fresh_observation"});
                        Data.Autoplay.Record("decision_dependency_not_applied",AgentJson.Encode(new{batch.Intent,call.tool,reason="prior_call_failed_requires_fresh_observation",batch.After}));continue;
                    }
                    var args=call.tool=="plan.submit"?AgentSchedule.RebaseOwnTurn(call.args,batch.Revision,Data.Autoplay.Schedule.Revision):call.args;
                    if(!AgentSchedule.Queueable(call.tool)&&!DecisionBarrier.Control(call.tool))CheckKnownFailure(new ScheduledAgentTask{spec=new(){actor="player",tool=call.tool,args=args}});
                    result=AgentSchedule.Queueable(call.tool)?QueueLegacyAction(call):agentTools.Execute(call.tool,args);
                }catch(Exception e){result=new{status="failed",error=e is InvalidOperationException?e.Message:"tool_exception_"+e.GetType().Name};}
                // Invocation has happened: receipt handling must never replay its side effects next tick.
                batch.Index++;
                var observed=WithBlockedAlternatives(JsonSerializer.SerializeToElement(result,AgentJson.Options));RecordToolAttempt(call,observed);
                if(!AutoplayRunning)return;
                foreach(var task in Data.Autoplay.Schedule.Tasks.Where(t=>!before.Contains(t.spec.id)))if(!batch.After.Contains(task.spec.id))batch.After.Add(task.spec.id);
                if(DecisionBarrier.Text(observed,"task_id") is {} taskId&&!batch.After.Contains(taskId))batch.After.Add(taskId);
                // menu.choose can start a native command directly rather than submit a task.
                if(DecisionBarrier.Text(observed,"status")=="running"&&DecisionBarrier.Text(observed,"command_id") is {} cid) {
                    var id="observe-"+Guid.NewGuid().ToString("N");
                    Data.Autoplay.Schedule.Tasks.Add(new(){spec=new(){id=id,tool=call.tool,args=call.args.Clone(),day=Game1.Date.TotalDays,intent_id=batch.Intent},state="running",command_id=cid});
                    batch.After.Add(id);
                }
                if(DecisionBarrier.Text(observed,"error") is {} error) {
                    RecordAgentFailure(error);batch.Followup=true;batch.Error=true;
                    Data.Autoplay.Record("decision_independent_tail_review",AgentJson.Encode(new{batch.Intent,failed_tool=call.tool,remaining=batch.Calls.Skip(batch.Index),note="独立工具逐项检查；真实依赖仍不执行"}));
                    if(!AutoplayRunning)return;
                }
                if(DecisionBarrier.Text(observed,"status")=="queued_with_rejections")batch.Followup=true;
                if(!AgentSchedule.Queueable(call.tool)&&call.tool is not ("plan.submit" or "agent.wait" or "agent.pause"))batch.Followup=true;
            }
            if(deferredDecision==batch){deferredDecision=null;Data.Autoplay.Record("decision_continuation_finished",AgentJson.Encode(new{batch.Intent,batch.Error}));WakeAgent("deferred_tool_results");}
        }catch(Exception e){
            batch.Error=true;deferredDecision=null;
            Data.Autoplay.Record("decision_receipt_failed",AgentJson.Encode(new{batch.Intent,next_index=batch.Index,error=e.ToString(),remaining=batch.Calls.Skip(batch.Index)}));
            PauseAutoplay("decision_receipt_failed_preserve_evidence");
        }finally{decisionIntent=oldIntent;}
    }
}
