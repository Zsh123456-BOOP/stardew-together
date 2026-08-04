using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class ModEntry {
    public override object GetApi()=>new LabApi(this);
    public sealed class LabApi {
        private readonly ModEntry mod;
        public LabApi(ModEntry mod){this.mod=mod;}
        public string RunScenario(string json)=>mod.RunLabScenario(json);
        public string GetDiagnostics()=>mod.StatusJson();
    }
    private string RunLabScenario(string json) {
        if(!Settings.EnableLab || !Context.IsWorldReady || Context.IsMultiplayer || Game1.player.Name!="AgentLab")throw new InvalidOperationException("isolated_lab_required");
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;string scenario=root.GetProperty("scenario").GetString()!;
        string Arg(string key,string fallback="")=>root.TryGetProperty(key,out var value)?value.GetString()??fallback:fallback;
        int Num(string key,int fallback=0)=>root.TryGetProperty(key,out var value)?value.GetInt32():fallback;
        var farm=Game1.getFarm();
        switch(scenario) {
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
                    animal.home=coop;animal.homeLocation.Value=new Vector2(coop.tileX.Value,coop.tileY.Value);animal.currentLocation=house;
                    house.animals.Add(animal.myID.Value,animal);house.animalsThatLiveHere.Add(animal.myID.Value);
                }
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
                var npc=Game1.getCharacterFromName(Arg("npc","Abigail"));string location=Arg("location","Farm");
                var destination=location.StartsWith("UndergroundMine")?MineShaft.GetMine(location):Game1.getLocationFromName(location);
                if(npc==null || destination==null)throw new InvalidOperationException("fixture_target_missing");
                Game1.warpCharacter(npc,destination,new Vector2(Num("x",44),Num("y",23)));break;
            }
            case "warp_player":Game1.warpFarmer(Arg("location","Farm"),Num("x",44),Num("y",23),false);break;
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
            case "sleep": {
                farm.animals.Remove(-449404282); // remove only the homeless petting fixture
                Game1.currentLocation.answerDialogueAction("Sleep_Yes",null);break;
            }
            case "panel":Game1.activeClickableMenu=new CompanionMenu(this,Num("tab",5));break;
            case "close":if(Game1.activeClickableMenu is CompanionMenu)Game1.exitActiveMenu();break;
            default:throw new InvalidOperationException("unknown_lab_scenario");
        }
        RefreshFacts(true);return StatusJson();
    }
}
