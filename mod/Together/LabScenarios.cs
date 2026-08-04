using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed class TogetherLabApi {
    private readonly ModEntry mod;
    public TogetherLabApi(ModEntry mod){this.mod=mod;}
    public string RunScenario(string json)=>mod.RunLabScenario(json);
    public string GetDiagnostics()=>mod.StatusJson();
}
public sealed partial class ModEntry {
    public override object GetApi()=>new TogetherLabApi(this);
    internal string RunLabScenario(string json) {
        if(!Settings.EnableLab || !Context.IsWorldReady || Context.IsMultiplayer || Game1.player.Name!="AgentLab")throw new InvalidOperationException("isolated_lab_required");
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;string scenario=root.GetProperty("scenario").GetString()!;
        string Arg(string key,string fallback="")=>root.TryGetProperty(key,out var value)?value.GetString()??fallback:fallback;
        int Num(string key,int fallback=0)=>root.TryGetProperty(key,out var value)?value.GetInt32():fallback;
        var farm=Game1.getFarm();
        switch(scenario) {
            case "forget":ClearMemories();break;
            case "performance_reset":measuredFrames.Clear();break;
            case "event_fixture": {
                var tile=Game1.player.TilePoint;
                Game1.currentLocation.startEvent(new Event($"none/-1000 -1000/farmer {tile.X} {tile.Y} 2/pause 5000/end"));break;
            }
            case "night_continue":
                if(Game1.activeClickableMenu is StardewValley.Menus.ShippingMenu shipping)shipping.receiveLeftClick(shipping.okButton.bounds.Center.X,shipping.okButton.bounds.Center.Y);
                else if(Game1.activeClickableMenu is StardewValley.Menus.LevelUpMenu level)level.receiveKeyPress(Microsoft.Xna.Framework.Input.Keys.Escape);
                break;
            case "model_name":Settings.Model=Arg("value","deepseek-flash");break;
            case "resume":Resume();break;
            case "habit":ConfirmHabit(Num("index"));break;
            case "fresh_person":
                Cancel();Data.People[Selected]=new Companion{EnergyDay=Game1.Date.TotalDays,DailyCompanion=true};break;
            case "profile":Current.Profile.Likes=Arg("likes",Current.Profile.Likes);Current.Profile.Dislikes=Arg("dislikes",Current.Profile.Dislikes);break;
            case "quest_contract":return NativeQuestContracts();
            case "mine_fixture": {
                Cancel();Settings.Autonomy=false;
                var mine=MineShaft.GetMine("UndergroundMine1");var ladder=mine.tileBeneathLadder+new Vector2(1,0);
                mine.objects.Remove(ladder);mine.createLadderAt(ladder);
                var npc=FindCharacter("Abigail")??throw new InvalidOperationException("fixture_target_missing");
                Game1.warpCharacter(npc,mine,mine.tileBeneathLadder+new Vector2(0,2));
                break;
            }
            case "combat_fixture": {
                var npc=FindCharacter("Abigail")??throw new InvalidOperationException("fixture_target_missing");var location=npc.currentLocation;
                var monster=new StardewValley.Monsters.GreenSlime(npc.Position+new Vector2(128,0),1){Health=12};
                location.characters.Add(monster);Game1.warpFarmer(location.NameOrUniqueName,npc.TilePoint.X,npc.TilePoint.Y+2,false);
                break;
            }
            case "configure":
                if(root.TryGetProperty("policy",out var policy))Data.FarmPolicy=JsonSerializer.Deserialize<FarmPolicy>(policy)??new();
                Data.FarmHelp=Data.FarmPolicy.Enabled;break;
            case "production": {
                Cancel();api!.PrepareLab();Settings.Autonomy=false;Data.Projects.Clear();Data.FarmPolicy=new();Data.FarmHelp=true;
                Data.FarmPolicy.Areas.Add(new(){Id="lab-plot",X=45,Y=28,Width=2,Height=1,Seed="(O)472"});
                for(int x=45;x<47;x++){farm.objects.Remove(new Vector2(x,28));farm.terrainFeatures.Remove(new Vector2(x,28));}
                var supplies=(Chest)farm.objects[new Vector2(44,26)];supplies.Items.Add(ItemRegistry.Create("(O)472",9));
                farm.piecesOfHay.Value=8;
                var coop=farm.buildings.FirstOrDefault(b=>b.modData.ContainsKey("together-lab"));
                if(coop==null) {
                    coop=new Building("Coop",new Vector2(58,20));coop.modData["together-lab"]="true";coop.daysOfConstructionLeft.Value=0;
                    farm.buildings.Add(coop);
                }
                if(coop.GetIndoors() is AnimalHouse house) {
                    house.objects.Clear();house.animals.Clear();house.animalsThatLiveHere.Clear();
                    var animal=new FarmAnimal("White Chicken",-449404284,Game1.player.UniqueMultiplayerID){Position=new Vector2(3,4)*64};
                    animal.home=coop;animal.currentLocation=house;
                    house.animals.Add(animal.myID.Value,animal);house.animalsThatLiveHere.Add(animal.myID.Value);
                }
                break;
            }
            case "animal_products": {
                var type=Arg("type","White Cow");var animal=new FarmAnimal(type,-449404285,Game1.player.UniqueMultiplayerID){Position=new Vector2(46,25)*64};
                animal.age.Value=20;animal.currentProduce.Value=type=="Sheep"?"440":"184";animal.currentLocation=farm;animal.wasPet.Value=true;
                farm.animals[-449404285]=animal;break;
            }
            case "forage_fixture": {
                var item=ItemRegistry.Create<StardewValley.Object>("(O)18");item.TileLocation=new Vector2(48,29);item.IsSpawnedObject=true;farm.objects[item.TileLocation]=item;break;
            }
            case "clear_fixture": {
                Data.FarmPolicy.ClearDesignatedPlots=true;
                foreach(var entry in new[]{(45,"294"),(46,"674")}) {var item=ItemRegistry.Create<StardewValley.Object>(entry.Item2);item.TileLocation=new Vector2(entry.Item1,28);farm.objects[item.TileLocation]=item;}
                break;
            }
            case "economy": {
                Cancel();api!.PrepareLab();Settings.Autonomy=false;Data.Projects.Clear();Data.FarmPolicy=new(){DailyBudget=200,KeepGold=500};Data.FarmHelp=true;
                Data.FarmPolicy.Shopping.Add(new(){Id="lab-seeds",Item="(O)472",Shop="SeedShop",Count=2,MaxUnitPrice=50});
                Game1.player.Money=1000;Game1.player.modData.Remove("stardewagent.together/economy");
                var sale=new Chest(true){TileLocation=new Vector2(52,28)};sale.modData["stardewagent.together/chest-role"]="sell";
                sale.Items.Add(ItemRegistry.Create("(O)24",3));farm.objects[sale.TileLocation]=sale;
                break;
            }
            case "warp_actor": {
                var npc=FindCharacter(Arg("npc","Abigail"));string location=Arg("location","Farm");
                var destination=location.StartsWith("UndergroundMine")?MineShaft.GetMine(location):Game1.getLocationFromName(location);
                if(npc==null || destination==null)throw new InvalidOperationException("fixture_target_missing");
                Game1.warpCharacter(npc,destination,new Vector2(Num("x",44),Num("y",23)));break;
            }
            case "warp_player":Game1.warpFarmer(Arg("location","Farm"),Num("x",44),Num("y",23),false);break;
            case "town_key":Game1.player.HasTownKey=Num("enabled",0)!=0;break;
            case "day":Game1.dayOfMonth=Math.Clamp(Num("value",10),1,28);break;
            case "full_pouch": {
                var inventory=Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{Selected}");inventory.Clear();
                for(int i=0;i<12;i++)inventory.Add(ItemRegistry.Create("(W)0"));break;
            }
            case "gift_fixture": {
                var inventory=Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{Selected}");
                inventory.Add(ItemRegistry.Create("(O)145"));break;
            }
            case "full_output":foreach(var chest in farm.objects.Values.OfType<Chest>().Where(c=>c.modData.TryGetValue("stardewagent.together/chest-role",out var role)&&role=="output")) {
                chest.Items.Clear();for(int i=0;i<36;i++)chest.Items.Add(ItemRegistry.Create("(W)0"));
            }break;
            case "reservation": {
                Data.Projects.RemoveAll(p=>p.Kind=="lab-reservation");
                Data.Projects.Add(new(){Kind="lab-reservation",Title="验收预留",Needs=new(){new(){Item=Arg("item","(O)472"),Count=Num("count",9),Quality=Num("quality")}}});break;
            }
            case "wet_fields":foreach(var dirt in farm.terrainFeatures.Values.OfType<HoeDirt>())dirt.state.Value=HoeDirt.watered;break;
            case "obstacle": {
                var location=Game1.getLocationFromName(Arg("location","Farm"));var tile=new Vector2(Num("x"),Num("y"));
                if(Num("remove")==1)location.objects.Remove(tile);
                else {var stone=ItemRegistry.Create<StardewValley.Object>("(O)343");stone.TileLocation=tile;location.objects[tile]=stone;}break;
            }
            case "budget_limit":Settings.MaxCallsPerDay=Math.Clamp(Num("value",24),1,100);break;
            case "time":Game1.timeOfDay=Num("value",1000);break;
            case "weather":Game1.isRaining=Num("raining",1)!=0;break;
            case "auto":Settings.Autonomy=Num("enabled",1)!=0;Settings.AutoIntervalSeconds=Num("interval",30);autoAt=DateTime.MinValue;foreach(var p in Data.People.Values)p.Life.LastDecisionMinute=-1000;break;
            case "model":Send(Arg("message"));break;
            case "cancel":Cancel();break;
            case "select":Select(Arg("npc","Abigail"));break;
            case "preset":SetPreset(Arg("name","农场伙伴"));break;
            case "social":SetSocialMode(Arg("mode","normal"));break;
            case "challenge":StartChallenge();break;
            case "project":if(Arg("kind")=="farm")AddFarmProject();else if(Arg("kind")=="bundle")AddBundleProject();else AddProgressProject(Arg("kind"));break;
            case "plan": {
                var steps=JsonSerializer.Deserialize<List<Step>>(root.GetProperty("steps"))??new();
                if(steps.Count==0 || steps.Any(s=>!Decision.Labels.ContainsKey(s.skill)))throw new InvalidOperationException("invalid_lab_plan");
                StartFor(Selected,new(){decision="accept",title="真实场景检查",speech="开始检查",steps=steps},Num("forced")==1);break;
            }
            case "budget":Data.Calls=Num("calls",Settings.MaxCallsPerDay);RecordUsage();break;
            case "supply_remove":foreach(var chest in farm.objects.Values.OfType<Chest>().Where(c=>c.modData.TryGetValue("stardewagent.together/chest-role",out var role)&&role=="supplies"))chest.Items.Clear();break;
            case "bed": {
                var house=Utility.getHomeOfFarmer(Game1.player);var spot=house.GetPlayerBedSpot();
                Game1.warpFarmer(house.NameOrUniqueName,spot.X,spot.Y,false);break;
            }
            case "sleep": {
                if(Game1.currentLocation is not FarmHouse)throw new InvalidOperationException("sleep_fixture_requires_home");
                Game1.player.isInBed.Value=true;
                farm.animals.Remove(-449404285);
                farm.animals.Remove(-449404282); // remove only the homeless petting fixture
                Game1.currentLocation.answerDialogueAction("Sleep_Yes",null);break;
            }
            case "panel":Game1.activeClickableMenu=new CompanionMenu(this,Num("tab",5),Num("page"),Num("choice"));break;
            case "close":if(Game1.activeClickableMenu is CompanionMenu)Game1.exitActiveMenu();break;
            default:throw new InvalidOperationException("unknown_lab_scenario");
        }
        RefreshFacts(true);return StatusJson();
    }
}
