using System.Text.Json;
using StardewValley;
using StardewValley.Menus;
using StardewValley.SpecialOrders;

namespace Together;
public sealed partial class PlayerExecutor {
    internal static object ReadQuestBoard() {
        if(Game1.activeClickableMenu is Billboard billboard) {
            var q=Game1.questOfTheDay;
            return new{kind="daily",available=billboard.acceptQuestButton.visible,quest=q==null?null:new{id=NativeQuestIdentity.Id(q),title=q.questTitle,description=q.questDescription,objective=q.currentObjective,accepted=q.accepted.Value}};
        }
        if(Game1.activeClickableMenu is SpecialOrdersBoard orders) {
            object? Entry(SpecialOrder? order,bool available)=>order==null?null:new{id=order.questKey.Value,title=order.GetName(),description=order.GetDescription(),deadline=order.dueDate.Value,available,objectives=order.objectives.Select(o=>new{type=o.GetType().Name,description=o.GetDescription(),current=o.GetCount(),required=o.GetMaxCount()})};
            return new{kind="special",board=orders.boardType,left=Entry(orders.leftOrder,orders.acceptLeftQuestButton.visible),right=Entry(orders.rightOrder,orders.acceptRightQuestButton.visible)};
        }
        throw new InvalidOperationException("native_quest_board_required");
    }
    private void StartQuestAcceptance(JsonElement args) {
        string id=AgentToolRegistry.Text(args,"id");
        if(Game1.activeClickableMenu is Billboard board) {
            var q=Game1.questOfTheDay;
            if(q==null||NativeQuestIdentity.Id(q)!=id||!board.acceptQuestButton.visible||q.accepted.Value)throw new InvalidOperationException("observed_daily_quest_unavailable");
            var bounds=board.acceptQuestButton.bounds;board.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
            if(!q.accepted.Value||!Game1.player.questLog.Contains(q))throw new InvalidOperationException("native_daily_quest_acceptance_not_verified");
            Current!.effects.Add(new{kind="native_daily_quest_accepted",id,deadline=Game1.Date.TotalDays+q.daysLeft.Value});board.exitThisMenu();
        }else if(Game1.activeClickableMenu is SpecialOrdersBoard special) {
            var choice=special.leftOrder?.questKey.Value==id?special.acceptLeftQuestButton:special.rightOrder?.questKey.Value==id?special.acceptRightQuestButton:null;
            if(choice==null||!choice.visible||Game1.player.team.specialOrders.Any(o=>o.questKey.Value==id))throw new InvalidOperationException("observed_special_order_unavailable");
            special.receiveLeftClick(choice.bounds.Center.X,choice.bounds.Center.Y);
            var actual=Game1.player.team.specialOrders.FirstOrDefault(o=>o.questKey.Value==id)??throw new InvalidOperationException("native_special_order_acceptance_not_verified");
            Current!.effects.Add(new{kind="native_special_order_accepted",id,deadline=actual.dueDate.Value,board=special.boardType});special.exitThisMenu();
        }else throw new InvalidOperationException("native_quest_board_required");
        Current.completed=1;Finish("succeeded");
    }
}
