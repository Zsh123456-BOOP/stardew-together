using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private PurchaseAnimalsMenu? livestockMenu;
    private Building? livestockHome;
    private string livestockName="";
    private int livestockPrice,livestockReserve,livestockMoney;
    private long livestockId;
    internal static object ReadAnimalShop() {
        if(Game1.activeClickableMenu is not PurchaseAnimalsMenu menu)throw new InvalidOperationException("native_animal_shop_required");
        return new{location=menu.TargetLocation.NameOrUniqueName,stock=menu.animalsToPurchase.Select(b=>new{type=b.hoverText,price=b.item.salePrice(),available=b.item is StardewValley.Object {Type:null},condition=(b.item as StardewValley.Object)?.Type}),
            homes=menu.TargetLocation.buildings.Where(b=>b.GetIndoors() is AnimalHouse).Select(b=>new{type=b.buildingType.Value,x=b.tileX.Value,y=b.tileY.Value,under_construction=b.isUnderConstruction(),full=((AnimalHouse)b.GetIndoors()).isFull(),animals=((AnimalHouse)b.GetIndoors()).animalsThatLiveHere.Count})};
    }
    private void StartLivestockPurchase(JsonElement args) {
        if(Game1.activeClickableMenu is not PurchaseAnimalsMenu {onFarm:false,readOnly:false} menu)throw new InvalidOperationException("native_animal_selection_required");
        string type=AgentToolRegistry.Text(args,"type");
        var choice=menu.animalsToPurchase.FirstOrDefault(b=>b.hoverText==type&&b.item is StardewValley.Object {Type:null})??throw new InvalidOperationException("animal_not_available");
        livestockName=AgentToolRegistry.Text(args,"name").Trim();
        if(livestockName.Length is <1 or >12||livestockName.Any(char.IsControl)||Utility.areThereAnyOtherAnimalsWithThisName(livestockName))throw new InvalidOperationException("unique_animal_name_required_max12");
        livestockPrice=choice.item.salePrice();livestockReserve=AgentToolRegistry.Number(args,"keep_gold",500);
        if(livestockReserve<0||livestockPrice>AgentToolRegistry.Number(args,"budget",0)||Game1.player.Money-livestockPrice<livestockReserve)throw new InvalidOperationException("animal_purchase_budget_insufficient");
        livestockMenu=menu;livestockHome=null;livestockId=0;livestockMoney=Game1.player.Money;
        int index=menu.animalsToPurchase.IndexOf(choice);menu.Scroll(Math.Clamp(index/3,0,menu.scrollRows)-menu.currentScroll);
        if(!choice.visible)throw new InvalidOperationException("animal_choice_not_visible");
        menu.receiveLeftClick(choice.bounds.Center.X,choice.bounds.Center.Y);
        if(menu.animalBeingPurchased==null)throw new InvalidOperationException("native_animal_selection_rejected");
        livestockId=menu.animalBeingPurchased.myID.Value;
        Current!.phase="animal_home_selection";
    }
    private void TickLivestockPurchase() {
        var menu=livestockMenu!;
        if(Game1.IsFading()||Game1.locationRequest!=null||menu.freeze)return;
        if(Current!.phase=="animal_purchase_result") {
            if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} dialogue) {
                if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(400);dialogue.finishTyping();dialogue.receiveLeftClick(dialogue.xPositionOnScreen+16,dialogue.yPositionOnScreen+16);return;
            }
            if(Game1.activeClickableMenu==null){Finish("succeeded");return;}
            throw new InvalidOperationException("animal_result_menu_changed");
        }
        if(Game1.activeClickableMenu!=menu||!menu.onFarm)throw new InvalidOperationException("animal_purchase_menu_changed");
        if(Game1.player.Money-livestockPrice<livestockReserve||menu.priceOfAnimal!=livestockPrice)throw new InvalidOperationException("animal_price_or_budget_changed");
        livestockHome??=menu.TargetLocation.buildings.Where(b=>!b.isUnderConstruction()&&b.GetIndoors() is AnimalHouse h&&!h.isFull()&&menu.animalBeingPurchased.CanLiveIn(b)).OrderBy(b=>b.tileY.Value).ThenBy(b=>b.tileX.Value).FirstOrDefault();
        if(livestockHome==null)throw new InvalidOperationException("no_available_compatible_animal_home");
        if(menu.namingAnimal) {
            if(menu.newAnimalHome!=livestockHome||((AnimalHouse)livestockHome.GetIndoors()).isFull())throw new InvalidOperationException("animal_home_changed_or_full");
            menu.textBox.Text=livestockName;
            menu.receiveLeftClick(menu.doneNamingButton.bounds.Center.X,menu.doneNamingButton.bounds.Center.Y);
            var home=(AnimalHouse)livestockHome.GetIndoors();
            if(!home.animals.TryGetValue(livestockId,out var actual)||actual.Name!=livestockName||!home.animalsThatLiveHere.Contains(livestockId)||livestockMoney-Game1.player.Money!=livestockPrice)throw new InvalidOperationException("native_animal_purchase_not_verified");
            Current.effects.Add(new{kind="native_animal_purchased",id=livestockId,name=actual.Name,type=actual.type.Value,cost=livestockPrice,home=new{x=livestockHome.tileX.Value,y=livestockHome.tileY.Value}});Current.completed=1;Current.phase="animal_purchase_result";return;
        }
        var tile=new Point(livestockHome.tileX.Value+livestockHome.tilesWide.Value/2,livestockHome.tileY.Value+livestockHome.tilesHigh.Value/2);
        int x=tile.X*64+32-Game1.viewport.X,y=tile.Y*64+32-Game1.viewport.Y;
        if(x<64||x>Game1.viewport.Width-128||y<64||y>Game1.viewport.Height-128){Game1.panScreen(x<64?-16:x>Game1.viewport.Width-128?16:0,y<64?-16:y>Game1.viewport.Height-128?16:0);return;}
        NativeMenuInput.ClickWorld(menu,tile);
        if(!menu.namingAnimal)throw new InvalidOperationException("native_animal_home_selection_rejected");
    }
}
