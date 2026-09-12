using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace Together;
public sealed partial class ModEntry {
    // Object.placementAction materializes these native objects as Chest. The
    // carried ItemRegistry prototype is Object, not Chest (native 1.6.15).
    private static bool NativeStorageItem(Item item)=>item is Chest||item.QualifiedItemId is "(BC)130" or "(BC)232" or "(BC)BigChest" or "(BC)BigStoneChest";
    private IReadOnlyDictionary<FarmCell,int> GoalFacilityCosts(GameLocation location,Item item,IReadOnlyList<LayoutCell> grid) {
        if(location!=Game1.getFarm()||!NativeStorageItem(item))return new Dictionary<FarmCell,int>();
        var home=FarmHome(Game1.getFarm());var district=District(Game1.getFarm());
        var homeRoutes=FarmLayout.WalkDistances(grid,home);
        var fieldRoutes=FarmLayout.WalkDistances(grid,district.Field.OrderBy(t=>FarmDistrict.Distance(t,home)).FirstOrDefault(home));
        int Route(Dictionary<FarmCell,int> map,FarmCell p)=>new[]{new FarmCell(p.X+1,p.Y),new(p.X-1,p.Y),new(p.X,p.Y+1),new(p.X,p.Y-1)}.Select(t=>map.GetValueOrDefault(t,10000)).Min();
        return grid.ToDictionary(c=>c.Tile,c=>FarmDistrict.Distance(c.Tile,home)<=2||district.Field.Contains(c.Tile)||Route(homeRoutes,c.Tile)>24?100000:Route(homeRoutes,c.Tile)*3+Route(fieldRoutes,c.Tile)*2);
    }
    // The storage policy authorizes a capability, not a second wood/craft loop.
    // Its existing budget is wood-only; recipes requiring other investment are
    // exposed to the model, never silently authorized by this policy adapter.
    private SharedGoal? EnsureStorageGoal(bool force=false) {
        var policy=Data.Storage;
        if(!SinglePlayerMode||!policy.AutoExpand||policy.MaxSharedChests<1)return null;
        var existing=Data.SharedGoals.FirstOrDefault(g=>g.Purpose=="policy:shared_storage"&&g.Status=="active");if(existing!=null)return existing;
        var stores=SharedStorage().ToArray();
        if(stores.Length>=policy.MaxSharedChests||!force&&stores.Length>0)return null;
        ReadGoalRecipes();
        var candidates=goalRecipes.Values.Where(r=>r.Kind=="craft"&&r.Supported&&Game1.player.craftingRecipes.ContainsKey(r.Id[6..])&&NativeStorageItem(ItemRegistry.Create(r.Item))&&r.Inputs.All(i=>i.Item=="(O)388")).ToArray();
        var recipe=candidates.OrderBy(r=>r.Inputs.Sum(i=>i.Count)).ThenBy(r=>r.Id,StringComparer.Ordinal).FirstOrDefault();if(recipe==null)return null;
        int cost=recipe.Inputs.Sum(i=>i.Count);
        if(policy.BudgetDay!=Game1.Date.TotalDays){policy.BudgetDay=Game1.Date.TotalDays;policy.WoodReserved=0;}
        bool owned=Game1.player.Items.Any(i=>i?.QualifiedItemId==recipe.Item)||stores.Any(s=>s.Chest.GetItemsForPlayer().Any(i=>i?.QualifiedItemId==recipe.Item));
        if(!owned&&policy.WoodReserved+cost>policy.WoodBudgetPerDay)return null;
        var goal=(SharedGoal)AgentGoalCreate(JsonSerializer.SerializeToElement(new{request_id="storage-"+Guid.NewGuid().ToString("N"),entity=recipe.Id,count=Game1.getFarm().objects.Values.Count(o=>o.QualifiedItemId==recipe.Item)+1,completion="placed",purpose="policy:shared_storage"}));
        if(!owned)policy.WoodReserved+=cost;
        goal.AutoExecute=true;
        Data.Autoplay.Record("infrastructure_goal",AgentJson.Encode(new{goal.Id,goal.Entity,goal.Count,goal.Purpose,authorized_cost=owned?0:cost,policy.WoodReserved,free_slots=CapacityAdapter.Of(Game1.player).FreeSlots,trigger="authorized_storage_before_production"}));
        return goal;
    }
    private void GoalFacilityPlaced(string goalId,GameLocation location,Point tile) {
        if(Data.SharedGoals.FirstOrDefault(g=>g.Id==goalId) is not {Purpose:"policy:shared_storage"} goal)return;
        if(!location.objects.TryGetValue(tile.ToVector2(),out var item)||item is not Chest {playerChest.Value:true} chest)throw new InvalidOperationException("native_storage_placement_not_verified");
        chest.modData[WorkChestRole]="output";
        Data.Autoplay.Record("goal_facility_verified",AgentJson.Encode(new{goal.Id,item=item.QualifiedItemId,location=location.NameOrUniqueName,tile,capability="shared_storage",free_slots=CapacityAdapter.Of(Game1.player).FreeSlots}));
    }
}
