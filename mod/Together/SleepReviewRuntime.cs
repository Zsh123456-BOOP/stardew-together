using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private object? sleepReview;
    private string SleepDecisionBasis()=>FailureKnowledge.Hash(AgentJson.Encode(new{day=Game1.Date.TotalDays,time=Game1.timeOfDay,Game1.player.Money,Game1.player.Stamina,Game1.player.health,location=Game1.currentLocation.NameOrUniqueName,weather=Game1.IsRainingHere(),inventory=Game1.player.Items.Select(i=>new{id=i?.QualifiedItemId,i?.Stack,i?.Quality}),stock=Facts.Stock,Data.Autoplay.VerifiedActions}));
    private bool ReviewQueuedSleep(ScheduledAgentTask task) {
        if(task.spec.tool!="player.sleep"||task.spec.source!="model")return false;
        bool changed=task.spec.sleep_review_basis!=SleepDecisionBasis()||task.spec.sleep_review_day!=Game1.Date.TotalDays||task.spec.sleep_review_progress!=Data.Autoplay.VerifiedActions||task.spec.sleep_review_time!=Game1.timeOfDay;
        if(!changed)return false;
        sleepReview=new{task=task.spec.id,planned_day=task.spec.sleep_review_day,planned_time=task.spec.sleep_review_time,
            now_day=Game1.Date.TotalDays,now_time=Game1.timeOfDay,stamina=Game1.player.Stamina,cash=Game1.player.Money,
            reason="先前排定睡觉时的状态已变化；请结合本轮真实候选重新决定继续工作或直接调用player.sleep确认收工。没有固定体力或时间门槛。"};
        Data.Autoplay.Schedule.Finish(task,"cancelled","sleep_reassessment_required",AgentJson.Encode(sleepReview));
        Data.Autoplay.Record("sleep_reassessment",AgentJson.Encode(sleepReview));
        WakeAgent("sleep_reassessment_required");return true;
    }
}
