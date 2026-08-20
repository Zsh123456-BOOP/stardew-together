using System.Text.Json;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private void StartAttachment(JsonElement args) {
        int toolSlot=AgentToolRegistry.Number(args,"tool_slot",-1),itemSlot=AgentToolRegistry.Number(args,"slot",-1);
        string mode=AgentToolRegistry.Text(args,"mode","attach");
        if(mode is not ("attach" or "detach")||toolSlot<0||toolSlot>=Game1.player.Items.Count||Game1.player.Items[toolSlot] is not Tool tool||tool.attachments.Count==0)throw new InvalidOperationException("attachable_tool_slot_required");
        if(Game1.player.CursorSlotItem!=null)throw new InvalidOperationException("player_cursor_not_empty");
        StardewValley.Object? attachment=null;
        if(mode=="attach") {
            if(itemSlot<0||itemSlot>=Game1.player.Items.Count||Game1.player.Items[itemSlot] is not StardewValley.Object item||!tool.canThisBeAttached(item))throw new InvalidOperationException("compatible_attachment_slot_required");
            attachment=item;
            // Loading bait/ammunition commits it to use, so shared-goal reservations apply.
            ValidateConsumption?.Invoke(new Dictionary<Item,int>{{item,item.Stack}},"","attachment:"+tool.QualifiedItemId);
        } else {
            if(tool.attachments.All(i=>i==null)){Finish("succeeded");return;}
            itemSlot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i]==null,-1);
            if(itemSlot<0)throw new InvalidOperationException("detachment_requires_empty_slot");
        }
        Dictionary<string,int> Balance()=>Game1.player.Items.Where(i=>i!=null).Concat(tool.attachments.Where(i=>i!=null)).GroupBy(i=>i.QualifiedItemId+":"+i.Quality).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
        var before=Balance();string slotsBefore=AgentJson.Encode(tool.attachments.Select(i=>AgentToolRegistry.ItemInfo(i)));
        var menu=new GameMenu(0);Game1.activeClickableMenu=menu;
        if(menu.GetCurrentPage() is not InventoryPage page)throw new InvalidOperationException("native_inventory_page_changed");
        var keys=Game1.oldKBState;
        try {
            Game1.oldKBState=default;
            var source=page.inventory.inventory[itemSlot].bounds;var target=page.inventory.inventory[toolSlot].bounds;
            if(attachment!=null)page.receiveLeftClick(source.Center.X,source.Center.Y);
            page.receiveRightClick(target.Center.X,target.Center.Y);
            if(Game1.player.CursorSlotItem!=null)page.receiveLeftClick(source.Center.X,source.Center.Y);
        } finally {Game1.oldKBState=keys;}
        if(Game1.player.CursorSlotItem!=null)throw new InvalidOperationException("attachment_cursor_recovery_required");
        var after=Balance();
        if(before.Count!=after.Count||before.Any(p=>after.GetValueOrDefault(p.Key)!=p.Value))throw new InvalidOperationException("attachment_inventory_balance_changed");
        string slotsAfter=AgentJson.Encode(tool.attachments.Select(i=>AgentToolRegistry.ItemInfo(i)));
        if(slotsBefore==slotsAfter)throw new InvalidOperationException("native_attachment_did_not_change");
        if(!menu.readyToClose())throw new InvalidOperationException("attachment_menu_cannot_close");menu.exitThisMenu();
        Current!.effects.Add(new{kind="native_tool_attachment",tool=tool.QualifiedItemId,mode,before=slotsBefore,after=slotsAfter});Current.completed=1;Finish("succeeded");
    }
}
