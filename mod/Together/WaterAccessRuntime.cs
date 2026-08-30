using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Tools;
using StardewValley.TerrainFeatures;
namespace Together;
public sealed partial class ModEntry {
    private bool ClearWaterAccess(SemanticJob job,GameLocation location,List<Point> sources) {
        if(!location.IsFarm||!location.IsOutdoors||job.WaterAccessEnergy>=40)return false;
        int dry=location.terrainFeatures.Values.OfType<HoeDirt>().Count(d=>d.crop!=null&&!d.crop.dead.Value&&d.needsWatering()&&d.state.Value!=1);
        int budget=Math.Min(40-job.WaterAccessEnergy,Math.Max(0,(int)Game1.player.Stamina-job.Reserve-dry*2));
        if(budget<=0)return false;
        var cells=new List<WaterAccessCell>();
        for(int y=0;y<location.Map.Layers[0].LayerHeight;y++)for(int x=0;x<location.Map.Layers[0].LayerWidth;x++) {
            var at=new Point(x,y);int cost=MaintenanceProtects(location,at)?0:PlotClearCost(location,at);
            if(cost>0&&location.objects.TryGetValue(at.ToVector2(),out var o)) {
                int tool=WorkSlot(i=>o.IsWeeds()?i is Tool t&&t.isScythe():o.IsTwig()?i is Axe:i is Pickaxe);
                if(tool<0)cost=0;
            }
            cells.Add(new(new(x,y),PlayerExecutor.Passable(location,at),cost));
        }
        var banks=sources.SelectMany(p=>new[]{new FarmCell(p.X-1,p.Y),new FarmCell(p.X+1,p.Y),new FarmCell(p.X,p.Y-1),new FarmCell(p.X,p.Y+1)}).ToHashSet();
        var route=WaterAccessPlan.Find(cells,new(Game1.player.TilePoint.X,Game1.player.TilePoint.Y),banks,budget);
        foreach(var tile in route) {
            var at=new Point(tile.X,tile.Y);int cost=PlotClearCost(location,at);if(cost<=0)continue;
            if(WorkStand(location,at)==null)return false;
            var obj=location.objects[at.ToVector2()];int slot=WorkSlot(i=>obj.IsWeeds()?i is Tool t&&t.isScythe():obj.IsTwig()?i is Axe:i is Pickaxe);
            if(slot<0)return false;
            job.WaterAccessEnergy+=cost;
            job.evidence.Add(new{kind="water_access_clearance",tile=new[]{at.X,at.Y},estimated_energy=cost,remaining_route_energy_budget=budget-cost,protected_dry_crops=dry});
            WorkChild(job,"player.work",new{skill="clear",slot,tiles=new[]{new{x=at.X,y=at.Y}}},"refill_clear");return true;
        }
        return false;
    }
}
