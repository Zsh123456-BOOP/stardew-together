using System.Text.Json;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private GeodeMenu? geodeMenu;
    private int geodeCount,geodeBudget,geodeKeep,geodeMoney,geodeSpent,geodeSlot;
    private string geodeFilter="",geodeItem="";
    private Dictionary<string,int> geodeStock=new();
    private static Dictionary<string,int> GeodeInventory(GeodeMenu menu)=>Game1.player.Items.Append(menu.heldItem).Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
    private void StartGeodes(JsonElement args) {
        if(Game1.activeClickableMenu is not GeodeMenu {heldItem:null,waitingForServerResponse:false,geodeAnimationTimer:<=0} menu)throw new InvalidOperationException("idle_native_geode_menu_required");
        geodeCount=AgentToolRegistry.Number(args,"count",1);geodeBudget=AgentToolRegistry.Number(args,"budget",0);geodeKeep=AgentToolRegistry.Number(args,"keep_gold",0);geodeFilter=AgentToolRegistry.Text(args,"item");
        if(geodeCount is <1 or >40||geodeBudget<0||geodeKeep<0)throw new InvalidOperationException("invalid_geode_batch_budget");
        geodeMenu=menu;geodeSpent=0;Current!.phase="geode_next";
    }
    private void TickGeodes() {
        var menu=geodeMenu!;if(Game1.activeClickableMenu!=menu)throw new InvalidOperationException("geode_menu_changed");
        if(menu.waitingForServerResponse||menu.geodeAnimationTimer>0)return;
        if(Current!.phase=="geode_animation") {
            var after=GeodeInventory(menu);var expected=geodeStock.ToDictionary(p=>p.Key,p=>p.Value);expected[geodeItem]=expected.GetValueOrDefault(geodeItem)-1;
            var changes=after.Keys.Union(expected.Keys).Select(id=>new{item=id,count=after.GetValueOrDefault(id)-expected.GetValueOrDefault(id)}).Where(c=>c.count!=0).ToArray();
            if(geodeMoney-Game1.player.Money!=25||changes.Length==0||changes.Any(c=>c.count<0))throw new InvalidOperationException("native_geode_result_not_verified");
            Current.effects.Add(new{kind="native_geode_opened",input=geodeItem,cost=25,output=changes});Current.completed++;geodeSpent+=25;Current.phase="geode_next";
        }
        if(Current.completed>=geodeCount) {
            if(menu.heldItem!=null)throw new InvalidOperationException("geode_cursor_not_empty");
            if(menu.readyToClose()){menu.exitThisMenu();Finish("succeeded");}return;
        }
        if(menu.heldItem!=null)throw new InvalidOperationException("unexpected_geode_cursor_item");
        if(geodeSpent+25>geodeBudget||Game1.player.Money-25<geodeKeep)throw new InvalidOperationException("geode_budget_exhausted");
        CapacityAdapter.RequireSlots(Game1.player,1); // Unknown native geode output: reserve a slot conservatively.
        geodeSlot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is {} item&&Utility.IsGeode(item)&&(geodeFilter.Length==0||item.QualifiedItemId==geodeFilter),-1);
        if(geodeSlot<0)throw new InvalidOperationException("requested_geodes_exhausted");
        var input=Game1.player.Items[geodeSlot];ValidateConsumption?.Invoke(new Dictionary<Item,int>{{input,1}},"","");geodeItem=input.QualifiedItemId;geodeStock=GeodeInventory(menu);geodeMoney=Game1.player.Money;
        var slot=menu.inventory.inventory[geodeSlot].bounds;menu.receiveRightClick(slot.Center.X,slot.Center.Y);
        if(menu.heldItem?.QualifiedItemId!=geodeItem||menu.heldItem.Stack!=1)throw new InvalidOperationException("native_geode_pickup_not_verified");
        var anvil=menu.geodeSpot.bounds;menu.receiveLeftClick(anvil.Center.X,anvil.Center.Y);
        if(!menu.waitingForServerResponse&&menu.geodeAnimationTimer<=0)throw new InvalidOperationException("native_geode_crack_rejected");
        Current.phase="geode_animation";
    }
}
