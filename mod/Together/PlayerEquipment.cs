using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace Together;
public sealed partial class PlayerExecutor {
    private static Item? Equipped(string target)=>target switch{"left_ring"=>Game1.player.leftRing.Value,"right_ring"=>Game1.player.rightRing.Value,"boots"=>Game1.player.boots.Value,"hat"=>Game1.player.hat.Value,"shirt"=>Game1.player.shirtItem.Value,"pants"=>Game1.player.pantsItem.Value,_=>throw new InvalidOperationException("unsupported_equipment_slot")};
    internal static object ReadEquipment()=>new{slots=new[]{"left_ring","right_ring","boots","hat","shirt","pants"}.Select(slot=>new{slot,item=AgentToolRegistry.ItemInfo(Equipped(slot))}),inventory=AgentToolRegistry.Inventory()};
    private void StartEquipment(JsonElement args) {
        string targetSlot=AgentToolRegistry.Text(args,"target");Item? old=Equipped(targetSlot);
        int slot=AgentToolRegistry.Number(args,"slot",-1);bool remove=AgentToolRegistry.Text(args,"mode","equip")=="remove";
        Item? selected=null;
        if(remove) {
            if(old==null){Finish("succeeded");return;}
            slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i]==null,-1);
            if(slot<0)throw new InvalidOperationException("unequip_requires_empty_inventory_slot");
        }else {
            if(slot<0||slot>=Game1.player.Items.Count||Game1.player.Items[slot] is not {} item)throw new InvalidOperationException("equipment_inventory_slot_required");
            selected=item;
            bool compatible=targetSlot switch{"left_ring" or "right_ring"=>item is Ring,"boots"=>item is Boots,"hat"=>item is Hat,"shirt"=>item is Clothing c&&c.clothesType.Value==Clothing.ClothesType.SHIRT,"pants"=>item is Clothing c&&c.clothesType.Value==Clothing.ClothesType.PANTS,_=>false};
            if(!compatible||item.Stack!=1)throw new InvalidOperationException("equipment_type_mismatch");
        }
        if(Game1.player.CursorSlotItem!=null)throw new InvalidOperationException("player_cursor_not_empty");
        var menu=new GameMenu(0);Game1.activeClickableMenu=menu;
        if(menu.GetCurrentPage() is not InventoryPage page)throw new InvalidOperationException("native_inventory_page_changed");
        string nativeName=targetSlot switch{"left_ring"=>"Left Ring","right_ring"=>"Right Ring","boots"=>"Boots","hat"=>"Hat","shirt"=>"Shirt",_=>"Pants"};
        var component=page.equipmentIcons.First(c=>c.name==nativeName);
        var keyboard=Game1.oldKBState;
        try {
            Game1.oldKBState=default;
            void Click(Rectangle bounds)=>page.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
            if(!remove){Click(page.inventory.inventory[slot].bounds);if(Game1.player.CursorSlotItem!=selected)throw new InvalidOperationException("native_equipment_pickup_failed");}
            Click(component.bounds);
            if(Equipped(targetSlot)!=selected)throw new InvalidOperationException("native_equipment_change_not_verified");
            if(Game1.player.CursorSlotItem!=null)Click(page.inventory.inventory[slot].bounds);
        }finally {Game1.oldKBState=keyboard;}
        if(Game1.player.CursorSlotItem!=null||old!=null&&!Game1.player.Items.Contains(old))throw new InvalidOperationException("old_equipment_not_returned_to_inventory");
        if(!menu.readyToClose())throw new InvalidOperationException("equipment_menu_cannot_close");menu.exitThisMenu();
        Current!.effects.Add(new{kind="native_equipment_changed",target=targetSlot,before=old?.QualifiedItemId,after=Equipped(targetSlot)?.QualifiedItemId});Current.completed=1;Finish("succeeded");
    }
}
