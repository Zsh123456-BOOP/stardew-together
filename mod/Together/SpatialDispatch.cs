using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class ModEntry {
    private (string Map,Point? Tile) TaskPlace(AgentTaskSpec spec) {
        string map=AgentToolRegistry.Text(spec.args,"location",spec.location.Length>0?spec.location:Game1.currentLocation.NameOrUniqueName);
        if(spec.args.TryGetProperty("x",out _)&&spec.args.TryGetProperty("y",out _))return(map,new(AgentToolRegistry.Number(spec.args,"x"),AgentToolRegistry.Number(spec.args,"y")));
        string goal=AgentToolRegistry.Text(spec.args,"goal");
        if(map=="Farm"&&goal is "water" or "harvest" or "clear_dead") {
            var cells=Game1.getFarm().terrainFeatures.Pairs.Where(p=>p.Value is HoeDirt {crop:not null} d&&(goal=="water"?d.needsWatering()&&d.state.Value!=1:goal=="harvest"?d.readyForHarvest():d.crop.dead.Value)).Select(p=>p.Key.ToPoint());
            return(map,cells.OrderBy(p=>Math.Abs(p.X-Game1.player.TilePoint.X)+Math.Abs(p.Y-Game1.player.TilePoint.Y)).Select(p=>(Point?)p).FirstOrDefault());
        }
        if(map=="Farm"&&goal=="cleanup") {
            var order=Data.Maintenance.Orders.FirstOrDefault(o=>o.Id==AgentToolRegistry.Text(spec.args,"cleanup_id"));
            if(order!=null)return(map,CleanupTargets().Where(t=>CleanupMatches(order,t)).OrderBy(t=>Math.Abs(t.Tile.X-Game1.player.TilePoint.X)+Math.Abs(t.Tile.Y-Game1.player.TilePoint.Y)).Select(t=>(Point?)new Point(t.Tile.X,t.Tile.Y)).FirstOrDefault());
        }
        return(map,null);
    }
    private IEnumerable<ScheduledAgentTask> SpatialOrder(IEnumerable<ScheduledAgentTask> ready) {
        var here=Game1.player.TilePoint;string map=Game1.currentLocation.NameOrUniqueName;
        // Dependencies/time windows are already filtered by Ready. Never reorder
        // an active action, sleep, due care, or a nearly closing service behind
        // optional local work. Within a priority tier, avoid a second trip.
        return ready.Select(t=>{var at=TaskPlace(t.spec);int distance=at.Map!=map?80:at.Tile is {} tile?Math.Abs(tile.X-here.X)+Math.Abs(tile.Y-here.Y):0;return new{Task=t,Distance=distance};})
            .OrderByDescending(x=>x.Task.spec.priority>=80||x.Task.spec.deadline<=Game1.timeOfDay+100)
            .ThenBy(x=>x.Distance-x.Task.spec.priority/5).ThenBy(x=>x.Task.spec.id,StringComparer.Ordinal).Select(x=>x.Task);
    }
    private string spatialPreviousMap="",spatialPreviousRegion="",spatialBeforeRegion="";
    private int spatialDay=-1;
    private void RecordSpatialDispatch(ScheduledAgentTask task) {
        if(task.spec.actor!="player")return;
        if(spatialDay!=Game1.Date.TotalDays){spatialDay=Game1.Date.TotalDays;spatialPreviousMap=spatialPreviousRegion=spatialBeforeRegion="";}
        var at=TaskPlace(task.spec);string region=at.Tile is {} p?$"{at.Map}:{p.X/12},{p.Y/12}":at.Map;
        bool switched=spatialPreviousRegion.Length>0&&region!=spatialPreviousRegion;
        Data.Autoplay.Record("spatial_dispatch",AgentJson.Encode(new{task.spec.id,task.spec.tool,task.spec.purpose,from_map=Game1.currentLocation.NameOrUniqueName,from=new[]{Game1.player.TilePoint.X,Game1.player.TilePoint.Y},to_map=at.Map,to=at.Tile is {} tile?new[]{tile.X,tile.Y}:null,region,previous_region=spatialPreviousRegion,region_switch=switched,region_return=switched&&region==spatialBeforeRegion,note="12格区域代理指标；不是实际步行路程，原始 route_segment 仍为证据"}));
        if(switched)spatialBeforeRegion=spatialPreviousRegion;spatialPreviousRegion=region;spatialPreviousMap=at.Map;
    }
}
