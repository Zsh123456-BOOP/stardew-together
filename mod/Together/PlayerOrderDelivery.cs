using System.Text.Json;
using StardewValley;
using StardewValley.SpecialOrders;
using StardewValley.SpecialOrders.Objectives;

namespace Together;
public sealed partial class PlayerExecutor {
    private SpecialOrder? socialOrder;
    private DeliverObjective? socialOrderObjective;
    private int socialOrderBefore;
    private void BindSocialOrder(JsonElement args) {
        socialOrder=null;socialOrderObjective=null;
        if(socialMode!="order_deliver")return;
        socialOrder=OrderRules.Find(AgentToolRegistry.Text(args,"order_id"))??throw new InvalidOperationException("active_special_order_required");
        int index=AgentToolRegistry.Number(args,"objective",-1);
        if(index<0||index>=socialOrder.objectives.Count||socialOrder.objectives[index] is not DeliverObjective objective||objective.IsComplete()||objective.failOnCompletion.Value)
            throw new InvalidOperationException("active_order_delivery_objective_required");
        if(objective.targetName.Value!=socialName)throw new InvalidOperationException("order_delivery_recipient_mismatch");
        socialOrderObjective=objective;socialOrderBefore=objective.GetCount();
    }
    private void ValidateOrderDelivery(NPC npc,Item item) {
        var objective=socialOrderObjective!;
        if(socialOrder?.questState.Value!=SpecialOrderStatus.InProgress)throw new InvalidOperationException("order_not_active");
        int expected=objective.OnItemDelivered(Game1.player,npc,item,true);
        if(expected<=0)throw new InvalidOperationException("order_requires_full_matching_stack");
        if(Game1.player.questLog.Any(q=>!q.completed.Value&&q.OnItemOfferedToNpc(npc,item,true))||
            Game1.player.team.specialOrders.Where(o=>o!=socialOrder).SelectMany(o=>o.objectives).OfType<DeliverObjective>().Any(o=>o.OnItemDelivered(Game1.player,npc,item,true)>0))
            throw new InvalidOperationException("delivery_matches_other_quest_resolve_priority_first");
        ValidateConsumption?.Invoke(new Dictionary<Item,int>{{item,expected}},"","order:"+socialOrder!.questKey.Value);
    }
}
