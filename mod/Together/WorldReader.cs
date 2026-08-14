using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;

namespace Together;

public sealed class WorldFacts {
    public int SpentToday {get;set;}
    public Dictionary<string,int> Purchased {get;set;}=new();
    public List<string> Transactions {get;set;}=new();
    public int FeedNeeded {get;set;}
    public int HayInSilo {get;set;}
    public List<ProgressGoal> Goals {get;set;}=new();
    public List<object> Objectives {get;set;}=new();
    public List<SeedFact> Seeds {get;set;}=new();
    public List<PlanNode> CareLocations {get;set;}=new();
    public int Day {get;set;}
    public int Time {get;set;}
    public string Season {get;set;}="";
    public string Route {get;set;}="undecided";
    public int Money {get;set;}
    public int DryCrops {get;set;}
    public int RipeCrops {get;set;}
    public int DeadCrops {get;set;}
    public int MachinesReady {get;set;}
    public int AnimalsUnpetted {get;set;}
    public List<object> Crops {get;set;}=new();
    public List<object> Animals {get;set;}=new();
    public List<object> Machines {get;set;}=new();
    public List<object> Quests {get;set;}=new();
    public List<BundleFact> Bundles {get;set;}=new();
    public List<StockFact> Stock {get;set;}=new();
    public List<ContainerFact> Containers {get;set;}=new();
    public List<string> Alerts {get;set;}=new();
    public Dictionary<string,int> Progress {get;set;}=new();
    public List<string> Errors {get;set;}=new();
}
public sealed class ContainerFact {
    public string source {get;set;}="";
    public string role {get;set;}="none";
}
public sealed class StockFact {
    public string Item {get;set;}="";
    public string Name {get;set;}="";
    public string Location {get;set;}="";
    public int Count {get;set;}
    public int Quality {get;set;}
    public int Category {get;set;}
}
public sealed class BundleFact {
    public string Id {get;set;}="";
    public string Name {get;set;}="";
    public bool Complete {get;set;}
    public int RequiredSlots {get;set;}
    public int CompletedSlots {get;set;}
    public List<Requirement> Missing {get;set;}=new();
}
public static class WorldReader {
    private static Dictionary<string,object> QuestConditions(StardewValley.Quests.Quest quest) {
        var data=new Dictionary<string,object>();
        foreach(var field in quest.GetType().GetFields(System.Reflection.BindingFlags.Public|System.Reflection.BindingFlags.Instance)) {
            if(field.DeclaringType==typeof(StardewValley.Quests.Quest))continue;
            var value=field.GetValue(quest);if(value==null)continue;
            var raw=value.GetType().GetProperty("Value")?.GetValue(value)??value;
            if(raw is string or int or bool or long)data[field.Name]=raw;
        }
        return data;
    }
    // Called on the game thread; never expose mutable game objects to HTTP/model tasks.
    public static WorldFacts Read(string[]? companions=null) {
        var p=Game1.player;var farm=Game1.getFarm();
        var f=new WorldFacts{Day=Game1.Date.TotalDays,Time=Game1.timeOfDay,Season=Game1.currentSeason,Money=p.Money,
            Route=Game1.MasterPlayer.mailReceived.Contains("JojaMember")?"joja":Game1.MasterPlayer.mailReceived.Contains("ccIsComplete")?"community_complete":"undecided_or_community"};
        void Stock(IEnumerable<Item> items,string source) {
            foreach(var item in items.Where(i=>i!=null))f.Stock.Add(new(){Item=item.QualifiedItemId,Name=item.DisplayName,Count=item.Stack,Quality=item.Quality,Category=item.Category,Location=source});
        }
        Stock(p.Items,"player");
        Stock(p.team.GetOrCreateGlobalInventory($"TheStardewSquad_SquadInventory_{p.UniqueMultiplayerID}"),"squad_inventory");
        foreach(var name in companions??Array.Empty<string>())Stock(p.team.GetOrCreateGlobalInventory($"Together_Pouch_{p.UniqueMultiplayerID}_{name}"),"npc_pouch:"+name);
        var locations=new List<GameLocation>{farm};
        foreach(var building in farm.buildings) {var indoors=building.GetIndoors();if(indoors!=null)locations.Add(indoors);}
        foreach(var location in locations) {
            foreach(var entry in location.terrainFeatures.Pairs)if(entry.Value is HoeDirt dirt && dirt.crop is {} crop) {
                bool dead=crop.dead.Value,ripe=dirt.readyForHarvest(),dry=!dead && !ripe && dirt.state.Value==HoeDirt.dry;
                if(dead)f.DeadCrops++;if(ripe)f.RipeCrops++;if(dry)f.DryCrops++;
                f.Crops.Add(new{location=location.NameOrUniqueName,x=(int)entry.Key.X,y=(int)entry.Key.Y,item=crop.indexOfHarvest.Value,dead,ripe,dry,
                    phase=crop.currentPhase.Value,day_in_phase=crop.dayOfCurrentPhase.Value,phase_days=crop.phaseDays.ToArray()});
            }
            foreach(var entry in location.objects.Pairs) {
                if(entry.Value is Chest chest) {
                    string source=location.NameOrUniqueName+":"+entry.Key.X+","+entry.Key.Y;
                    Stock(chest.GetItemsForPlayer(p.UniqueMultiplayerID),source);
                    f.Containers.Add(new(){source=source,role=chest.modData.TryGetValue("stardewagent.together/chest-role",out var role)?role:"none"});
                }
                else if(entry.Value.bigCraftable.Value) {
                    var machine=entry.Value;
                    if(machine.readyForHarvest.Value)f.MachinesReady++;
                    if(machine.GetMachineData()!=null)f.Machines.Add(new{location=location.NameOrUniqueName,x=(int)entry.Key.X,y=(int)entry.Key.Y,
                        name=machine.DisplayName,item=machine.QualifiedItemId,ready=machine.readyForHarvest.Value,
                        processing_output=machine.heldObject.Value?.QualifiedItemId,minutes_remaining=machine.MinutesUntilReady});
                }
            }
        }
        foreach(var animal in farm.getAllFarmAnimals()) {
            if(!animal.wasPet.Value)f.AnimalsUnpetted++;
            f.Animals.Add(new{name=animal.displayName,location=animal.currentLocation?.NameOrUniqueName,x=animal.TilePoint.X,y=animal.TilePoint.Y,home=animal.home?.GetIndoors()?.NameOrUniqueName,pet=animal.wasPet.Value,fullness=animal.fullness.Value});
        }
        foreach(var q in p.questLog) {
            try{f.Quests.Add(new{id=q.id.Value,type=q.GetType().Name,title=q.questTitle,objective=q.currentObjective,complete=q.completed.Value,
                days_left=q.daysLeft.Value,daily=q.dailyQuest.Value,conditions=QuestConditions(q),credit="read_only_player_quest; companion labor does not imply completion"});}
            catch{f.Errors.Add("quest:"+q.id.Value);}
        }
        foreach(var pair in Game1.netWorldState.Value.BundleData) {
            try {
                var key=pair.Key.Split('/');int id=int.Parse(key[1]);var fields=pair.Value.Split('/');
                var tokens=fields[2].Split(' ',StringSplitOptions.RemoveEmptyEntries);
                Game1.netWorldState.Value.Bundles.TryGetValue(id,out var donated);
                int slots=tokens.Length/3;
                int required=fields.Length>4 && int.TryParse(fields[4],out int n) && n>0?n:slots;
                var b=new BundleFact{Id=id.ToString(),Name=fields[0],RequiredSlots=required};
                for(int i=0;i<slots;i++) {
                    bool complete=donated!=null && i<donated.Length && donated[i];
                    if(complete){b.CompletedSlots++;continue;}
                    string item=tokens[i*3];int count=int.Parse(tokens[i*3+1]),quality=int.Parse(tokens[i*3+2]);
                    string qualified=item.StartsWith("(")?item:"(O)"+item;
                    b.Missing.Add(new(){Name=item=="-1"?"金币":ItemRegistry.GetDataOrErrorItem(qualified).DisplayName,Item=qualified,Count=count,Quality=quality,Owned=item=="-1"?p.Money:f.Stock.Where(x=>(x.Item==qualified || (int.TryParse(item,out int category) && category<0 && x.Category==category)) && x.Quality>=quality).Sum(x=>x.Count),Source="bundle:"+id});
                }
                b.Complete=b.CompletedSlots>=required;f.Bundles.Add(b);
            }catch{f.Errors.Add("bundle:"+pair.Key);}
        }
        f.Progress=new(){["achievements"]=p.achievements.Count,["fish_species"]=p.fishCaught.Count(),["shipped_species"]=p.basicShipped.Count(),
            ["bundles_complete"]=f.Bundles.Count(b=>b.Complete),["quests_complete"]=p.questLog.Count(q=>q.completed.Value),
            ["farming_level"]=p.FarmingLevel,["fishing_level"]=p.FishingLevel,["mining_level"]=p.MiningLevel,["monster_kills"]=(int)p.stats.MonstersKilled};
        if(f.DryCrops>0)f.Alerts.Add($"还有 {f.DryCrops} 株作物需要浇水");
        if(f.RipeCrops>0)f.Alerts.Add($"{f.RipeCrops} 株成熟作物可以收获");
        if(f.AnimalsUnpetted>0)f.Alerts.Add($"{f.AnimalsUnpetted} 只动物还没被抚摸");
        if(f.MachinesReady>0)f.Alerts.Add($"{f.MachinesReady} 台机器可以收取");
        if(Game1.dayOfMonth>=25)f.Alerts.Add("临近换季：播种前需要核对剩余生长天数");
        ProgressReader.Read(f);
        if(p.modData.TryGetValue("stardewagent.together/economy",out var ledger)) {
            try {
                using var document=System.Text.Json.JsonDocument.Parse(ledger);var root=document.RootElement;
                f.SpentToday=root.GetProperty("Day").GetInt32()==f.Day?root.GetProperty("Spent").GetInt32():0;
                f.Purchased=root.GetProperty("Purchased").EnumerateObject().ToDictionary(x=>x.Name,x=>x.Value.GetInt32());
                f.Transactions=root.GetProperty("Entries").EnumerateArray().Select(x=>x.GetString()??"").TakeLast(20).ToList();
            }catch{f.Errors.Add("economy_ledger_unreadable");}
        }
        return f;
    }
}
