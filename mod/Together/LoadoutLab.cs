using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class ModEntry {
    // Test setup uses the native inventory menu's split/pick/place handlers on
    // existing items only. No seeded inventory, stack assignment or save edits.
    private object LabPackNearFull() {
        if(!Settings.EnableLab||!Context.IsWorldReady||Game1.player.Name!="AgentLab")throw new InvalidOperationException("isolated_lab_required");
        if(playerExecutor.Busy||preparation!=null||Game1.activeClickableMenu!=null)throw new InvalidOperationException("probe_requires_idle");
        var s=SharedStorage().FirstOrDefault(s=>s.Location==Game1.currentLocation&&Vector2.Distance(s.Tile,Game1.player.Tile)<=2);
        if(s.Chest==null||s.Chest.GetMutex().IsLocked())throw new InvalidOperationException("adjacent_unlocked_chest_required");
        var before=KitInventoryEvidence(s.Chest);var bag=Game1.player.Items;var stock=s.Chest.GetItemsForPlayer();
        var playerMenu=new InventoryMenu(0,0,true,bag,capacity:Game1.player.MaxItems,rows:3);
        // Native InventoryMenu indexes existing slots; null padding represents
        // the chest's existing capacity, never new items or extra capacity.
        while(stock.Count<s.Chest.GetActualCapacity())stock.Add(null);
        var chestMenu=new InventoryMenu(0,300,false,stock,capacity:s.Chest.GetActualCapacity(),rows:3);
        Item? held=null;int moves=0;
        void MoveOne(InventoryMenu from,int source,InventoryMenu to,int destination) {
            var a=from.inventory[source].bounds.Center;var b=to.inventory[destination].bounds.Center;
            held=from.rightClick(a.X,a.Y,held,false);
            if(held==null)throw new InvalidOperationException("native_split_returned_empty");
            held=to.leftClick(b.X,b.Y,held,false);moves++;
            if(held!=null){held=from.leftClick(a.X,a.Y,held,false);if(held!=null){PauseAutoplay("loadout_conservation_failed:lab_held_item");throw new InvalidOperationException("lab_held_item_restore_failed");}throw new InvalidOperationException("native_place_rejected_source_restored");}
        }
        int Empty(StardewValley.Inventories.IInventory items,int size){for(int n=0;n<size;n++)if(n>=items.Count||items[n]==null)return n;return -1;}
        // Bank the watering can first so the next water task genuinely needs a withdrawal.
        int can=bag.ToList().FindIndex(i=>i is StardewValley.Tools.WateringCan);
        if(can>=0)MoveOne(playerMenu,can,chestMenu,Empty(stock,s.Chest.GetActualCapacity()));
        while(stock.Count(i=>i!=null)<s.Chest.GetActualCapacity()-2) {
            int source=bag.ToList().FindIndex(i=>i!=null&&i.Stack>1);
            if(source>=0)MoveOne(playerMenu,source,chestMenu,Empty(stock,s.Chest.GetActualCapacity()));
            else {int own=stock.ToList().FindIndex(i=>i!=null&&i.Stack>1);if(own<0)throw new InvalidOperationException("not_enough_native_units_for_near_full_probe");MoveOne(chestMenu,own,chestMenu,Empty(stock,s.Chest.GetActualCapacity()));}
        }
        while(Empty(bag,Game1.player.MaxItems)>=0) {
            int source=bag.ToList().FindIndex(i=>i!=null&&i.Stack>1);
            if(source>=0)MoveOne(playerMenu,source,playerMenu,Empty(bag,Game1.player.MaxItems));
            else {int index=stock.ToList().FindIndex(i=>i is StardewValley.Object&&i.Stack>1);if(index<0)index=stock.ToList().FindIndex(i=>i is StardewValley.Object);if(index<0)break;MoveOne(chestMenu,index,playerMenu,Empty(bag,Game1.player.MaxItems));}
        }
        var after=KitInventoryEvidence(s.Chest);bool conserved=KitTotals(before).OrderBy(x=>x.Key).SequenceEqual(KitTotals(after).OrderBy(x=>x.Key));
        Data.Autoplay.Record("lab_native_inventory_repack",AgentJson.Encode(new{before,after,moves,conserved,held=held?.QualifiedItemId}));
        if(!conserved){PauseAutoplay("loadout_conservation_failed:lab_native_repack");throw new InvalidOperationException("loadout_conservation_failed");}
        return new{before,after,moves,conserved,bag_occupied=bag.Count(i=>i!=null),chest_occupied=stock.Count(i=>i!=null),chest_capacity=s.Chest.GetActualCapacity()};
    }
}
