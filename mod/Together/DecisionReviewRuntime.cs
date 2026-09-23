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
        var options=opportunities.Where(o=>o.Tool is "player.service" or "player.social" or "player.collect_home_gifts" or "player.read_mail" or "shop.read" or "farm.select_seeds"||o.Tool=="work.run"&&AgentToolRegistry.Text(JsonSerializer.SerializeToElement(o.Args),"goal") is "water" or "harvest" or "forage" or "fish")
            .Select(o=>new ReviewOption(o.Id,o.Energy,o.Minutes,o.Tool is "player.service" or "shop.read"?"visit_quote_only;buy_or_plant_separately;profit_unknown_until_quote":"shown_action_estimate" )).ToList();
        if(Facts.DryCrops>0&&!options.Any(o=>o.id=="water"))options.Add(new("care:dry_crops",(float)Math.Ceiling(Facts.DryCrops*Math.Max(0,2-.1*Game1.player.FarmingLevel)),30,"watering_estimate;tool_route_not_verified"));
        return options.OrderBy(o=>o.id is "water" or "care:dry_crops" or "harvest"?0:o.id.Contains("seed")||o.id=="inspect:current-shop"?1:o.energy==0?2:3).Take(3).ToArray();
    }
    private object DecisionReviewContext(IEnumerable<OperatingOpportunity> opportunities)=>new {
        sleep_alternatives=SleepAlternatives(opportunities),last_rejection=reviewFailure,
        rule="sleep/agent.pause逐项比较此处最多3项：energy/time须符合当前估算，价值取舍用low_value或defer并说明。报价未知不等于收益为零；询价/采购不含锄地浇水。资源批量劳动显式给reserve_stamina与labor_review；留给后续工作的体力由模型决定。"
    };
    private void RecordDecisionReview(string action,JsonElement args,object facts,string? error) {
        Data.Autoplay.Record("decision_review",AgentJson.Encode(new{action,args,facts,error,accepted=error==null}));
        if(error==null){reviewFailure=null;reviewAttempts.Clear();return;}
        bool stop=reviewAttempts.Reject(Data.Autoplay.RunId+":"+Game1.Date.TotalDays+":"+Data.Autoplay.VerifiedActions);
        reviewFailure=new{action,error,facts,attempt=reviewAttempts.Count,next="按当前事实修正取舍；无需重复补读已有事实"};
        if(stop)decisionBlockedReason="decision_review_failed_three_attempts:"+error;
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
