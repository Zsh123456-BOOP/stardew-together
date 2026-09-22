using System.Text.Json;
using StardewValley;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;
namespace Together;
public sealed partial class ModEntry {
    private object FacilityAssets()=>new {
        observed_day=Game1.Date.TotalDays,observed_time=Game1.timeOfDay,
        storage=SharedStorage().Select(s=>new{item=s.Chest.QualifiedItemId,location=s.Location.NameOrUniqueName,x=(int)s.Tile.X,y=(int)s.Tile.Y,capacity=s.Chest.GetActualCapacity(),used=s.Chest.GetItemsForPlayer().Count(i=>i!=null),busy=s.Chest.GetMutex().IsLocked()}),
        goals=Data.SharedGoals.Where(g=>g.Status is "active" or "paused"||g.Completion=="placed"&&g.Status=="fulfilled").Select(g=>new{g.Id,g.Entity,g.Count,g.Completion,g.Status,g.AutoExecute,g.AutoBlockedReason,g.Summary}),
        rule="placed count为该地点目标总量；已放置设施以storage为准，存货用store。storage_expand默认复用可用仓库；明确追加用additional:true或goal.create设置更高总量。"
    };
    private object FarmLaborBudget() {
        var farm=Game1.getFarm();var irrigated=farm.objects.Values.Where(o=>o.IsSprinkler()).SelectMany(o=>o.GetSprinklerTiles()).ToHashSet();
        var care=farm.terrainFeatures.Pairs.Where(p=>p.Value is HoeDirt {crop:not null} d&&!d.crop.dead.Value&&d.needsWatering()).ToArray();
        return OperatingDecisionPolicy.Labor(Game1.player.Stamina,Game1.player.MaxStamina,PendingFarmEnergy(),care.Count(p=>((HoeDirt)p.Value).state.Value!=1),care.Count(p=>!irrigated.Contains(p.Key)),OwnedSeeds().Sum(i=>i.Stack),Game1.player.FarmingLevel);
    }
    private bool ExistingStorageAcceptsCargo() {
        var cargo=Game1.player.Items.Where(i=>i!=null&&StoreCount(i)>0).ToArray();
        return SharedStorage().Any(s=> {
            if(s.Chest.GetMutex().IsLocked()||WorkStand(s.Location,s.Tile.ToPoint())==null)return false;
            var adapter=new CapacityAdapter(s.Chest.GetActualCapacity(),s.Chest.GetItemsForPlayer(),cargo);
            return CapacityPlan.Simulate(adapter.Snapshot,cargo.Select(i=>adapter.Put(i,StoreCount(i))).ToArray()).Feasible;
        });
    }
    private void PreparePurchaseVisit(ScheduledAgentTask task) {
        if(task.spec.tool!="player.buy"||Game1.activeClickableMenu is ShopMenu)return;
        var args=task.spec.args;string shop=AgentToolRegistry.Text(args,"shop");
        bool recipe=args.TryGetProperty("recipe",out var flag)&&flag.ValueKind==JsonValueKind.True;
        var locations=PlayerExecutor.ShopSources(AgentToolRegistry.Text(args,"item"),recipe).Where(s=>s.Shop==shop).Select(s=>s.Location).Distinct().ToArray();
        if(!OperatingDecisionPolicy.CanCompletePurchaseVisit(args,locations))return;
        // Preserve every explicit price/budget/quantity. Static shop data supplies only the destination.
        var amended=args.Deserialize<Dictionary<string,JsonElement>>()!;amended["location"]=JsonSerializer.SerializeToElement(locations[0]);
        task.spec.tool="player.procure";task.spec.args=JsonSerializer.SerializeToElement(amended);task.spec.location=locations[0];
        Data.Autoplay.Record("purchase_visit_prepared",AgentJson.Encode(new{task.spec.id,original="player.buy",tool=task.spec.tool,args=task.spec.args,note="原生开店和现场核价仍由采购执行器完成，不补造报价或预算"}));
    }
}
