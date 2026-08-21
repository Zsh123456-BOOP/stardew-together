using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace Together;
internal static class NativeRewards {
    public static bool IsReward(ItemGrabMenu menu)=>!menu.shippingBin&&menu.source!=ItemGrabMenu.source_chest&&(menu.essential||menu.source is ItemGrabMenu.source_gift or ItemGrabMenu.source_fishingChest or ItemGrabMenu.source_overflow||menu.context is FishingRod or JunimoNoteMenu or StardewValley.Locations.CommunityCenter);
    public static (bool Finished,object Evidence) Step(ItemGrabMenu menu) {
        if(!IsReward(menu))throw new InvalidOperationException("menu_is_not_an_observed_reward");
        if(menu.heldItem!=null) {
            string id=menu.heldItem.QualifiedItemId;int before=menu.heldItem.Stack;
            menu.heldItem=Game1.player.addItemToInventory(menu.heldItem);
            if(menu.heldItem!=null)throw new InvalidOperationException("reward_inventory_full_menu_preserved");
            return(false,new{kind="native_reward_held_item_received",item=id,count=before});
        }
        var inventory=menu.ItemsToGrabMenu.actualInventory;
        int slot=Enumerable.Range(0,Math.Min(inventory.Count,menu.ItemsToGrabMenu.inventory.Count)).FirstOrDefault(i=>inventory[i]!=null,-1);
        if(slot<0) {
            if(inventory.Any(i=>i!=null))throw new InvalidOperationException("reward_pagination_not_visible");
            if(!menu.readyToClose())return(false,new{kind="reward_waiting_for_native_close"});
            menu.exitThisMenu();return(true,new{kind="native_reward_menu_empty"});
        }
        var item=inventory[slot];
        bool unlock=item.IsRecipe||item is StardewValley.Objects.SpecialItem||item.QualifiedItemId is "(O)326" or "(O)102" or "(O)434";
        if(!unlock&&!Game1.player.couldInventoryAcceptThisItem(item))throw new InvalidOperationException("reward_inventory_full_menu_preserved");
        string qid=item.QualifiedItemId;int amount=item.Stack,beforeBag=Game1.player.Items.Where(i=>i?.QualifiedItemId==qid).Sum(i=>i.Stack);
        int beforeSource=inventory.Where(i=>i?.QualifiedItemId==qid).Sum(i=>i.Stack);
        var bounds=menu.ItemsToGrabMenu.inventory[slot].bounds;menu.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
        int remaining=inventory.Where(i=>i?.QualifiedItemId==qid).Sum(i=>i.Stack);
        int added=Game1.player.Items.Where(i=>i?.QualifiedItemId==qid).Sum(i=>i.Stack)-beforeBag;
        if(beforeSource-remaining<=0||!unlock&&added<=0)throw new InvalidOperationException("reward_pickup_not_verified");
        if(item is StardewValley.Objects.SpecialItem special&&!PlayerExecutor.SpecialRewardPresent(special.which.Value))throw new InvalidOperationException("native_special_reward_not_verified");
        return(false,new{kind="native_reward_received",item=qid,source_reduction=beforeSource-remaining,inventory_increase=added,native_unlock_item=unlock});
    }
}
public sealed partial class PlayerExecutor {
    private void StartCollectReward() {
        if(Game1.activeClickableMenu is not ItemGrabMenu menu||!NativeRewards.IsReward(menu))throw new InvalidOperationException("native_reward_menu_required");
        Current!.phase="collecting_reward";
    }
    private void TickCollectReward() {
        if(Game1.activeClickableMenu==null){Finish("succeeded");return;}
        if(Game1.activeClickableMenu is not ItemGrabMenu menu)throw new InvalidOperationException("reward_menu_changed");
        if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(180);
        var step=NativeRewards.Step(menu);Current!.effects.Add(step.Evidence);if(step.Finished)Finish("succeeded");else Current.completed++;
    }
}
