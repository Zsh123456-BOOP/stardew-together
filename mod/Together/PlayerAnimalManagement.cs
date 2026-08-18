using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private FarmAnimal? managedAnimal;
    private Building? animalNewHome;
    private string animalOperation="",animalRename="";
    private bool animalReproduction;
    private int animalSellMinimum;
    internal static object ReadAnimals()=>Game1.getFarm().getAllFarmAnimals().Select(a=>new{id=a.myID.Value,name=a.Name,type=a.type.Value,owner=a.ownerID.Value,location=a.currentLocation?.NameOrUniqueName,x=a.TilePoint.X,y=a.TilePoint.Y,home=new{x=a.home?.tileX.Value,y=a.home?.tileY.Value},baby=a.isBaby(),produce=a.currentProduce.Value,pet=a.wasPet.Value,fullness=a.fullness.Value,friendship=a.friendshipTowardFarmer.Value,allow_reproduction=a.allowReproduction.Value,sell_price=a.getSellPrice()}).ToArray();
    private void StartAnimalManagement(JsonElement args) {
        if(!args.TryGetProperty("id",out var raw)||!raw.TryGetInt64(out long id))throw new InvalidOperationException("observed_animal_id_required");
        managedAnimal=Game1.getFarm().getAllFarmAnimals().FirstOrDefault(a=>a.myID.Value==id&&a.ownerID.Value==Game1.player.UniqueMultiplayerID)??throw new InvalidOperationException("owned_animal_not_found");
        animalOperation=AgentToolRegistry.Text(args,"mode");animalRename=AgentToolRegistry.Text(args,"name").Trim();animalNewHome=null;
        if(animalOperation is not ("rename" or "sell" or "move_home" or "reproduction"))throw new InvalidOperationException("unknown_animal_management_mode");
        if(animalOperation=="rename"&&(animalRename.Length is <1 or >24||animalRename.Any(char.IsControl)||animalRename!=managedAnimal.Name&&Utility.areThereAnyOtherAnimalsWithThisName(animalRename)))throw new InvalidOperationException("unique_animal_name_required_max24");
        if(animalOperation=="reproduction") {
            if(!args.TryGetProperty("enabled",out var enabled)||enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("reproduction_enabled_required");
            animalReproduction=enabled.GetBoolean();
        }
        if(animalOperation=="sell") {
            if(!args.TryGetProperty("min_price",out var min)||!min.TryGetInt32(out animalSellMinimum)||animalSellMinimum<0)throw new InvalidOperationException("explicit_minimum_sale_price_required");
        }
        if(animalOperation=="move_home") {
            int x=AgentToolRegistry.Number(args,"home_x",-1),y=AgentToolRegistry.Number(args,"home_y",-1);
            animalNewHome=Game1.getFarm().buildings.FirstOrDefault(b=>b.tileX.Value==x&&b.tileY.Value==y&&b!=managedAnimal.home&&!b.isUnderConstruction()&&b.GetIndoors() is AnimalHouse h&&!h.isFull()&&managedAnimal.CanLiveIn(b))??throw new InvalidOperationException("observed_compatible_new_home_required");
        }
        destination=managedAnimal.currentLocation?.NameOrUniqueName??throw new InvalidOperationException("animal_location_unknown");Current!.phase="animal_manage_travel";
    }
    private void TickAnimalManagement() {
        var animal=managedAnimal!;
        if(Game1.locationRequest!=null||Game1.IsFading())return;
        if(Current!.phase=="animal_manage_result") {
            if(Game1.activeClickableMenu==null){Finish("succeeded");return;}
            return;
        }
        if(Game1.activeClickableMenu is AnimalQueryMenu menu&&menu.animal==animal) {
            if(animalOperation=="move_home"&&menu.movingAnimal) {
                var b=animalNewHome!;
                if(b.isUnderConstruction()||b.GetIndoors() is not AnimalHouse h||h.isFull()||!animal.CanLiveIn(b))throw new InvalidOperationException("animal_new_home_unavailable");
                var tile=new Point(b.tileX.Value+b.tilesWide.Value/2,b.tileY.Value+b.tilesHigh.Value/2);int x=tile.X*64+32-Game1.viewport.X,y=tile.Y*64+32-Game1.viewport.Y;
                if(x<64||x>Game1.viewport.Width-128||y<64||y>Game1.viewport.Height-128){Game1.panScreen(x<64?-16:x>Game1.viewport.Width-128?16:0,y<64?-16:y>Game1.viewport.Height-128?16:0);return;}
                var old=animal.homeInterior as AnimalHouse;NativeMenuInput.ClickWorld(menu,tile);
                if(animal.home!=b||!h.animalsThatLiveHere.Contains(animal.myID.Value)||old?.animalsThatLiveHere.Contains(animal.myID.Value)==true)throw new InvalidOperationException("native_animal_move_not_verified");
                Current.effects.Add(new{kind="native_animal_rehomed",id=animal.myID.Value,home=new{x=b.tileX.Value,y=b.tileY.Value}});Current.completed=1;Current.phase="animal_manage_result";return;
            }
            if(animalOperation=="sell") {
                int price=animal.getSellPrice();if(price<animalSellMinimum)throw new InvalidOperationException("animal_sale_below_minimum");
                if(!menu.confirmingSell){menu.receiveLeftClick(menu.sellButton.bounds.Center.X,menu.sellButton.bounds.Center.Y);return;}
                int before=Game1.player.Money;menu.receiveLeftClick(menu.yesButton.bounds.Center.X,menu.yesButton.bounds.Center.Y);
                if(animal.health.Value>=0||((AnimalHouse)animal.homeInterior).animalsThatLiveHere.Contains(animal.myID.Value)||Game1.player.Money-before!=price)throw new InvalidOperationException("native_animal_sale_not_verified");
                Current.effects.Add(new{kind="native_animal_sold",id=animal.myID.Value,price});Current.completed=1;Finish("succeeded");return;
            }
            if(animalOperation=="move_home"){menu.receiveLeftClick(menu.moveHomeButton.bounds.Center.X,menu.moveHomeButton.bounds.Center.Y);return;}
            if(animalOperation=="reproduction"&&animal.allowReproduction.Value!=animalReproduction) {
                if(menu.allowReproductionButton==null)throw new InvalidOperationException("animal_cannot_reproduce");
                var button=menu.allowReproductionButton.bounds;menu.receiveLeftClick(button.Center.X,button.Center.Y);
                if(animal.allowReproduction.Value!=animalReproduction)throw new InvalidOperationException("native_reproduction_toggle_not_verified");
            }
            if(animalOperation=="rename")menu.textBox.Text=animalRename;
            menu.receiveLeftClick(menu.okButton.bounds.Center.X,menu.okButton.bounds.Center.Y);
            if(Game1.activeClickableMenu!=null||animalOperation=="rename"&&animal.Name!=animalRename)throw new InvalidOperationException("native_animal_edit_not_verified");
            Current.effects.Add(new{kind="native_animal_edited",id=animal.myID.Value,name=animal.Name,allow_reproduction=animal.allowReproduction.Value});Current.completed=1;Finish("succeeded");return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("animal_manage_menu_changed");
        if(!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(Game1.timeOfDay>=1900)throw new InvalidOperationException("animal_sleeping");
        if(animal.currentLocation?.NameOrUniqueName!=destination){destination=animal.currentLocation?.NameOrUniqueName??throw new InvalidOperationException("animal_location_unknown");edge=null;StopWalk();}
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(Math.Abs(Game1.player.TilePoint.X-animal.TilePoint.X)+Math.Abs(Game1.player.TilePoint.Y-animal.TilePoint.Y)>1) {
            if(DateTime.UtcNow>=nextInteraction){nextInteraction=DateTime.UtcNow.AddMilliseconds(500);Walk(Approach(animal.TilePoint,true));}else if(ownedController!=null)MonitorWalk();return;
        }
        if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(500);
        int slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is null or Tool,-1);
        if(slot<0)throw new InvalidOperationException("animal_interaction_empty_or_tool_slot_required");
        StopWalk();Game1.player.CurrentToolIndex=slot;animal.pet(Game1.player); // First pet is care; a subsequent native pet opens its query menu.
    }
}
