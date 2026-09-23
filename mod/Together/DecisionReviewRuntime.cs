using System.Text.Json;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private readonly DecisionReviewAttempts reviewAttempts=new();
    private object? reviewFailure;
    private bool reviewingSleep;
    private bool OpportunityPlayerOccupied()=>!reviewingSleep?AgentActorHasWork("player"):WorkActorBusy("player")||playerExecutor.Busy||Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.actor=="player"&&t.spec.tool!="player.sleep"&&Data.Autoplay.Schedule.Readiness(t,Game1.Date.TotalDays,Game1.timeOfDay).State is "running" or "runnable");
    private ReviewOption[] SleepAlternatives(IEnumerable<OperatingOpportunity> opportunities) {
        // Only the shown step is costed: opening a shop is not planting its seeds,
        // and registering a construction goal does not cost zero total energy.
        var options=opportunities.Select(o=> {
            int owned=o.Id.Contains(":owned-seeds:")?OwnedSeeds().Where(i=>o.Id.EndsWith(":"+i.QualifiedItemId)).Sum(i=>i.Stack):0;
            return new ReviewOption(o.Id,owned>0&&o.Tool=="work.run"?null:o.Energy,o.Minutes,owned>0?"owned_seeds;additional_seed_cost=0;defer_planting_and_watering_delays_growth;compare_future_yield_and_care;layout_cost_from_farm.plan":o.Tool is "player.service" or "shop.read"?"visit_quote_only;buy_or_plant_separately;profit_unknown_until_quote":"shown_action_estimate",owned);
        }).ToList();
        if(Facts.DryCrops>0&&!options.Any(o=>o.id=="water"))options.Add(new("care:dry_crops",(float)Math.Ceiling(Facts.DryCrops*Math.Max(0,2-.1*Game1.player.FarmingLevel)),30,"watering_estimate;tool_route_not_verified"));
        return DecisionReviewPolicy.Select(options);
    }
    private object DecisionReviewContext(IEnumerable<OperatingOpportunity> opportunities)=>new {
        sleep_alternatives=SleepAlternatives(opportunities),last_rejection=reviewFailure,
        rule="收工只提交reason和defer（此处候选ID数组，最多3项）；明确接受推迟这些工作的后果。程序已核算时间、体力和自有种子，不再重复填写成本。无即时回款不等于无价值，推迟播种或照料可能延迟成熟。"
    };
    private void RecordDecisionReview(string action,JsonElement args,object facts,string? error) {
        Data.Autoplay.Record("decision_review",AgentJson.Encode(new{action,args,facts,error,accepted=error==null}));
        if(error==null){reviewFailure=null;reviewAttempts.Clear();return;}
        bool stop=reviewAttempts.Reject(Data.Autoplay.RunId+":"+Game1.Date.TotalDays+":"+Data.Autoplay.VerifiedActions+":"+action+":"+error);
        reviewFailure=new{action,error,facts,attempt=reviewAttempts.Count,next=stop?"仅此动作反复失败，停止原样重试，选择其他工作或按当前契约修正参数；全局继续。":"按当前事实修正参数；收工填reason和defer候选ID，不必编写成本论证。"};
        if(stop)Data.Autoplay.Record("decision_review_task_isolated",AgentJson.Encode(new{action,error,attempt=reviewAttempts.Count,global_paused=false}));
        WakeAgent("decision_review_rejected");
        throw new InvalidOperationException(error);
    }
    private void CheckResourceReview(JsonElement args,string item,int owned,int approved,int missing) {
        if(!AutoplayRunning||AgentToolRegistry.Text(args,"actor_id","player")!="player"||missing<=0)return;
        int reserve=AgentToolRegistry.Number(args,"reserve_stamina",-1),pending=PendingFarmEnergy();
        var facts=new{item,owned,approved_target=approved,missing,stamina=Game1.player.Stamina,reserve_stamina=reserve,pending_care_energy=pending,
            spend_ceiling_without_food=reserve<0?(float?)null:Math.Max(0,Game1.player.Stamina-reserve),total_energy_estimate=(float?)null,
            note="总消耗取决于实际节点/工具/掉落，未知不伪造；执行器按原生动作成本和明确预留停工。额外补给受max_food控制。"};
        RecordDecisionReview("resource",args,facts,DecisionReviewPolicy.Resource(args,missing,reserve,pending));
    }
}
