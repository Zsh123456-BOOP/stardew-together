using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;
namespace Together;
public sealed partial class PlayerExecutor {
    internal Action<Item,int>? ValidateDisposal {get;set;}
    private JsonElement discardArgs;
    private Point discardChest;
    private bool discardWalk;
    private void StartDiscard(JsonElement args) {
        discardArgs=args.Clone();string source=AgentToolRegistry.Text(args,"source","backpack");
        if(source is not ("backpack" or "storage")||AgentToolRegistry.Text(args,"reason").Length==0)throw new InvalidOperationException("discard_source_and_reason_required");
        if(source=="backpack"){DiscardNative(null);return;}
        destination=AgentToolRegistry.Text(args,"location");discardChest=new(AgentToolRegistry.Number(args,"x",-1),AgentToolRegistry.Number(args,"y",-1));discardWalk=false;
        if(LoadedLocation(destination)?.objects.GetValueOrDefault(discardChest.ToVector2()) is not Chest)throw new InvalidOperationException("discard_storage_missing");
        Current!.phase="discard_storage_travel";
    }
    private void TickDiscard() {
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(Game1.currentLocation.objects.GetValueOrDefault(discardChest.ToVector2()) is not Chest chest)throw new InvalidOperationException("discard_storage_changed");
        if(!discardWalk){Walk(Approach(discardChest,true));discardWalk=true;Current!.phase="discard_storage_walk";return;}
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Adjacent(discardChest);
        if(Game1.activeClickableMenu==null){
            if(Current!.phase=="discard_storage_open"){if(DateTime.UtcNow>=nextInteraction)throw new InvalidOperationException("discard_storage_open_timeout");return;}
            if(chest.GetMutex().IsLocked())throw new InvalidOperationException("storage_busy");
            if(!NativeMenuInput.InteractWorld(discardChest))throw new InvalidOperationException("discard_storage_open_rejected");
            Current.phase="discard_storage_open";nextInteraction=DateTime.UtcNow.AddSeconds(5);return;
        }
        if(Game1.activeClickableMenu is not ItemGrabMenu menu||menu.context!=chest)throw new InvalidOperationException("discard_storage_menu_changed");
        DiscardNative(chest);
    }
    private void DiscardNative(Chest? chest) {
        var p=Game1.player;var src=chest==null?p.Items:chest.GetItemsForPlayer();int slot=AgentToolRegistry.Number(discardArgs,"slot",-1),count=AgentToolRegistry.Number(discardArgs,"count",0);
        string id=AgentToolRegistry.Text(discardArgs,"item");int quality=AgentToolRegistry.Number(discardArgs,"quality",0),expected=AgentToolRegistry.Number(discardArgs,"expected_stack",-1);
        if(slot<0||slot>=src.Count||src[slot] is not {} item||item.QualifiedItemId!=id||item.Quality!=quality||item.Stack!=expected||count<1||count>item.Stack)throw new InvalidOperationException("discard_stock_changed_read_inventory_capacity");
        if(item is Tool||item is not StardewValley.Object o||o.questItem.Value||o.bigCraftable.Value||!item.canBeTrashed())throw new InvalidOperationException("discard_protected_item");
        ValidateDisposal?.Invoke(item,count);
        object Rows()=>new{backpack=p.Items.Select((i,n)=>new{slot=n,item=i?.QualifiedItemId,quality=i?.Quality,count=i?.Stack??0}).ToArray(),storage=chest?.GetItemsForPlayer().Select((i,n)=>new{slot=n,item=i?.QualifiedItemId,quality=i?.Quality,count=i?.Stack??0}).ToArray()};
        Dictionary<string,int> Totals()=>p.Items.Concat(chest?.GetItemsForPlayer()??Enumerable.Empty<Item>()).Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId+":"+i.Quality).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
        var before=Rows();var totals=Totals();int free=CapacityAdapter.Of(p).FreeSlots,money=p.Money;
        Current!.effects.Add(new{kind="discard_before",before,source=chest==null?"backpack":"storage",item=id,count,reason=AgentToolRegistry.Text(discardArgs,"reason")});
        var kb=Game1.oldKBState;
        try {
            Game1.oldKBState=default;
            if(chest==null) {
                var top=new GameMenu(0);Game1.activeClickableMenu=top;var page=(InventoryPage)top.GetCurrentPage();var bounds=page.inventory.inventory[slot].bounds;
                if(count==item.Stack)page.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
                else for(int n=0;n<count;n++)page.receiveRightClick(bounds.Center.X,bounds.Center.Y);
                if(p.CursorSlotItem?.QualifiedItemId!=id||p.CursorSlotItem.Stack!=count)throw new InvalidOperationException("discard_native_selection_mismatch");
                page.receiveLeftClick(page.trashCan.bounds.Center.X,page.trashCan.bounds.Center.Y);
                if(p.CursorSlotItem!=null)throw new InvalidOperationException("discard_native_trash_rejected");
                top.exitThisMenu();
            }else {
                var menu=(ItemGrabMenu)Game1.activeClickableMenu;var bounds=menu.ItemsToGrabMenu.inventory[slot].bounds;
                // Use the native inventory widget to take the requested units into
                // the menu cursor; do not auto-deliver them into a full backpack.
                if(count==item.Stack)menu.heldItem=menu.ItemsToGrabMenu.leftClick(bounds.Center.X,bounds.Center.Y,null);
                else for(int n=0;n<count;n++)menu.heldItem=menu.ItemsToGrabMenu.rightClick(bounds.Center.X,bounds.Center.Y,menu.heldItem);
                if(menu.heldItem?.QualifiedItemId!=id||menu.heldItem.Stack!=count)throw new InvalidOperationException("discard_native_selection_mismatch");
                menu.receiveLeftClick(menu.trashCan.bounds.Center.X,menu.trashCan.bounds.Center.Y);
                if(menu.heldItem!=null)throw new InvalidOperationException("discard_native_trash_rejected");
                menu.exitThisMenu();
            }
        }finally{Game1.oldKBState=kb;}
        var after=Totals();string key=id+":"+quality;totals[key]-=count;
        bool verified=totals.Where(x=>x.Value!=0).OrderBy(x=>x.Key).SequenceEqual(after.Where(x=>x.Value!=0).OrderBy(x=>x.Key));
        Current.effects.Add(new{kind="native_discard",before,after=Rows(),item=id,quality,destroyed=count,free_slots_before=free,free_slots_after=CapacityAdapter.Of(p).FreeSlots,native_reclamation_gold=p.Money-money,verified});
        if(!verified)throw new InvalidOperationException("loadout_conservation_failed:discard_delta");
        Current.completed=count;Finish("succeeded");
    }
}
