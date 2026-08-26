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
            case "agent_tool":return JsonSerializer.Serialize(agentTools.Execute(Arg("tool"),root.GetProperty("args")));
            case "agent_start":StartAutoplay(Arg("goal"));break;
            case "agent_pause":PauseAutoplay("lab_pause");break;
            case "agent_ui":Game1.activeClickableMenu=new AutoplayMenu(this);break;
            case "agent_schedule_probe": {
                PauseAutoplay("lab_schedule_probe");Game1.exitActiveMenu();
                Data.Autoplay=new(){Goal="LAB deterministic scheduler contract",Status="running",StartDay=Game1.Date.TotalDays,RunId=Guid.NewGuid().ToString("N")};
                agentNext=DateTime.UtcNow.AddMinutes(3);agentStarting=false;agentNeedsDecision=false;agentWakeReasons.Clear();
                agentLabProbe=true;return JsonSerializer.Serialize(AgentPlanRead());
            }
            case "agent_reply_probe": {
                if(!agentLabProbe || !AutoplayRunning)throw new InvalidOperationException("schedule_probe_required");
                agentRequestEpoch=agentGeneration;agentRequestDay=Game1.Date.TotalDays;agentWatch.Restart();
                agentPending=Task.FromResult(new ModelReply(Arg("reply"),0));return JsonSerializer.Serialize(new{synthetic_reply=true});
            }
            case "agent_semantic_fixture": {
                PauseAutoplay("lab_semantic_fixture");Settings.Autonomy=false;Game1.exitActiveMenu();Game1.warpFarmer("Farm",62,17,false);
                Game1.player.Stamina=90;Game1.player.health=100;
                for(int i=0;i<Game1.player.Items.Count;i++)if(Game1.player.Items[i] is StardewValley.Object)Game1.player.Items[i]=null;
                foreach(var name in Data.People.Keys)Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{name}").Clear();
                for(int x=61;x<=70;x++)for(int y=17;y<=24;y++){farm.objects.Remove(new Vector2(x,y));farm.terrainFeatures.Remove(new Vector2(x,y));}
                string seed=DataLoader.Crops(Game1.content).First(c=>c.Value.Seasons.Any(s=>s.ToString().Equals(Game1.currentSeason,StringComparison.OrdinalIgnoreCase))).Key;
                for(int x=61;x<=63;x++)for(int y=18;y<=19;y++)farm.terrainFeatures[new Vector2(x,y)]=new HoeDirt(0,farm){crop=new Crop(seed,x,y,farm)};
                for(int x=67;x<=68;x++)for(int y=18;y<=23;y++)farm.objects[new Vector2(x,y)]=ItemRegistry.Create<StardewValley.Object>("(O)294");
                for(int x=64;x<=65;x++)for(int y=20;y<=23;y++){var o=ItemRegistry.Create<StardewValley.Object>("(O)343");o.MinutesUntilReady=1;farm.objects[new Vector2(x,y)]=o;}
                foreach(var can in Game1.player.Items.OfType<StardewValley.Tools.WateringCan>())can.WaterLeft=1;
                var box=new Chest(true);box.modData["stardewagent.together/chest-role"]="output";farm.objects[new Vector2(60,17)]=box;
                break;
            }
            case "agent_full_inventory_fixture": {
                PauseAutoplay("lab_full_inventory_fixture");Game1.exitActiveMenu();
                for(int i=0;i<Game1.player.Items.Count;i++)if(Game1.player.Items[i] is not Tool){Game1.player.Items[i]=ItemRegistry.Create(i==5?"(O)472":i==6?"(O)24":"(O)"+(388+i%3),i==6?5:10);}
                foreach(var name in Data.People.Keys) {var pouch=Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{name}");pouch.Clear();foreach(string id in new[]{"388","390","771","378","380","382","384","386"})pouch.Add(ItemRegistry.Create("(O)"+id,3));}
                break;
            }
            case "agent_pause_notice_fixture": {
                Data.Autoplay.Status="running";PauseAutoplay("玩家按 F10 暂停接管");break;
            }
            case "agent_empty_can_fixture": {
                PauseAutoplay("lab_empty_can_fixture");Game1.exitActiveMenu();
                foreach(var can in Game1.player.Items.OfType<StardewValley.Tools.WateringCan>())can.WaterLeft=0;
                break;
            }
            case "agent_dead_crop_fixture": {
                PauseAutoplay("lab_dead_crop_fixture");Game1.exitActiveMenu();Game1.warpFarmer("Farm",66,18,false);
                for(int x=67;x<=69;x++){
                    var tile=new Vector2(x,18);farm.objects.Remove(tile);var crop=new Crop("472",x,18,farm);crop.dead.Value=x<69;
                    farm.terrainFeatures[tile]=new HoeDirt(0,farm){crop=crop};
                }
                break;
            }
            case "agent_policy_probe": {
                PauseAutoplay("lab_policy_probe");Game1.exitActiveMenu();
                Data.Autoplay.Status="running";Data.Autoplay.Detail="LAB POLICY PROBE — no model decision";
                agentNext=DateTime.UtcNow.AddSeconds(60);agentStarting=false;agentLabProbe=true;return JsonSerializer.Serialize(AgentDailyRead());
            }
            case "agent_night_notice_fixture": {
                // Replay a plain native notice for menu regression, not earned skill proof.
                var notice=new Point(0,1);if(!Game1.player.newLevels.Contains(notice))Game1.player.newLevels.Add(notice);
                break;
            }
            case "agent_resource_fixture": {
                PauseAutoplay("lab_resource_fixture");Settings.Autonomy=false;Game1.exitActiveMenu();Game1.warpFarmer("Farm",62,17,false);
                for(int x=66;x<=69;x++)for(int y=17;y<=24;y++){var tile=new Vector2(x,y);farm.terrainFeatures.Remove(tile);farm.objects.Remove(tile);}
                foreach(int x in new[]{67,68})for(int y=18;y<=23;y++)farm.objects[new Vector2(x,y)]=ItemRegistry.Create<StardewValley.Object>("(O)294");
                var stone=ItemRegistry.Create<StardewValley.Object>("(O)343");stone.MinutesUntilReady=2;farm.objects[new Vector2(63,18)]=stone;
                var forage=ItemRegistry.Create<StardewValley.Object>("(O)16");forage.IsSpawnedObject=true;farm.objects[new Vector2(63,17)]=forage;
                break;
            }
            case "agent_fixture": {
                foreach(var can in Game1.player.Items.OfType<StardewValley.Tools.WateringCan>())can.WaterLeft=can.waterCanMax;
                Game1.player.Stamina=Game1.player.MaxStamina;Game1.player.health=Game1.player.maxHealth;
                PauseAutoplay("lab_fixture");Settings.Autonomy=false;Game1.exitActiveMenu();
                Game1.warpFarmer("Farm",62,17,false);
                for(int x=61;x<=63;x++)for(int y=18;y<=19;y++){farm.objects.Remove(new Vector2(x,y));farm.terrainFeatures.Remove(new Vector2(x,y));}
                string seed=DataLoader.Crops(Game1.content).Where(c=>c.Value.Seasons.Any(s=>s.ToString().Equals(Game1.currentSeason,StringComparison.OrdinalIgnoreCase)) && ItemRegistry.GetData("(O)"+c.Key)!=null).OrderBy(c=>c.Value.DaysInPhase.Sum()).ThenBy(c=>c.Key,StringComparer.Ordinal).First().Key;
                Game1.player.Items[5]=ItemRegistry.Create("(O)"+seed,6);Game1.player.Items[6]=ItemRegistry.Create("(O)388",50);
                farm.animals.Remove(-449404285);farm.animals.Remove(-449404282);
                break;
            }
            case "agent_speed":Settings.AutoplayClockRate=AutoplaySpeed.Clock(Num("rate",2));break;
            case "goal_recipes":ReadGoalRecipes();return JsonSerializer.Serialize(goalRecipes.Values,jsonOptions);
            case "goal_suite":return GoalContracts();
            case "goal_refusal":return GoalRefusalContract();
            case "goal_add":AddSharedGoal(Arg("id"),Num("count",1));break;
            case "goal_menu":OpenGoals();break;
            case "goal_button":if(Game1.activeClickableMenu is SharedGoalsMenu goalsMenu)goalsMenu.CheckButton(Arg("label"));else throw new InvalidOperationException("goals_menu_required");break;
            case "goal_assign":AssignGoalNode(Arg("id"),Arg("node"),Arg("owner","player"));break;
            case "goal_work":RequestGoalWork(Arg("id"),Arg("node"),Num("forced")==1);break;
            case "goal_accept":AcceptProposal(Num("forced")==1);break;
            case "goal_pause":ToggleSharedGoal(Arg("id"));break;
            case "goal_options": {RefreshFacts(true);var world=World();var actor=Actor(world,Selected);return JsonSerializer.Serialize(actor.HasValue?OptionsFor(Selected,Current,SituationFor(Selected,actor.Value,world),actor.Value):new(),jsonOptions);}
            case "goal_fixture": {
                Cancel();api!.PrepareLab();Settings.Autonomy=false;Data.SharedGoals.Clear();Data.Projects.Clear();Data.FarmPolicy=new();Data.FarmHelp=true;
                Data.Knowledge.DiscoveredOnly=false;
                foreach(var c in farm.objects.Values.OfType<Chest>())c.Items.Clear();
                foreach(var name in Data.People.Keys)Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{name}").Clear();
                for(int i=0;i<Game1.player.Items.Count;i++)if(Game1.player.Items[i] is StardewValley.Object)Game1.player.Items[i]=null;
                foreach(var point in new[]{new Vector2(47,26),new Vector2(49,26)}) {var ore=ItemRegistry.Create<StardewValley.Object>("(O)751");ore.TileLocation=point;ore.MinutesUntilReady=2;farm.objects[point]=ore;}
                break;
            }
            case "knowledge_suite":return KnowledgeContracts();
            case "knowledge_query":return JsonSerializer.Serialize(Knowledge.Query(Arg("query"),Arg("id")==""?null:Arg("id")),jsonOptions);
            case "knowledge_ask":AskKnowledge(Arg("query"),Arg("id")==""?null:Arg("id"));break;
            case "knowledge_book":OpenKnowledge(Arg("query"));break;
            case "knowledge_button":
                if(Game1.activeClickableMenu is not EncyclopediaMenu bookMenu)throw new InvalidOperationException("encyclopedia_menu_required");
                bookMenu.CheckButton(Arg("label"));return JsonSerializer.Serialize(bookMenu.Evidence,jsonOptions);
            case "knowledge_invalidate":Knowledge.Invalidate();break;
            case "knowledge_mode":Data.Knowledge.DiscoveredOnly=Arg("value")!="all";CancelKnowledge();break;
            case "knowledge_pin":PinKnowledge(Arg("id"));break;
            case "knowledge_note":Data.Knowledge.Note(Arg("id"),Arg("text"));break;
            case "knowledge_export":return JsonSerializer.Serialize(new{ready=Knowledge.Ready,status=Knowledge.Status,entries=Knowledge.Index.Entries.Count,answer=KnowledgeAnswer,packet=LastKnowledge,book=Data.Knowledge},jsonOptions);
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
            case "business_fixture": {
                RunLabScenario(JsonSerializer.Serialize(new{scenario="production"}));
                farm.animals.Remove(-449404282);
                Data.Business=new();Data.FarmInvestment=new();Data.Autoplay.Routine=new();Data.SharedGoals.Clear();Data.Projects.Clear();
                Game1.player.Money=20000;Game1.timeOfDay=1000;Game1.player.Stamina=Game1.player.MaxStamina;
                foreach(int i in Enumerable.Range(0,Game1.player.Items.Count))if(Game1.player.Items[i] is StardewValley.Object)Game1.player.Items[i]=null;
                foreach(var supply in new[]{("(O)398",20),("(O)24",20),("(O)176",20),("(BC)105",1)})Game1.player.addItemToInventoryBool(ItemRegistry.Create(supply.Item1,supply.Item2));
                foreach(var spec in new[]{(48,25,"(BC)12"),(49,25,"(BC)15"),(50,25,"(BC)24")}) {
                    var tile=new Vector2(spec.Item1,spec.Item2);farm.terrainFeatures.Remove(tile);var machine=ItemRegistry.Create<StardewValley.Object>(spec.Item3);machine.TileLocation=tile;farm.objects[tile]=machine;
                }
                var treeTile=new Vector2(52,25);farm.objects.Remove(treeTile);farm.terrainFeatures[treeTile]=new Tree("1",5);
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
            case "close":if(Game1.activeClickableMenu is CompanionMenu or EncyclopediaMenu or SharedGoalsMenu)Game1.exitActiveMenu();break;
            default:throw new InvalidOperationException("unknown_lab_scenario");
        }
        RefreshFacts(true);return StatusJson();
    }
}
