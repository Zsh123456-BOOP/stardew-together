using System.Text.Json;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class ModEntry {
    private IEnumerable<GameLocation> MaterialLocations() {
        var seen=new HashSet<GameLocation>();var queue=new Queue<GameLocation>(new[]{Game1.currentLocation,Game1.getFarm()}.Concat(Game1.locations));
        while(queue.TryDequeue(out var l)) {
            if(!seen.Add(l))continue;
            foreach(var building in l.buildings)if(building.GetIndoors() is {} indoor)queue.Enqueue(indoor);
            if(Knowledge.Index.Get("location:"+l.Name) is {} entry&&!Knowledge.Visible(entry))continue;
            if(l==Game1.currentLocation||PlayerExecutor.NextExit(Game1.currentLocation,l.NameOrUniqueName)!=null)yield return l;
        }
    }
    private (string Skill,string Location)? ReadyLivingSource(string item) {
        foreach(var l in MaterialLocations()) {
            if(l.terrainFeatures.Values.OfType<HoeDirt>().Any(d=>d.crop!=null&&!d.crop.dead.Value&&d.readyForHarvest()&&ItemRegistry.QualifyItemId(d.crop.indexOfHarvest.Value)==item))return ("harvest",l.NameOrUniqueName);
            if(l.objects.Values.Any(o=>!o.bigCraftable.Value&&o.isForage()&&o.QualifiedItemId==item))return ("forage",l.NameOrUniqueName);
        }
        return null;
    }
    private bool HasLivingMaterialRoute(string item) {
        if(ReadyLivingSource(item)!=null)return true;
        var crops=DataLoader.Crops(Game1.content);var farm=Game1.getFarm();
        return farm.terrainFeatures.Values.OfType<HoeDirt>().Any(d=>d.crop!=null&&!d.crop.dead.Value&&ItemRegistry.QualifyItemId(d.crop.indexOfHarvest.Value)==item)
            ||crops.Any(c=>ItemRegistry.QualifyItemId(c.Value.HarvestItemId)==item&&c.Value.Seasons.Contains(farm.GetSeason())&&Facts.Stock.Any(s=>s.Item=="(O)"+c.Key&&s.Count>0));
    }
    private bool PrepareLivingMaterial(SharedGoal goal,GoalNode node,Action<string,object,string> add,out string wait) {
        wait="";
        if(ReadyLivingSource(node.Item) is {} source) {
            add("work.run",new{goal=source.Skill,location=source.Location,item=node.Item,count=Math.Min(node.ToPrepare,999),quality=node.Quality},"按真实地图收集目标材料，品质以实际入包计数");return true;
        }
        var farm=Game1.getFarm();var standing=farm.terrainFeatures.Values.OfType<HoeDirt>().Where(d=>d.crop!=null&&!d.crop.dead.Value&&ItemRegistry.QualifyItemId(d.crop.indexOfHarvest.Value)==node.Item).ToArray();
        if(standing.Length>0) {
            if(standing.Any(d=>d.needsWatering()&&d.state.Value!=1))add("work.run",new{goal="water",location="Farm",count=0},"照料目标作物并补水，等待正常生长");
            else wait="target_crop_growing_wait_native_day_and_quality";
            return true;
        }
        var crops=DataLoader.Crops(Game1.content);
        var candidates=crops.Where(c=>ItemRegistry.QualifyItemId(c.Value.HarvestItemId)==node.Item&&c.Value.Seasons.Contains(farm.GetSeason()))
            .Select(c=>new{Seed="(O)"+c.Key,Data=c.Value}).Where(c=>Facts.Stock.Any(s=>s.Item==c.Seed&&s.Count>0)).ToArray();
        if(candidates.Length==0)return false;
        // No spending authority is inferred from a material goal. This branch uses
        // existing seeds and the existing layout/care policy; shopping is separate.
        var selected=candidates.OrderBy(c=>c.Data.DaysInPhase.Sum()).First();int carried=Game1.player.Items.Where(i=>i?.QualifiedItemId==selected.Seed).Sum(i=>i.Stack);
        if(carried==0) {
            int stored=SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer()).Where(i=>i?.QualifiedItemId==selected.Seed).Sum(i=>i.Stack);
            if(stored==0){wait="target_seeds_not_in_accessible_shared_storage";return true;}
            add("work.run",new{goal="withdraw",item=selected.Seed,count=Math.Min(stored,Math.Min(24,node.ToPrepare))},"取已有目标种子");return true;
        }
        if(Game1.currentLocation!=farm){add("player.travel",new{location="Farm"},"返回农场读取真实目标种植布局");return true;}
        var watered=farm.objects.Values.Where(o=>o.IsSprinkler()).SelectMany(o=>o.GetSprinklerTiles()).ToHashSet();
        int currentManual=farm.terrainFeatures.Pairs.Count(c=>c.Value is HoeDirt {crop:not null} d&&!d.crop.dead.Value&&!watered.Contains(c.Key));
        int manual=Math.Max(0,Data.FarmInvestment.ManualWaterLimit-currentManual),count=Math.Min(node.ToPrepare,Data.FarmInvestment.Plots);
        var result=JsonSerializer.SerializeToElement(PlanFarm(JsonSerializer.SerializeToElement(new{seed=selected.Seed,count,max_daily_manual_water=manual,priority="collection"})));
        var option=result.GetProperty("options").EnumerateArray().FirstOrDefault();
        if(option.ValueKind!=JsonValueKind.Object){wait="target_crop_no_feasible_season_space_or_care_capacity";return true;}
        add("work.run",new{goal="plant",plan_id=option.GetProperty("plan_id").GetString()},"按可达农田布局种目标作物；普通生长与随机品质仍需后续核验");return true;
    }
}
