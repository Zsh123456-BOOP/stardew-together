using Microsoft.Xna.Framework;
using StardewValley;
using TheStardewSquad.Framework;
using TheStardewSquad.Framework.Squad;
using TheStardewSquad.Framework.Wrappers;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    private SquadTask? FishingTask(ISquadMate mate) {
        if(!mate.CanPerformTask(TaskType.Fishing))return null;
        var location=mate.Npc.currentLocation;var origin=mate.Npc.TilePoint;
        var info=new LocationInfoWrapper(location,mate.Npc);
        var map=new MapInfoWrapper(location,mate.Npc);
        int width=location.Map.Layers[0].LayerWidth,height=location.Map.Layers[0].LayerHeight;
        var passable=new Dictionary<Point,bool>();
        bool Pass(Point p) {
            if(p.X<0 || p.Y<0 || p.X>=width || p.Y>=height)return false;
            if(!passable.TryGetValue(p,out bool value))passable[p]=value=map.IsTilePassable(p);
            return value;
        }
        var claimed=records.Values.Where(r=>r.Status=="running" && r.Skill=="fish" && r.Actor!=Id(mate) && r.Location==location && r.Assigned!=null)
            .Select(r=>r.Assigned!.InteractionTile).ToHashSet();
        var region=ReachableRegion(mate);
        foreach(var water in TaskManager.FindNearbyWaterTiles(info,origin,32)) {
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++) {
                if(x==0 && y==0)continue;
                var adjacent=new Point(water.X+x,water.Y+y);
                if(region.Contains(adjacent) && Pass(adjacent) && !claimed.Contains(adjacent))return new SquadTask(TaskType.Fishing,water,adjacent,isManual:true);
                if(Pass(adjacent))continue;
                var distant=new Point(water.X+x*2,water.Y+y*2);
                if(region.Contains(distant) && Pass(distant) && !claimed.Contains(distant))return new SquadTask(TaskType.Fishing,water,distant,isManual:true);
            }
        }
        return null;
    }
}
