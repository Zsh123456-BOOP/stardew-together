using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private string productionRecipe="";
    private string productionGoal="";
    private int productionRemaining;
    private CraftingPage? productionMenu;
    private void StartProduction(string skill,JsonElement args) {
        productionRecipe=AgentToolRegistry.Text(args,"recipe");productionRemaining=AgentToolRegistry.Number(args,"count",1);productionMenu=null;
        productionGoal=AgentToolRegistry.Text(args,"goal_id");
        if(productionRemaining is <1 or >99)throw new InvalidOperationException("invalid_recipe_batch_count");
        bool cooking=skill=="player.cook";
        if(!(cooking?Game1.player.cookingRecipes:Game1.player.craftingRecipes).ContainsKey(productionRecipe))throw new InvalidOperationException("recipe_not_known");
        if(cooking) {
            var home=Utility.getHomeOfFarmer(Game1.player);
            if(home.upgradeLevel<1)throw new InvalidOperationException("home_kitchen_not_unlocked");
            destination=home.NameOrUniqueName;Current!.phase="production_home";
        }else OpenProductionMenu();
    }
    private void OpenProductionMenu() {
        var top=Utility.getTopLeftPositionForCenteringOnScreen(800+IClickableMenu.borderWidth*2,600+IClickableMenu.borderWidth*2);
        productionMenu=new CraftingPage((int)top.X,(int)top.Y,800+IClickableMenu.borderWidth*2,600+IClickableMenu.borderWidth*2,cooking:Current!.skill=="player.cook",standaloneMenu:true);
        Game1.activeClickableMenu=productionMenu;Current.phase="production_batch";
    }
    private void TickProduction() {
        if(Current!.phase=="production_home") {
            if(Game1.currentLocation.NameOrUniqueName!=destination){if(Game1.player.CanMove&&!Game1.fadeToBlack)Travel();return;}
            if(Game1.currentLocation is not FarmHouse home||home.upgradeLevel<1)throw new InvalidOperationException("kitchen_unavailable");
            StopWalk();Walk(home.getKitchenStandingSpot());Current.phase="production_kitchen";return;
        }
        if(Current.phase=="production_kitchen") {
            if(!AtWalkTarget){MonitorWalk();return;}StopWalk();OpenProductionMenu();return;
        }
        if(productionMenu==null||Game1.activeClickableMenu!=productionMenu)throw new InvalidOperationException("crafting_menu_replaced");
        if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(200);
        if(productionMenu.heldItem!=null) {
            // Native add-to-inventory returns the unaccepted remainder. Keep it in
            // the native menu if capacity changed; never silently drop the output.
            productionMenu.heldItem=Game1.player.addItemToInventory(productionMenu.heldItem);
            if(productionMenu.heldItem!=null)throw new InvalidOperationException("capacity_no_stackable_room");
        }
        if(productionRemaining==0){productionMenu.exitThisMenu();productionMenu=null;Finish("succeeded");return;}
        for(int page=0;page<productionMenu.pagesOfCraftingRecipes.Count;page++) {
            var choice=productionMenu.pagesOfCraftingRecipes[page].FirstOrDefault(x=>x.Value.name==productionRecipe);
            if(choice.Key==null)continue;
            var recipe=choice.Value;
            if(!recipe.doesFarmerHaveIngredientsInInventory())throw new InvalidOperationException("recipe_ingredients_missing");
            var expected=recipe.createItem();
            var consumption=CapacityAdapter.Ingredients(Game1.player,recipe);
            if(Current.skill=="player.cook"&&expected.Quality==0) {
                var seasoning=Game1.player.Items.LastOrDefault(i=>i?.QualifiedItemId=="(O)917"&&i.Stack>consumption.GetValueOrDefault(i));
                if(seasoning!=null){consumption[seasoning]=consumption.GetValueOrDefault(seasoning)+1;expected.Quality=2;}
            }
            ValidateConsumption?.Invoke(consumption,productionGoal,expected.QualifiedItemId);
            var capacity=CapacityAdapter.After(Game1.player,consumption,expected);
            if(!capacity.Feasible)throw new InvalidOperationException(capacity.Reason);
            productionMenu.currentCraftingPage=page;
            // Use the game's own handler: consumes ingredients, emits quest events,
            // increments crafting/cooking statistics and checks achievements.
            var method=typeof(CraftingPage).GetMethod("clickCraftingRecipe",BindingFlags.Instance|BindingFlags.NonPublic)??throw new InvalidOperationException("native_crafting_handler_changed");
            int before=Current.skill=="player.cook"?Game1.player.recipesCooked.GetValueOrDefault(expected.ItemId):Game1.player.craftingRecipes[productionRecipe];
            var inventoryBefore=Game1.player.Items.Select((i,n)=>new{slot=n,id=i?.QualifiedItemId,count=i?.Stack??0,quality=i?.Quality??0}).ToArray();
            method.Invoke(productionMenu,new object[]{choice.Key,true});
            var inventoryAfter=Game1.player.Items.Select((i,n)=>new{slot=n,id=i?.QualifiedItemId,count=i?.Stack??0,quality=i?.Quality??0}).ToArray();
            Current.effects.Add(new{kind="native_recipe_order",recipe=productionRecipe,remaining_before=productionRemaining,inventory_before=inventoryBefore,inventory_after=inventoryAfter,held=productionMenu.heldItem==null?null:new{id=productionMenu.heldItem.QualifiedItemId,count=productionMenu.heldItem.Stack},capacity});
            int after=Current.skill=="player.cook"?Game1.player.recipesCooked.GetValueOrDefault(expected.ItemId):Game1.player.craftingRecipes[productionRecipe];
            if(productionMenu.heldItem?.QualifiedItemId!=expected.QualifiedItemId||after<=before)throw new InvalidOperationException("native_crafting_result_not_verified");
            Current.effects.Add(new{kind="native_recipe",recipe=productionRecipe,item=expected.QualifiedItemId,count=productionMenu.heldItem.Stack,native_count_before=before,native_count_after=after});
            Current.completed++;productionRemaining--;return;
        }
        throw new InvalidOperationException("known_recipe_not_in_native_menu");
    }
}
