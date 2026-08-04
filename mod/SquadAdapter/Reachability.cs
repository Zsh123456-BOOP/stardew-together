using Microsoft.Xna.Framework;
using StardewValley;
using TheStardewSquad.Framework.Squad;
using TheStardewSquad.Framework.Wrappers;
using TheStardewSquad.Pathfinding;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    private sealed record Region(GameLocation Location,int Objects,int Features,DateTime Until,HashSet<Point> Tiles);
    private readonly Dictionary<string,Region> regions=new();
    // A short-lived candidate hint, shared across targets. Execution still checks
    // each live path tile and the target before applying any game effect.
    private HashSet<Point> ReachableRegion(ISquadMate mate) {
        var location=mate.Npc.currentLocation;string id=Id(mate);var origin=mate.Npc.TilePoint;
        if(regions.TryGetValue(id,out var known) && known.Location==location && known.Until>DateTime.UtcNow
            && known.Objects==location.objects.Count() && known.Features==location.terrainFeatures.Count() && known.Tiles.Contains(origin))return known.Tiles;
        int width=location.Map.Layers[0].LayerWidth,height=location.Map.Layers[0].LayerHeight;
        var map=new MapInfoWrapper(location,mate.Npc);var checkedTiles=new HashSet<Point>{origin};
        var reachable=new HashSet<Point>{origin};var pending=new Queue<Point>();pending.Enqueue(origin);
        var offsets=new[]{new Point(1,0),new Point(-1,0),new Point(0,1),new Point(0,-1)};
        while(pending.Count>0 && checkedTiles.Count<Math.Min(16384,width*height)) {
            var tile=pending.Dequeue();
            foreach(var d in offsets) {
                var next=new Point(tile.X+d.X,tile.Y+d.Y);
                if(next.X<0 || next.Y<0 || next.X>=width || next.Y>=height || !checkedTiles.Add(next) || !map.IsTilePassable(next))continue;
                reachable.Add(next);pending.Enqueue(next);
            }
        }
        regions[id]=new(location,location.objects.Count(),location.terrainFeatures.Count(),DateTime.UtcNow.AddSeconds(2),reachable);return reachable;
    }
    private Point? StandingSpot(ISquadMate mate,Point point) {
        var region=ReachableRegion(mate);
        var neighbors=new[]{new Point(point.X,point.Y+1),new Point(point.X-1,point.Y),new Point(point.X+1,point.Y),new Point(point.X,point.Y-1)};
        return neighbors.Where(p=>region.Contains(p) && AStarPathfinder.IsTilePassableForFollower(mate.Npc.currentLocation,p,mate.Npc))
            .OrderBy(p=>Vector2.DistanceSquared(p.ToVector2(),mate.Npc.Tile)).Select(p=>(Point?)p).FirstOrDefault();
    }
}
