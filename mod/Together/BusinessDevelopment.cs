using System.Text.Json;
using StardewValley;
using StardewValley.Locations;

namespace Together;
public sealed partial class ModEntry {
    private IEnumerable<BusinessOption> BusinessDevelopmentOptions() {
        var b=Data.Business;var farm=Game1.getFarm();var animals=farm.getAllFarmAnimals().ToArray();var buildings=DataLoader.Buildings(Game1.content);
        double MaterialValue(IEnumerable<Requirement> needs)=>needs.Sum(n=>n.Count*(double)((ItemRegistry.Create(n.Item) as StardewValley.Object)?.sellToStorePrice()??0));
        int installed=GoalMachines().Count();
        foreach(var group in BusinessProductionChoices(false).GroupBy(c=>c.Machine)) {
            var best=group.OrderByDescending(c=>c.Margin/Math.Max(1,c.Minutes)).First();
            var recipe=goalRecipes.Values.FirstOrDefault(r=>r.Kind=="craft"&&r.Known&&r.Item==best.Machine);if(recipe==null)continue;
            int existing=GoalMachines().Count(m=>m.Object.QualifiedItemId==best.Machine);
            int wanted=Math.Min(existing+Math.Max(0,b.MaxMachines-installed),Math.Max(1,best.Available/(best.Count*2)));
            if(wanted<=existing)continue;
            double batchesPerDay=Math.Min(1440d/best.Minutes,best.Available/(double)best.Count/7);
            yield return new("machine:"+recipe.Id,"machine",recipe.Item,0,MaterialValue(recipe.Inputs),best.Margin*batchesPerDay,existing,wanted,"以当前原料存量估计一周供给；存在积压才扩容，按实际消耗继续重算",Array.Empty<string>());
        }
        var watered=farm.objects.Values.Where(o=>o.IsSprinkler()).SelectMany(o=>o.GetSprinklerTiles()).ToHashSet();
        int manual=farm.terrainFeatures.Pairs.Count(t=>t.Value is StardewValley.TerrainFeatures.HoeDirt {crop:not null} d&&!d.crop.dead.Value&&!watered.Contains(t.Key));
        foreach(var recipe in goalRecipes.Values.Where(r=>r.Kind=="craft"&&r.Known)) {
            if(ItemRegistry.Create(recipe.Item) is not StardewValley.Object sprinkler||!sprinkler.IsSprinkler()||manual<8)continue;
            int coverage=sprinkler.GetSprinklerTiles().Count();if(coverage<=0)continue;
            int existing=farm.objects.Values.Count(o=>o.QualifiedItemId==recipe.Item);
            yield return new("irrigation:"+recipe.Id,"machine",recipe.Item,0,MaterialValue(recipe.Inputs),Math.Min(coverage,manual),existing,existing+1,"优先覆盖已有手浇作物；每日收益字段在此为每格节省一次浇水的1金等价估值，不是现金收益",Array.Empty<string>());
        }
        if(animals.Length>=b.MaxAnimals)yield break;
        foreach(var pair in DataLoader.FarmAnimals(Game1.content).Where(a=>a.Value.PurchasePrice>0&&a.Value.RequiredBuilding!=null).OrderBy(a=>a.Value.PurchasePrice)) {
            var a=pair.Value;
            if(!GameStateQuery.CheckConditions(a.UnlockCondition,farm,Game1.player,random:new Random(0)))continue;
            var produce=a.ProduceItemIds.Select(v=>ItemRegistry.Create(v.ItemId) as StardewValley.Object).Where(o=>o!=null).ToArray();
            if(produce.Length==0)continue;
            // Purchased hay is a conservative fallback; grass isn't guaranteed daily.
            double daily=produce.Min(o=>o!.sellToStorePrice())/(double)Math.Max(1,a.DaysToProduce)-50;
            if(daily<=0)continue;
            var home=farm.buildings.FirstOrDefault(h=>!h.isUnderConstruction()&&h.buildingType.Value.Contains(a.House,StringComparison.Ordinal)&&h.GetIndoors() is AnimalHouse house&&!house.isFull()&&(h.buildingType.Value==a.RequiredBuilding||h.buildingType.Value.StartsWith("Deluxe ")));
            if(home==null) {
                string type=a.RequiredBuilding;var chain=new HashSet<string>();
                while(buildings.TryGetValue(type,out var current)&&!string.IsNullOrEmpty(current.BuildingToUpgrade)&&!Game1.IsBuildingConstructed(current.BuildingToUpgrade)&&chain.Add(type))type=current.BuildingToUpgrade;
                if(!buildings.TryGetValue(type,out var build)||Game1.IsBuildingConstructed(type)||farm.buildings.Any(h=>h.isUnderConstruction()||h.daysUntilUpgrade.Value>0))continue;
                if(!GameStateQuery.CheckConditions(build.BuildCondition,farm,Game1.player,random:new Random(0)))continue;
                var materials=(build.BuildMaterials??new()).Select(m=>new Requirement{Item=ItemRegistry.QualifyItemId(m.ItemId)??m.ItemId,Count=m.Amount});
                // Reserve the first animal and feed as well as the empty building.
                yield return new("building:"+type,"building",type,build.BuildCost+a.PurchasePrice+50*b.FeedDays,MaterialValue(materials),daily,0,1,"完整启动成本包含首只动物与饲料；实际建造只扣原生建筑费用",Array.Empty<string>());
            } else {
                int feed=Facts.HayInSilo+Facts.Stock.Where(s=>s.Item=="(O)178").Sum(s=>s.Count);
                yield return new("animal:"+pair.Key,"animal",pair.Key,a.PurchasePrice+Math.Max(0,(animals.Length+1)*b.FeedDays-feed)*50,0,daily,animals.Length,b.MaxAnimals,"购入前覆盖现有动物与新动物的饲料储备；成熟前没有预计收入",Array.Empty<string>());
            }
        }
    }
    private bool RunBusinessDevelopment() {
        var b=Data.Business;var options=BusinessDevelopmentOptions().Where(o=>o.Id==Data.Operating.Production.Selected).DistinctBy(o=>o.Id).ToArray();
        foreach(var option in BusinessMath.Rank(options,BusinessCashAvailable(ownDevelopment:true),0,-1,0)
            .OrderBy(o=>o.Id==b.PendingAsset?0:1)) {
            if(b.BlockedConditions.GetValueOrDefault(option.Id)==BusinessCondition())continue;
            var actions=new List<(string Tool,object Args)>();b.PendingAsset=option.Id;
            if(option.Kind=="machine") {
                if(!BusinessMaterials(new(){[option.Item]=1},actions))return b.Tasks.Count>0;
                actions.Add(("player.place_facility",new{item=option.Item,location="Farm"}));
                if(QueueBusiness(option.Id,actions,option.Reason))return true;
            } else if(option.Kind=="building") {
                var data=DataLoader.Buildings(Game1.content)[option.Item];
                if(!BusinessMaterials((data.BuildMaterials??new()).ToDictionary(m=>ItemRegistry.QualifyItemId(m.ItemId)??m.ItemId,m=>m.Amount),actions))return b.Tasks.Count>0;
                actions.Add(("player.service",new{location=data.Builder=="Wizard"?"WizardHouse":"ScienceHouse",service="build"}));
                actions.Add(("player.build",new{blueprint=option.Item,budget=data.BuildCost,keep_gold=b.KeepGold+option.Cash-data.BuildCost}));
                if(QueueBusiness(option.Id,actions,option.Reason,data.BuildCost))return true;
            } else if(option.Kind=="animal") {
                int count=Game1.getFarm().getAllFarmAnimals().Count(),hay=Math.Max(0,(count+1)*b.FeedDays-Facts.HayInSilo);
                if(!BusinessMaterials(new(){["(O)178"]=hay},actions))return b.Tasks.Count>0;
                int price=DataLoader.FarmAnimals(Game1.content)[option.Item].PurchasePrice;
                actions.Add(("player.acquire_animal",new{type=option.Item,name="伙伴"+(Game1.Date.TotalDays)+"-"+(++b.Revision),location="AnimalShop",budget=price,keep_gold=b.KeepGold}));
                if(QueueBusiness(option.Id,actions,option.Reason,price))return true;
            }
        }
        return false;
    }
}
