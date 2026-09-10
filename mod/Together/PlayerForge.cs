using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace Together;
public sealed partial class PlayerExecutor {
    private int forgeLeftSlot,forgeRightSlot,forgeCount,forgeBudget,forgeSpent,forgeCost,forgeShardsBefore,forgeMaterialBefore;
    private Item? forgeLeft,forgeRight;
    private string forgeRightId="",forgeBefore="";
    private Point? forgeTile;
    internal static object ForgeItemState(Item? item)=>new{item=AgentToolRegistry.ItemInfo(item),enchants=(item as Tool)?.enchantments.Select(e=>new{type=e.GetType().Name,level=e.GetLevel()}),combined=(item as CombinedRing)?.combinedRings.Select(r=>r.QualifiedItemId)};
    internal static object ReadForge()=>Game1.activeClickableMenu is ForgeMenu menu?new{opened=true,busy=menu.IsBusy(),left=ForgeItemState(menu.leftIngredientSpot.item),right=ForgeItemState(menu.rightIngredientSpot.item),held=ForgeItemState(menu.heldItem),valid=menu.IsValidCraft(menu.leftIngredientSpot.item,menu.rightIngredientSpot.item),cost=menu.GetForgeCost(menu.leftIngredientSpot.item,menu.rightIngredientSpot.item),shards=Game1.player.Items.Where(i=>i?.QualifiedItemId=="(O)848").Sum(i=>i.Stack)}:new{opened=false,note="原生锻造台开启后提供配方与实际费用，不假定所有工具和材料可组合"};
    private void StartForge(JsonElement args) {
        forgeLeftSlot=AgentToolRegistry.Number(args,"left_slot",-1);forgeRightSlot=AgentToolRegistry.Number(args,"right_slot",-1);forgeCount=AgentToolRegistry.Number(args,"count",1);forgeBudget=AgentToolRegistry.Number(args,"budget_shards",0);
        if(forgeLeftSlot<0||forgeRightSlot<0||forgeLeftSlot==forgeRightSlot||forgeLeftSlot>=Game1.player.Items.Count||forgeRightSlot>=Game1.player.Items.Count||forgeCount is <1 or >3||forgeBudget<0)throw new InvalidOperationException("forge_slots_count_budget_required");
        forgeLeft=Game1.player.Items[forgeLeftSlot];forgeRight=Game1.player.Items[forgeRightSlot];
        if(forgeLeft is not (Tool or Ring)||forgeRight==null||forgeRight.Stack<forgeCount)throw new InvalidOperationException("native_forge_ingredients_required");
        CapacityAdapter.RequireSlots(Game1.player,2); // Two native menu input returns, not one predicted forged item.
        if(Game1.activeClickableMenu is ForgeMenu m&&(m.heldItem!=null||m.leftIngredientSpot.item!=null||m.rightIngredientSpot.item!=null||m.IsBusy()))throw new InvalidOperationException("native_forge_has_unclaimed_items");
        forgeTile=null;forgeSpent=0;forgeRightId=forgeRight.QualifiedItemId;destination=AgentToolRegistry.Text(args,"location",Game1.activeClickableMenu is ForgeMenu?origin:"Caldera");
        Current!.phase="forge_travel";
    }
    private int ForgeTotal(string id,ForgeMenu menu)=>Game1.player.Items.Concat(new[]{menu.leftIngredientSpot.item,menu.rightIngredientSpot.item,menu.heldItem}).Where(i=>i?.QualifiedItemId==id).Sum(i=>i!.Stack);
    private void TickForge() {
        if(Game1.locationRequest!=null||Game1.fadeToBlack)return;
        if(Game1.activeClickableMenu is ForgeMenu menu) {
            if(menu.IsBusy())return;
            if(Current!.phase=="forge_animation") {
                if(menu.heldItem==null||forgeShardsBefore-ForgeTotal("(O)848",menu)!=forgeCost||forgeMaterialBefore-ForgeTotal(forgeRightId,menu)!=1)throw new InvalidOperationException("native_forge_consumption_or_result_not_verified");
                var result=menu.heldItem;forgeSpent+=forgeCost;Current.completed++;
                Current.effects.Add(new{kind="native_forge",before=forgeBefore,after=ForgeItemState(result),material=forgeRightId,shards=forgeCost});
                if(Game1.player.Items[forgeLeftSlot]!=null)throw new InvalidOperationException("forge_return_slot_changed");
                NativeMenuInput.ClickMenu(menu,menu.inventory.inventory[forgeLeftSlot].bounds);
                if(menu.heldItem!=null||!ReferenceEquals(Game1.player.Items[forgeLeftSlot],result))throw new InvalidOperationException("forge_result_not_returned_to_inventory");
                forgeLeft=result;
                if(menu.rightIngredientSpot.item!=null){NativeMenuInput.ClickMenu(menu,menu.rightIngredientSpot.bounds);NativeMenuInput.ClickMenu(menu,menu.inventory.inventory[forgeRightSlot].bounds);if(menu.heldItem!=null)throw new InvalidOperationException("forge_remainder_not_returned");}
                if(Current.completed>=forgeCount){if(!menu.readyToClose())return;menu.exitThisMenu();Finish("succeeded");return;}
                forgeRight=Game1.player.Items[forgeRightSlot];Current.phase="forge_next";
            }
            if(Current.phase is "forge_travel" or "forge_next") {
                if(!ReferenceEquals(Game1.player.Items[forgeLeftSlot],forgeLeft)||!ReferenceEquals(Game1.player.Items[forgeRightSlot],forgeRight)||forgeRight==null||forgeRight.QualifiedItemId!=forgeRightId)throw new InvalidOperationException("forge_inventory_changed");
                if(!menu.IsValidCraft(forgeLeft,forgeRight))throw new InvalidOperationException("native_forge_recipe_invalid");
                forgeCost=menu.GetForgeCost(forgeLeft,forgeRight);forgeShardsBefore=ForgeTotal("(O)848",menu);forgeMaterialBefore=ForgeTotal(forgeRightId,menu);forgeBefore=AgentJson.Encode(ForgeItemState(forgeLeft));
                if(forgeCost<0||forgeSpent+forgeCost>forgeBudget||forgeShardsBefore<forgeCost)throw new InvalidOperationException("forge_shard_budget_insufficient");
                var consumed=new Dictionary<Item,int>{{forgeRight,1}};int remaining=forgeCost;
                foreach(var shard in Game1.player.Items.Where(i=>i?.QualifiedItemId=="(O)848")){int take=Math.Min(remaining,shard.Stack);if(take>0)consumed[shard]=take;remaining-=take;}
                ValidateConsumption?.Invoke(consumed,"","forge");
                NativeMenuInput.ClickMenu(menu,menu.inventory.inventory[forgeLeftSlot].bounds);NativeMenuInput.ClickMenu(menu,menu.leftIngredientSpot.bounds);
                NativeMenuInput.ClickMenu(menu,menu.inventory.inventory[forgeRightSlot].bounds);NativeMenuInput.ClickMenu(menu,menu.rightIngredientSpot.bounds);
                if(menu.heldItem!=null||!ReferenceEquals(menu.leftIngredientSpot.item,forgeLeft)||!ReferenceEquals(menu.rightIngredientSpot.item,forgeRight))throw new InvalidOperationException("forge_ingredient_transfer_failed");
                NativeMenuInput.ClickMenu(menu,menu.startTailoringButton.bounds);if(!menu.IsBusy())throw new InvalidOperationException("native_forge_did_not_start");Current.phase="forge_animation";
            }
            return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("forge_menu_interrupted");
        if(Current!.phase=="forge_animation")throw new InvalidOperationException("forge_native_menu_closed_during_craft");
        if(!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(forgeTile==null) {
            var l=Game1.currentLocation;
            for(int y=0;y<l.Map.Layers[0].LayerHeight&&forgeTile==null;y++)for(int x=0;x<l.Map.Layers[0].LayerWidth;x++)if(l.doesTileHaveProperty(x,y,"Action","Buildings")=="Forge")try{var p=new Point(x,y);Walk(Approach(p,true));forgeTile=p;break;}catch(InvalidOperationException){}
            if(forgeTile==null)throw new InvalidOperationException("native_forge_unreachable");
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Adjacent(forgeTile.Value);Face(forgeTile.Value);NativeMenuInput.InteractWorld(forgeTile.Value);
        if(Game1.activeClickableMenu is not ForgeMenu)throw new InvalidOperationException("native_forge_not_opened");
    }
}
