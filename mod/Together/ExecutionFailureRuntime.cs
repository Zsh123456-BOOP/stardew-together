namespace Together;
public sealed partial class ModEntry {
    private static bool RecoverableExecutionFailure(string reason)=>ExecutionFailureWatch.CorrectableInput(reason)||FailureKnowledge.Family(reason) is "capacity" or "access" or "targets" or "material_policy"||reason.StartsWith("known_failure_conditions_unchanged:")||reason=="resource_item_has_no_known_native_node_route"||reason.StartsWith("fish_trip_invalid_")||reason.StartsWith("fish_requires_")||reason.StartsWith("fish_item_")||reason.StartsWith("target_fish_")||reason.StartsWith("fishing_stalled:")||reason.StartsWith("plan_on_farm_or_greenhouse_first_travel_to_")||reason is "path_stalled" or "no_path" or "action_timeout" or "remaining_targets_unreachable";
    private readonly ExecutionFailureWatch executionFailures=new();
    private void ObserveExecutionFailure(string attempt,string actor,string? cause,string source) {
        if(cause==null||!AutoplayRunning||cause.StartsWith("decision_review:"))return;
        int count=executionFailures.Observe(Data.Autoplay.RunId,attempt,actor,cause,Data.Autoplay.Capacity.Version);
        Data.Autoplay.Record("execution_failure_count",AgentJson.Encode(new{attempt_id=attempt,actor,root_cause=cause,capacity_version=Data.Autoplay.Capacity.Version,count,source}));
        if(count<3)return;
        Data.Autoplay.Record(RecoverableExecutionFailure(cause)?"execution_failure_task_isolated":"execution_failure_stop",AgentJson.Encode(new{attempt_id=attempt,actor,root_cause=cause,count,source,note="三次失败后隔离可恢复任务；仅不可恢复故障暂停全局"}));
        if(RecoverableExecutionFailure(cause)) {
            if(ExecutionFailureWatch.PurchaseInput(cause)&&!playerExecutor.Busy&&!WorkActorBusy("player")&&StardewValley.Game1.activeClickableMenu is StardewValley.Menus.ShopMenu shop&&shop.heldItem==null&&shop.readyToClose()) {
                shop.exitThisMenu();
                Data.Autoplay.Record("purchase_review_released",AgentJson.Encode(new{cause,action="closed_idle_shop_without_buying",next="采购步骤已失败，可改做独立工作；重新采购需重新观察报价"}));
            }
            Data.Autoplay.Record("task_recovery",AgentJson.Encode(new{actor,cause,action="skip_failed_task_reobserve_conditions_choose_alternative",night=StardewValley.Game1.timeOfDay>=2100}));
            WakeAgent("recoverable_task_failed_choose_alternative");
            if(actor=="player"&&StardewValley.Game1.timeOfDay>=2100&&!playerExecutor.Busy&&!WorkActorBusy("player")) {
                try{QueueLegacyAction(new(){tool="player.sleep",args=System.Text.Json.JsonSerializer.SerializeToElement(new{reason="夜间非必要任务反复失败，跳过收尾，正常返家睡觉",review="失败任务保留证据和恢复条件，次日重新安排"})});}
                catch(InvalidOperationException e){Data.Autoplay.Record("recovery_sleep_deferred",e.Message);}
            }
            return;
        }
        PauseAutoplay("same_root_three_distinct_attempts:"+actor+":"+cause);
    }
}
