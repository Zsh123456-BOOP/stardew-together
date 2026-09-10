using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Quests;

namespace Together;
public sealed partial class PlayerExecutor {
    private LostItemQuest? lostQuest;
    private Point lostTile;
    private void StartLostItem(JsonElement args) {
        lostQuest=NativeQuestIdentity.Find(AgentToolRegistry.Text(args,"quest_id")) as LostItemQuest??throw new InvalidOperationException("active_lost_item_quest_required");
        if(lostQuest.completed.Value)throw new InvalidOperationException("quest_already_complete");
        if(lostQuest.itemFound.Value){Finish("succeeded");return;}
        destination=lostQuest.locationOfItem.Value;lostTile=new(lostQuest.tileX.Value,lostQuest.tileY.Value);
        Current!.phase="lost_item_travel";
    }
    private void TickLostItem() {
        if(Game1.locationRequest!=null||Game1.fadeToBlack)return;
        var q=lostQuest!;
        if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} message&&q.itemFound.Value) {
            if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(350);
            message.finishTyping();message.receiveLeftClick(message.xPositionOnScreen+16,message.yPositionOnScreen+16);return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("lost_item_interrupted_by_menu");
        if(q.itemFound.Value) {
            bool owns=Game1.player.Items.Any(i=>i?.QualifiedItemId==q.ItemId.Value);
            Current!.effects.Add(new{kind="native_lost_item_found",quest=NativeQuestIdentity.Id(q),item=q.ItemId.Value,found=q.itemFound.Value,owns});
            Finish(owns?"succeeded":"failed",owns?null:"lost_item_not_in_inventory");return;
        }
        if(!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        // The native OnWarped event spawns the overlay object. Never invoke that
        // callback from observation: this quest's implementation mutates even in probe mode.
        if(!Game1.currentLocation.overlayObjects.TryGetValue(lostTile.ToVector2(),out var item)||item.QualifiedItemId!=q.ItemId.Value)
            throw new InvalidOperationException("native_lost_item_not_spawned_reenter_location");
        CapacityAdapter.RequireReceive(Game1.player,item);
        if(Math.Abs(Game1.player.TilePoint.X-lostTile.X)+Math.Abs(Game1.player.TilePoint.Y-lostTile.Y)>1) {
            if(ownedController==null)Walk(Approach(lostTile,true));else MonitorWalk();return;
        }
        StopWalk();Face(lostTile);
        if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(400);
        if(!Game1.tryToCheckAt(lostTile.ToVector2(),Game1.player))throw new InvalidOperationException("native_lost_item_pickup_rejected");
        Current!.phase="lost_item_verify";
    }
}
