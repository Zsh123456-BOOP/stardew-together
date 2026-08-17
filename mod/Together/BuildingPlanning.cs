using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace Together;
internal static class BuildingPlanning {
    public static List<Point> Sites(CarpenterMenu menu,CarpenterMenu.BlueprintEntry blueprint,int limit=3) {
        var l=menu.TargetLocation;
        if(blueprint.IsUpgrade)return l.buildings.Where(b=>b.buildingType.Value==blueprint.UpgradeFrom&&!b.isUnderConstruction()&&b.daysUntilUpgrade.Value==0).Select(b=>new Point(b.tileX.Value,b.tileY.Value)).Take(limit).ToList();
        var anchors=PlayerExecutor.Exits(l).Select(e=>new FarmCell(e.X,e.Y)).ToList();
        foreach(var b in l.buildings)if(b.humanDoor.Value.X>=0)anchors.Add(new(b.tileX.Value+b.humanDoor.Value.X,b.tileY.Value+b.humanDoor.Value.Y+1));
        foreach(var o in l.objects.Pairs.Where(o=>o.Value.bigCraftable.Value)) {
            var around=new[]{new Point((int)o.Key.X+1,(int)o.Key.Y),new Point((int)o.Key.X-1,(int)o.Key.Y),new Point((int)o.Key.X,(int)o.Key.Y+1),new Point((int)o.Key.X,(int)o.Key.Y-1)};
            foreach(var p in around)if(PlayerExecutor.Passable(l,p)){anchors.Add(new(p.X,p.Y));break;}
        }
        int w=l.Map.Layers[0].LayerWidth,h=l.Map.Layers[0].LayerHeight;
        var grid=new List<LayoutCell>();for(int y=0;y<h;y++)for(int x=0;x<w;x++)grid.Add(new(new(x,y),false,PlayerExecutor.Passable(l,new(x,y)),false,false,false,false,0));
        var start=anchors.FirstOrDefault(a=>grid.Any(c=>c.Tile==a&&c.Passable));
        if(!grid.Any(c=>c.Tile==start&&c.Passable))return new();
        var candidates=new List<Point>();
        for(int y=1;y<h-blueprint.TilesHigh-1;y++)for(int x=1;x<w-blueprint.TilesWide-1;x++) {
            var at=new Point(x,y);if(!Clear(l,blueprint,at))continue;candidates.Add(at);
        }
        var result=new List<Point>();
        foreach(var at in candidates.OrderBy(p=>Math.Abs(p.X-start.X)+Math.Abs(p.Y-start.Y)).ThenBy(p=>p.Y).ThenBy(p=>p.X)) {
            var blocked=Enumerable.Range(at.Y,blueprint.TilesHigh).SelectMany(y=>Enumerable.Range(at.X,blueprint.TilesWide).Select(x=>new FarmCell(x,y)));
            var access=new List<FarmCell>();var door=blueprint.Data.HumanDoor;if(door.X>=0)access.Add(new(at.X+door.X,at.Y+door.Y+1));
            var animal=blueprint.Data.AnimalDoor;if(animal.Width>0&&animal.Height>0)access.Add(new(at.X+animal.X,at.Y+animal.Bottom));
            if(!FarmLayout.KeepsAccess(grid,start,anchors,blocked,access))continue;
            result.Add(at);if(result.Count>=limit)break;
        }
        return result;
    }
    public static bool Clear(GameLocation l,CarpenterMenu.BlueprintEntry b,Point at) {
        bool Allowed(Point p,bool passableOnly=false) {
            var v=p.ToVector2();return l.isTileOnMap(p.X,p.Y)&&l.isBuildable(v,passableOnly)&&!l.objects.ContainsKey(v)&&!l.terrainFeatures.ContainsKey(v)
                &&l.farmers.All(f=>!f.GetBoundingBox().Intersects(new Rectangle(p.X*64,p.Y*64,64,64)));
        }
        for(int y=0;y<b.TilesHigh;y++)for(int x=0;x<b.TilesWide;x++)if(!Allowed(new(at.X+x,at.Y+y)))return false;
        foreach(var extra in b.Data.AdditionalPlacementTiles??new())for(int y=extra.TileArea.Top;y<extra.TileArea.Bottom;y++)for(int x=extra.TileArea.Left;x<extra.TileArea.Right;x++)if(!Allowed(new(at.X+x,at.Y+y),extra.OnlyNeedsToBePassable))return false;
        var door=b.Data.HumanDoor;return door.X<0||l.isBuildable(new Vector2(at.X+door.X,at.Y+door.Y+1),true)||l.isPath(new Vector2(at.X+door.X,at.Y+door.Y+1));
    }
}
