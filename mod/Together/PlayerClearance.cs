using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
namespace Together;
public sealed partial class PlayerExecutor {
    private sealed record ClearTarget(Point Tile,int Slot,float Energy,double Cost,Item[] Drops);
    private List<Point>? clearRoute;
    private Point clearDestination,clearTile;
    private string clearLocation="";
    private int clearSlotBefore,clearHits,clearCount;
    private bool clearSwing;
    private object? clearBefore;
    private DateTime clearStarted;
    private bool clearInternalWalk;
    private void ResetClearance(){clearRoute=null;clearSwing=false;clearCount=0;clearInternalWalk=false;}
    private ClearTarget? ClearanceTarget(Point tile) {
        var l=Game1.currentLocation;var v=tile.ToVector2();
        if(l is not Farm||tile.X<0||tile.Y<0||tile.X>=l.Map.Layers[0].LayerWidth||tile.Y>=l.Map.Layers[0].LayerHeight||WorkProtected?.Invoke(l,tile)==true||!l.isTilePassable(v))return null;
        if(l.terrainFeatures.ContainsKey(v)||ClumpAt(tile)!=null||l.buildings.Any(b=>b.occupiesTile(v))||l.characters.Any(c=>c.GetBoundingBox().Intersects(StandingBox(tile))))return null;
        if(!l.objects.TryGetValue(v,out var o)||o.bigCraftable.Value||o.questItem.Value)return null;
        int slot=-1;Item[] drops;
        if(o.IsTwig()){slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is Axe,-1);drops=new[]{ItemRegistry.Create("(O)388",2)};}
        else if(o.IsWeeds()&&o.ItemId is "674" or "675" or "676" or "677" or "678" or "679" or "784" or "792") {slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is Tool t&&t.isScythe(),-1);drops=new[]{ItemRegistry.Create("(O)771",2),ItemRegistry.Create("(O)770",1),ItemRegistry.Create("(H)40",1)};
            if(Game1.currentSeason=="summer")drops=drops.Append(ItemRegistry.Create("(O)MixedFlowerSeeds",1)).ToArray();
            if(l.HasUnlockedAreaSecretNotes(Game1.player))drops=drops.Append(ItemRegistry.Create("(O)79",1)).ToArray();}
        else if(o.BaseName=="Stone"&&o.ItemId is "343" or "450") {slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is Pickaxe,-1);drops=new[]{ItemRegistry.Create("(O)390",4),ItemRegistry.Create("(O)382",2),ItemRegistry.Create("(O)535",1)};}
        else return null;
        if(Game1.player.team.SpecialOrderRuleActive("DROP_QI_BEANS"))drops=drops.Append(ItemRegistry.Create("(O)890",1)).ToArray();
        if(o.BaseName=="Stone"&&Game1.stats.DaysPlayed>60)drops=drops.Append(ItemRegistry.Create("(O)536",1)).ToArray();
        if(o.BaseName=="Stone"&&Game1.stats.DaysPlayed>120)drops=drops.Append(ItemRegistry.Create("(O)537",1)).ToArray();
        if(slot<0)return null;var tool=(Tool)Game1.player.Items[slot];float energy=o.IsWeeds()?0:ResourceEnergy(o,tool);
        double hits=o.IsWeeds()?1:Math.Max(1,energy/Math.Max(.1f,SwingEnergy(tool)));
        return new(tile,slot,energy,hits*4+2,drops); // walk tile ~.25s, native swing+pickup allowance
    }
    private bool ClearanceCapacity(IEnumerable<ClearTarget> targets) {
        var drops=targets.SelectMany(t=>t.Drops).ToArray();var a=new CapacityAdapter(Game1.player,drops);
        return CapacityPlan.Simulate(a.Snapshot,drops.Select(d=>a.Put(d)).ToArray()).Feasible;
    }
    internal object ClearanceProbe(int offset=0) {
        if(Busy)throw new InvalidOperationException("player_busy");
        var at=Game1.player.TilePoint;var candidates=Game1.currentLocation.objects.Pairs.Where(p=>ClearanceTarget(p.Key.ToPoint())!=null).SelectMany(p=>new[]{new Point((int)p.Key.X+1,(int)p.Key.Y),new((int)p.Key.X-1,(int)p.Key.Y),new((int)p.Key.X,(int)p.Key.Y+1),new((int)p.Key.X,(int)p.Key.Y-1)}).Distinct().Where(p=>Passable(Game1.currentLocation,p)).OrderBy(p=>Vector2.DistanceSquared(p.ToVector2(),at.ToVector2())).Skip(offset).Take(12);
        foreach(var end in candidates) {
            var ordinary=PreviewPath(Game1.currentLocation,end);var path=ClearancePath(end,ordinary);var blocked=path?.Where(p=>!Passable(Game1.currentLocation,p)).ToArray();
            if(blocked?.Length>0)return new{found=true,end=new[]{end.X,end.Y},ordinary_steps=ordinary?.Count,path=path!.Select(p=>new[]{p.X,p.Y}),clears=blocked.Select(p=>new{tile=new[]{p.X,p.Y},before=TileState(p),slot=ClearanceTarget(p)!.Slot}),body=Snapshot()};
        }
        return new{found=false,offset,next_offset=offset+12,body=Snapshot()};
    }
    private Stack<Point>? ClearancePath(Point end,Stack<Point>? ordinary) {
        if(clearInternalWalk||Game1.currentLocation is not Farm||ordinary?.Count<=12)return ordinary;
        var start=Game1.player.TilePoint;var pass=new Dictionary<Point,bool>();var targets=new Dictionary<Point,ClearTarget?>();var fits=new Dictionary<string,bool>();
        bool Bounds(Point p)=>p.X>=Math.Min(start.X,end.X)-4&&p.X<=Math.Max(start.X,end.X)+4&&p.Y>=Math.Min(start.Y,end.Y)-4&&p.Y<=Math.Max(start.Y,end.Y)+4;
        ClearTarget? Get(Point p){if(!targets.TryGetValue(p,out var t))targets[p]=t=Bounds(p)?ClearanceTarget(p):null;return t;}
        var plan=ClearanceRouting.Find(new(start.X,start.Y),new(end.X,end.Y),p=>{var t=new Point(p.X,p.Y);if(!Bounds(t))return false;if(!pass.TryGetValue(t,out var b))pass[t]=b=Passable(Game1.currentLocation,t);return b;},
            p=>Get(new(p.X,p.Y)) is {} t?new(t.Cost,t.Energy,t.Drops.Any(i=>i.QualifiedItemId is "(O)388" or "(O)390")):null,
            list=>{string key=string.Join(";",list.OrderBy(p=>p.X).ThenBy(p=>p.Y));if(!fits.TryGetValue(key,out var ok))fits[key]=ok=ClearanceCapacity(list.Select(p=>Get(new(p.X,p.Y))!));return ok;},
            Math.Max(0,Game1.player.Stamina-workReserve),maxClear:3-clearCount,maxCost:ordinary?.Count??double.PositiveInfinity);
        if(plan==null||plan.Clear.Count==0)return ordinary??(plan==null?null:new Stack<Point>(plan.Path.AsEnumerable().Reverse().Select(p=>new Point(p.X,p.Y))));
        if(Busy)Current!.effects.Add(new{kind="clearance_route_selected",from=new[]{start.X,start.Y},to=new[]{end.X,end.Y},ordinary_steps=ordinary?.Count,weighted_cost=plan.Cost,energy=plan.Energy,clears=plan.Clear.Select(p=>new[]{p.X,p.Y}),max_obstacles=3,note="native clearing before walking; capacity includes possible drops"});
        return new Stack<Point>(plan.Path.AsEnumerable().Reverse().Select(p=>new Point(p.X,p.Y)));
    }
    private bool BeginClearance(Stack<Point>? path,Point end) {
        if(clearInternalWalk||path==null||!path.Any(p=>!Passable(Game1.currentLocation,p)))return false;
        var points=path.ToList();var blocked=points.First(p=>!Passable(Game1.currentLocation,p));
        if(ClearanceTarget(blocked)==null)throw new InvalidOperationException("route_obstacle_conditions_changed");
        clearRoute=points;clearDestination=end;clearLocation=Game1.currentLocation.NameOrUniqueName;clearSlotBefore=Game1.player.CurrentToolIndex;clearStarted=DateTime.UtcNow;clearHits=0;clearSwing=false;
        AdvanceClearance();return true;
    }
    private void PlainWalk(Point end,List<Point> path) {
        clearInternalWalk=true;
        try {approachPath=new Stack<Point>(path.AsEnumerable().Reverse());approachStart=Game1.player.TilePoint;approachEnd=end;approachLocation=Game1.currentLocation;Walk(end);}
        finally{clearInternalWalk=false;}
    }
    private void AdvanceClearance() {
        if(clearRoute==null)return;
        int blocked=clearRoute.FindIndex(p=>!Passable(Game1.currentLocation,p));
        if(blocked<0) {
            var path=clearRoute;var end=clearDestination;clearRoute=null;
            PlayerSelection.Set(Game1.player,Math.Clamp(clearSlotBefore,0,Game1.player.Items.Count-1));PlainWalk(end,path);return;
        }
        if(blocked==0||ClearanceTarget(clearRoute[blocked])==null)throw new InvalidOperationException("route_obstacle_conditions_changed");
        clearTile=clearRoute[blocked];clearHits=0;var prefix=clearRoute.Take(blocked).ToList();PlainWalk(prefix[^1],prefix);
    }
    private IEnumerable<ClearTarget> ClearanceSweepDrops(ClearTarget obstacle) {
        yield return obstacle;
        if(Game1.player.Items[obstacle.Slot] is not MeleeWeapon scythe||!scythe.isScythe())yield break;
        var l=Game1.currentLocation;var stand=Game1.player.TilePoint;var box=Game1.player.GetBoundingBox();var target=obstacle.Tile;
        int facing=target.X>stand.X?1:target.X<stand.X?3:target.Y>stand.Y?2:0;
        int x=facing==1?box.Right+48:facing==3?box.Left-48:box.Center.X,y=facing==0?box.Top-48:facing==2?box.Bottom+48:box.Center.Y;
        var seen=new HashSet<Point>{target};
        for(int frame=0;frame<6;frame++)foreach(var v in Utility.getListOfTileLocationsForBordersOfNonTileRectangle(NativeScytheGeometry.Area(x,y,facing,box,frame,scythe.addedAreaOfEffect.Value,scythe.type.Value))) {
            if(!seen.Add(v.ToPoint())||!l.objects.TryGetValue(v,out var item)||!item.IsWeeds())continue;
            yield return ClearanceTarget(v.ToPoint())??throw new InvalidOperationException("clearance_unknown_collateral_drop");
        }
    }
    private bool TickClearance() {
        if(clearRoute==null)return false;
        // Never swallow map/event/menu changes while a native tool action owns the farmer.
        if(Game1.currentLocation.NameOrUniqueName!=clearLocation||Game1.eventUp||Game1.activeClickableMenu!=null)throw new InvalidOperationException("clearance_interrupted_reobserve");
        if((DateTime.UtcNow-clearStarted).TotalSeconds>60)throw new InvalidOperationException("clearance_timeout");
        if(Game1.player.UsingTool||!Game1.player.CanMove)return true;
        if(clearSwing) {
            clearSwing=false;var after=TileState(clearTile);
            if(AgentJson.Encode(clearBefore)==AgentJson.Encode(after))throw new InvalidOperationException("clearance_native_no_effect");
            Current!.effects.Add(new{kind="native_route_clear",tile=new[]{clearTile.X,clearTile.Y},before=clearBefore,after,inventory=AgentToolRegistry.Inventory()});
            if(cancelAfterImpact)return true;
            if(!Game1.currentLocation.objects.ContainsKey(clearTile.ToVector2())) {
                clearCount++;if(!Passable(Game1.currentLocation,clearTile))throw new InvalidOperationException("clearance_residual_collision");
                int i=clearRoute.IndexOf(Game1.player.TilePoint);if(i>=0)clearRoute=clearRoute.Skip(i).ToList();AdvanceClearance();return true;
            }
        }
        if(!AtWalkTarget){MonitorWalk();return true;}
        StopWalk();Adjacent(clearTile);
        var obstacle=ClearanceTarget(clearTile)??throw new InvalidOperationException("route_obstacle_conditions_changed");
        // Scythe collateral is checked with the same native sweep geometry as work.run.
        int savedSlot=workSlot;workSlot=obstacle.Slot;bool safe;
        try{safe=SafeSweep(Game1.currentLocation,clearTile,Game1.player.TilePoint,Game1.player.GetBoundingBox());}finally{workSlot=savedSlot;}
        bool fits;try{fits=ClearanceCapacity(ClearanceSweepDrops(obstacle));}catch(InvalidOperationException){fits=false;}
        if((!safe||!fits)&&Game1.player.Items[obstacle.Slot] is Tool scythe&&scythe.isScythe()) {
            int axe=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is Axe,-1);
            if(axe>=0&&ClearanceCapacity(new[]{obstacle})) {
                obstacle=obstacle with{Slot=axe,Energy=SwingEnergy((Tool)Game1.player.Items[axe])};
                Current!.effects.Add(new{kind="clearance_single_target_fallback",tile=new[]{clearTile.X,clearTile.Y},reason=safe?"scythe_collateral_capacity":"protected_scythe_sweep",energy=obstacle.Energy,reserve=workReserve});safe=fits=true;
            }
        }
        if(!safe)throw new InvalidOperationException("clearance_protected_sweep");
        if(!fits)throw new InvalidOperationException("capacity_no_stackable_room");
        if(Game1.player.Stamina<obstacle.Energy+workReserve)throw new InvalidOperationException("energy_reserve_reached");
        if(++clearHits>16||clearCount>=3)throw new InvalidOperationException("clearance_limit_replan");
        Face(clearTile);PlayerSelection.Set(Game1.player,obstacle.Slot);clearBefore=TileState(clearTile);
        Game1.player.lastClick=clearTile.ToVector2()*64+new Vector2(32);Game1.player.BeginUsingTool();
        if(!Game1.player.UsingTool)throw new InvalidOperationException("clearance_native_tool_not_started");clearSwing=true;return true;
    }
}
