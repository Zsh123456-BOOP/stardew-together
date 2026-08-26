using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class PlayerExecutor {
    private string orchardMode="",orchardItem="";
    private int orchardCount;
    private Point? orchardTile;
    private readonly HashSet<Point> orchardVisited=new();
    private readonly Queue<Point> orchardPickupWalk=new();
    private Dictionary<string,int> orchardHarvest=new(),orchardBefore=new();
    internal static bool ProtectsOrchardGrowth(GameLocation location,Point tile)=>location.terrainFeatures.Pairs.Any(p=>p.Value is FruitTree {daysUntilMature.Value:>0}&&Math.Abs(p.Key.X-tile.X)<=1&&Math.Abs(p.Key.Y-tile.Y)<=1);
    internal static object ReadOrchard()=>new{location=Game1.currentLocation.NameOrUniqueName,trees=Game1.currentLocation.terrainFeatures.Pairs.Where(p=>p.Value is FruitTree).Select(p=>{var tree=(FruitTree)p.Value;return new{x=p.Key.X,y=p.Key.Y,id=tree.treeId.Value,days=tree.daysUntilMature.Value,stage=tree.growthStage.Value,growth_blocked=FruitTree.IsGrowthBlocked(p.Key,Game1.currentLocation),fruit=tree.fruit.Select(i=>AgentToolRegistry.ItemInfo(i))};})};
    private void StartOrchard(JsonElement args) {
        orchardMode=AgentToolRegistry.Text(args,"mode","harvest");orchardItem=AgentToolRegistry.Text(args,"item");orchardCount=AgentToolRegistry.Number(args,"count",orchardMode=="plant"?1:0);destination=AgentToolRegistry.Text(args,"location","Farm");
        if(orchardMode is not ("plant" or "harvest")||orchardCount<0||orchardCount>(orchardMode=="plant"?16:100)||orchardMode=="plant"&&orchardCount==0)throw new InvalidOperationException("invalid_orchard_operation");
        if(Game1.getLocationFromName(destination)==null)throw new InvalidOperationException("unknown_orchard_location");
        if(orchardMode=="plant"&&!Game1.player.Items.Any(i=>i is StardewValley.Object o&&o.QualifiedItemId==orchardItem&&o.IsFruitTreeSapling()))throw new InvalidOperationException("carried_fruit_tree_sapling_required");
        orchardTile=null;orchardVisited.Clear();orchardPickupWalk.Clear();Current!.phase="orchard_travel";
    }
    private void TickOrchard() {
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("orchard_menu_interrupted");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        var location=Game1.currentLocation;
        if(Current!.phase=="orchard_pickup") {
            if(!AtWalkTarget){MonitorWalk();return;}StopWalk();
            if(orchardPickupWalk.TryDequeue(out var point)){Walk(point);return;}
            Current.phase="orchard_settle";nextInteraction=DateTime.UtcNow.AddSeconds(1);return;
        }
        if(Current.phase=="orchard_settle") {
            if(DateTime.UtcNow<nextInteraction)return;
            var received=orchardHarvest.ToDictionary(p=>p.Key,p=>Game1.player.Items.Where(i=>i?.QualifiedItemId==p.Key).Sum(i=>i.Stack)-orchardBefore[p.Key]);
            Current.effects.Add(new{kind="native_fruit_collection",expected=orchardHarvest,received});
            if(orchardHarvest.Any(p=>received[p.Key]<p.Value))throw new InvalidOperationException("fruit_debris_not_fully_collected");
            Current.completed++;orchardTile=null;Current.phase="orchard_next";
        }
        if(orchardCount>0&&Current.completed>=orchardCount){Finish("succeeded");return;}
        if(Game1.timeOfDay>=2200)throw new InvalidOperationException("orchard_return_time_reached");
        if(orchardTile==null) {
            if(orchardMode=="harvest") {
                foreach(var pair in location.terrainFeatures.Pairs.Where(p=>p.Value is FruitTree tree&&tree.fruit.Count>0&&!orchardVisited.Contains(p.Key.ToPoint())).OrderBy(p=>Vector2.DistanceSquared(p.Key,Game1.player.Tile)))try{Walk(Approach(pair.Key.ToPoint(),true));orchardTile=pair.Key.ToPoint();break;}catch(InvalidOperationException){orchardVisited.Add(pair.Key.ToPoint());}
                if(orchardTile==null) {
                    if(location.terrainFeatures.Pairs.Any(p=>p.Value is FruitTree tree&&tree.fruit.Count>0))throw new InvalidOperationException("remaining_fruit_trees_unreachable");
                    Finish("succeeded");return;
                }
            }else FindOrchardSite();
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Point selected=orchardTile!.Value;Adjacent(selected);Face(selected);
        if(orchardMode=="plant") {
            int slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i]?.QualifiedItemId==orchardItem,-1);
            if(slot<0||Game1.player.Items[slot] is not StardewValley.Object sapling)throw new InvalidOperationException("orchard_sapling_supply_missing");
            if(!OrchardSiteLegal(location,selected,sapling))throw new InvalidOperationException("orchard_growth_area_changed");
            ValidateConsumption?.Invoke(new Dictionary<Item,int>{{sapling,1}},"","orchard:"+orchardItem);Game1.player.CurrentToolIndex=slot;Game1.player.netItemStowed.Value=false;
            int before=Game1.player.Items.Where(i=>i?.QualifiedItemId==orchardItem).Sum(i=>i.Stack);
            Utility.tryToPlaceItem(location,sapling,selected.X*64,selected.Y*64);
            if(location.terrainFeatures.GetValueOrDefault(selected.ToVector2()) is not FruitTree tree||tree.treeId.Value!=sapling.ItemId||before-Game1.player.Items.Where(i=>i?.QualifiedItemId==orchardItem).Sum(i=>i.Stack)!=1)throw new InvalidOperationException("native_orchard_plant_not_verified");
            Current.effects.Add(new{kind="native_fruit_tree_planted",location=destination,x=selected.X,y=selected.Y,item=orchardItem,days_until_mature=tree.daysUntilMature.Value});Current.completed++;orchardTile=null;return;
        }
        if(location.terrainFeatures.GetValueOrDefault(selected.ToVector2()) is not FruitTree harvest||harvest.fruit.Count==0){orchardVisited.Add(selected);orchardTile=null;return;}
        orchardHarvest=harvest.fruit.GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
        if(Game1.player.Items.Count(i=>i==null)<orchardHarvest.Count)throw new InvalidOperationException("fruit_inventory_space_required");
        orchardBefore=orchardHarvest.Keys.ToDictionary(id=>id,id=>Game1.player.Items.Where(i=>i?.QualifiedItemId==id).Sum(i=>i.Stack));
        harvest.performUseAction(selected.ToVector2());
        if(harvest.fruit.Count>0)throw new InvalidOperationException("native_fruit_shake_not_ready");orchardVisited.Add(selected);
        orchardPickupWalk.Clear();
        for(int y=selected.Y-1;y<=selected.Y+2;y++)for(int x=selected.X-2;x<=selected.X+2;x++){var at=new Point(x,y);if(Passable(location,at))orchardPickupWalk.Enqueue(at);}
        Current.phase="orchard_pickup";
    }
    private bool OrchardSiteLegal(GameLocation location,Point tile,StardewValley.Object sapling) {
        if(tile.X<1||tile.Y<1||tile.X>=location.Map.Layers[0].LayerWidth-1||tile.Y>=location.Map.Layers[0].LayerHeight-1)return false;
        if(location.objects.ContainsKey(tile.ToVector2())||location.terrainFeatures.ContainsKey(tile.ToVector2())||!sapling.canBePlacedHere(location,tile.ToVector2())||FruitTree.IsGrowthBlocked(tile.ToVector2(),location)||FruitTree.IsTooCloseToAnotherTree(tile.ToVector2(),location))return false;
        for(int y=tile.Y-1;y<=tile.Y+1;y++)for(int x=tile.X-1;x<=tile.X+1;x++)if(PlacementProtected?.Invoke(destination,new(x,y))==true||location.doesTileHaveProperty(x,y,"Action","Buildings")!=null||location.doesTileHaveProperty(x,y,"TouchAction","Back")!=null)return false;
        return true;
    }
    private void FindOrchardSite() {
        var location=Game1.currentLocation;var sapling=Game1.player.Items.OfType<StardewValley.Object>().FirstOrDefault(i=>i.QualifiedItemId==orchardItem)??throw new InvalidOperationException("orchard_sapling_supply_missing");
        var anchors=Exits(location).Select(e=>new FarmCell(e.X,e.Y)).ToList();
        foreach(var b in location.buildings)if(b.humanDoor.Value.X>=0)anchors.Add(new(b.tileX.Value+b.humanDoor.Value.X,b.tileY.Value+b.humanDoor.Value.Y+1));
        foreach(var p in location.objects.Pairs.Where(p=>p.Value.bigCraftable.Value))try{var point=Approach(p.Key.ToPoint(),true);anchors.Add(new(point.X,point.Y));}catch(InvalidOperationException){}
        var grid=new List<LayoutCell>();for(int y=0;y<location.Map.Layers[0].LayerHeight;y++)for(int x=0;x<location.Map.Layers[0].LayerWidth;x++)grid.Add(new(new(x,y),false,Passable(location,new(x,y)),false,false,false,false,0));
        var existing=location.terrainFeatures.Pairs.Where(p=>p.Value is FruitTree).Select(p=>p.Key.ToPoint()).ToArray();var start=new FarmCell(Game1.player.TilePoint.X,Game1.player.TilePoint.Y);
        foreach(var cell in grid.Where(c=>c.Passable&&c.Tile!=start).OrderBy(c=>existing.Length>0?existing.Min(t=>Math.Abs(t.X-c.Tile.X)+Math.Abs(t.Y-c.Tile.Y)):Math.Abs(c.Tile.X-start.X)+Math.Abs(c.Tile.Y-start.Y))) {
            var tile=new Point(cell.Tile.X,cell.Tile.Y);
            if(anchors.Contains(cell.Tile)||!OrchardSiteLegal(location,tile,sapling))continue;
            try {var stand=Approach(tile,true);if(!FarmLayout.KeepsAccess(grid,start,anchors,new[]{cell.Tile},new[]{new FarmCell(stand.X,stand.Y)}))continue;Walk(stand);orchardTile=tile;return;}catch(InvalidOperationException){}
        }
        throw new InvalidOperationException("no_orchard_site_with_clear_growth_ring_and_access");
    }
}
