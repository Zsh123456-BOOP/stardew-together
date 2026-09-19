using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;

public sealed partial class PlayerExecutor {
    internal sealed record LooseDrop(Debris Source,Vector2 Pixel,Item Item);
    internal Func<bool>? LabPickupBlock;
    private Debris? pickupTarget;
    private sealed record DeferredDrop(string Location,Point Tile,string Facts,string Reason,float Energy);
    private readonly Dictionary<Debris,DeferredDrop> deferredDrops=new();
    private string DropRouteFacts(GameLocation l,Point at) {
        var facts=new List<string>();
        for(int y=at.Y-5;y<=at.Y+5;y++)for(int x=at.X-5;x<=at.X+5;x++) {
            var v=new Vector2(x,y);if(l.objects.TryGetValue(v,out var o))facts.Add($"{x},{y}:{o.QualifiedItemId}:{o.MinutesUntilReady}");
            if(l.terrainFeatures.TryGetValue(v,out var f))facts.Add($"{x},{y}:{f.GetType().Name}:{(f as Tree)?.health.Value}");
        }
        facts.Add(string.Join(";",Game1.player.Items.Select(i=>i==null?"empty":$"{i.QualifiedItemId}:{i.Quality}:{i.Stack}")));
        facts.Add(string.Join(";",Game1.player.Items.OfType<Tool>().Select(t=>$"{t.QualifiedItemId}:{t.UpgradeLevel}")));
        return string.Join("|",facts);
    }
    internal bool PickupEligible(LooseDrop drop) {
        if(!deferredDrops.TryGetValue(drop.Source,out var blocked))return true;
        var at=(drop.Pixel/64).ToPoint();
        if(blocked.Location!=Game1.currentLocation.NameOrUniqueName||Math.Abs(at.X-blocked.Tile.X)+Math.Abs(at.Y-blocked.Tile.Y)>1||Game1.player.Stamina>blocked.Energy+4||blocked.Facts!=DropRouteFacts(Game1.currentLocation,blocked.Tile)) {deferredDrops.Remove(drop.Source);return true;}
        return false;
    }
    internal bool PickupTileEligible(Point tile)=>LooseDrops(Game1.currentLocation).Any(d=>Vector2.DistanceSquared(d.Pixel,tile.ToVector2()*64+new Vector2(32))<=320*320&&PickupEligible(d));
    private void DeferPickup(IEnumerable<LooseDrop> drops,string reason) {
        foreach(var g in drops.GroupBy(d=>d.Source)) {
            var first=g.First();var at=(first.Pixel/64).ToPoint();deferredDrops[g.Key]=new(Game1.currentLocation.NameOrUniqueName,at,DropRouteFacts(Game1.currentLocation,at),reason,Game1.player.Stamina);
        }
        Current!.effects.Add(new{kind="pickup_deferred",reason,remaining=drops.Count(),positions=drops.Select(d=>new{item=d.Item.QualifiedItemId,pixel=new[]{d.Pixel.X,d.Pixel.Y}}),resume="local obstacles, inventory capacity, energy recovery, available tools or drop position changes; other work may continue"});
        throw new InvalidOperationException("pickup_deferred_conditions_unchanged");
    }
    private DateTime pickupProgressAt;
    private int pickupLastCount=-1,pickupWalks;
    private bool pickupWalking;
    private int pickupRepositions;
    private readonly Dictionary<Debris,float> pickupBestDistance=new();
    private readonly HashSet<Point> pickupVisited=new();
    private List<Point> pickupTiles=new();
    private readonly HashSet<Debris> pickupTracked=new();
    private readonly HashSet<Debris> pickupBaseline=new();
    internal Point[] PendingDropTiles()=>LooseDrops(Game1.currentLocation).Where(d=>pickupTracked.Contains(d.Source)).Select(d=>(d.Pixel/64).ToPoint()).Distinct().ToArray();
    private void ObserveWorkDrops() {
        // Observe on every native work tick, including the tree's falling phase.
        // Once linked, a debris group remains tracked wherever it scatters.
        foreach(var drop in LooseDrops(Game1.currentLocation))
            if(!pickupBaseline.Contains(drop.Source)&&workTiles.Take(Math.Min(workTiles.Count,workIndex+1)).Any(p=>Vector2.DistanceSquared(drop.Pixel,p.ToVector2()*64+new Vector2(32))<=768*768))pickupTracked.Add(drop.Source);
    }
    internal static IEnumerable<LooseDrop> LooseDrops(GameLocation location) {
        foreach(var debris in location.debris) {
            if(debris.debrisType.Value is not (Debris.DebrisType.OBJECT or Debris.DebrisType.RESOURCE or Debris.DebrisType.ARCHAEOLOGY))continue;
            Item? item=debris.item;
            if(item==null&&!string.IsNullOrEmpty(debris.itemId.Value))item=ItemRegistry.Create(debris.itemId.Value,1,debris.itemQuality);
            if(item==null)continue;
            foreach(var chunk in debris.Chunks)yield return new(debris,chunk.position.Value+new Vector2(32),item);
        }
    }
    private void ResetPickup() {pickupTarget=null;pickupWalking=false;pickupLastCount=-1;pickupWalks=0;pickupRepositions=0;pickupBestDistance.Clear();pickupVisited.Clear();pickupTracked.Clear();pickupBaseline.Clear();if(Game1.currentLocation!=null)foreach(var d in Game1.currentLocation.debris)pickupBaseline.Add(d);pickupProgressAt=DateTime.UtcNow;}
    private void StartPickup(JsonElement args) {
        if(!args.TryGetProperty("tiles",out var tiles)||tiles.ValueKind!=JsonValueKind.Array||tiles.GetArrayLength() is <1 or >128)throw new InvalidOperationException("pickup_centers_required");
        pickupTiles=tiles.EnumerateArray().Select(Tile).Distinct().ToList();ResetPickup();Current!.phase="pickup_scan";
    }
    // Observe native debris and walk into the native magnetic radius. Never call
    // Debris.collect, add inventory items, increase magnetism or teleport loot.
    private bool TickNativePickup(IReadOnlyList<Point> centers) {
        if(Game1.currentLocation.NameOrUniqueName!=origin)throw new InvalidOperationException("pickup_location_changed");
        var observed=LooseDrops(Game1.currentLocation).ToArray();
        foreach(var drop in observed.Where(d=>centers.Any(p=>Vector2.DistanceSquared(d.Pixel,p.ToVector2()*64+new Vector2(32))<=320*320)))pickupTracked.Add(drop.Source);
        foreach(var gone in deferredDrops.Keys.Where(d=>deferredDrops[d].Location==Game1.currentLocation.NameOrUniqueName&&!Game1.currentLocation.debris.Contains(d)).ToArray())deferredDrops.Remove(gone);
        var tracked=observed.Where(d=>pickupTracked.Contains(d.Source)).ToArray();
        var drops=tracked.Where(PickupEligible).ToArray();
        if(tracked.Length>0&&drops.Length==0)DeferPickup(tracked,"unchanged_pickup_obstacle");
        if(drops.Length>0&&LabPickupBlock?.Invoke()==true){LabPickupBlock=null;StopWalk();Current!.effects.Add(new{kind="lab_injected_pickup_block",remaining=drops.Length,positions=drops.Select(d=>d.Pixel).ToArray(),note="fault injection, not a claim of measured path failure"});throw new InvalidOperationException("pickup_unreachable");}
        if(drops.Length==0) {
            StopWalk();Current!.effects.Add(new{kind="pickup_verified",remaining=0,walks=pickupWalks});return false;
        }
        if(drops.Length!=pickupLastCount){pickupLastCount=drops.Length;pickupProgressAt=DateTime.UtcNow;pickupBestDistance.Clear();pickupVisited.Clear();pickupRepositions=0;}
        // Native magnetism assigns a farmer using the centre of an entire Debris
        // group, not the nearest individual chunk of a felled tree.
        var available=drops.Where(d=>CapacityAdapter.CanReceive(Game1.player,d.Item)).GroupBy(d=>d.Source)
            .Select(g=>new LooseDrop(g.Key,g.Key.Chunks.Aggregate(Vector2.Zero,(sum,c)=>sum+c.position.Value+new Vector2(32))/g.Key.Chunks.Count,g.First().Item)).ToArray();
        if(available.Length==0)throw new InvalidOperationException("capacity_no_stackable_room");
        if(pickupWalking) {
            if(!drops.Any(d=>d.Source==pickupTarget)){StopWalk();pickupWalking=false;}
            else if(!AtWalkTarget){MonitorWalk();return true;}
            else {StopWalk();pickupWalking=false;pickupProgressAt=DateTime.UtcNow;}
        }
        var player=Game1.player.StandingPixel.ToVector2();int radius=Game1.player.GetAppliedMagneticRadius();
        foreach(var group in drops.GroupBy(d=>d.Source)) {
            float distance=group.Average(d=>Vector2.Distance(d.Pixel,player));
            if(!pickupBestDistance.TryGetValue(group.Key,out var best)||distance<best-8){pickupBestDistance[group.Key]=distance;pickupProgressAt=DateTime.UtcNow;}
        }
        // Native debris bounces, then accelerates towards the player. Wait only
        // while actual collectable items are in range, never after every hit.
        bool stalled=(DateTime.UtcNow-pickupProgressAt).TotalSeconds>1.5;
        if(!stalled&&available.Any(d=>Math.Abs(d.Pixel.X-player.X)<=radius&&Math.Abs(d.Pixel.Y-player.Y)<=radius)) {
            Current!.phase="pickup_native_attraction";
            return true;
        }
        if(stalled&&++pickupRepositions>6) {
            Current!.effects.Add(new{kind="pickup_stalled_evidence",player=new[]{player.X,player.Y},radius,remaining=drops.Length,groups=available.Select(d=>new{item=d.Item.QualifiedItemId,at=new[]{d.Pixel.X,d.Pixel.Y},native_owner=d.Source.player.Value?.UniqueMultiplayerID,chunks=d.Source.Chunks.Count})});
            DeferPickup(drops,"pickup_not_progressing_after_reposition");
        }
        // Use the Farmer's actual standing offset; tile centre is not necessarily
        // the native attraction point. A stalled group gets a closer reachable
        // approach, instead of waiting at the edge of the magnetic radius.
        var offset=player-Game1.player.TilePoint.ToVector2()*64;
        int approachRadius=stalled?Math.Min(radius,64):radius;
        foreach(var drop in available.OrderBy(d=>Vector2.DistanceSquared(d.Pixel,player))) {
            var at=(drop.Pixel/64).ToPoint();
            var stands=(from y in Enumerable.Range(at.Y-(int)Math.Ceiling(approachRadius/64d)-1,2*(int)Math.Ceiling(approachRadius/64d)+3) from x in Enumerable.Range(at.X-(int)Math.Ceiling(approachRadius/64d)-1,2*(int)Math.Ceiling(approachRadius/64d)+3) select new Point(x,y))
                .Where(p=>p!=Game1.player.TilePoint&&(!stalled||!pickupVisited.Contains(p))&&Passable(Game1.currentLocation,p)&&Math.Abs(p.X*64+offset.X-drop.Pixel.X)<=approachRadius&&Math.Abs(p.Y*64+offset.Y-drop.Pixel.Y)<=approachRadius)
                .OrderBy(p=>stalled?Vector2.DistanceSquared(p.ToVector2()*64+offset,drop.Pixel):Math.Abs(p.X-Game1.player.TilePoint.X)+Math.Abs(p.Y-Game1.player.TilePoint.Y));
            foreach(var stand in stands) {
                var path=MeasuredPath(stand);if(path==null||path.Count==0)continue;
                approachPath=path;approachLocation=Game1.currentLocation;approachStart=Game1.player.TilePoint;approachEnd=stand;
                pickupVisited.Add(Game1.player.TilePoint);pickupVisited.Add(stand);
                if(stalled)Current!.effects.Add(new{kind="pickup_reposition",item=drop.Item.QualifiedItemId,target=new[]{stand.X,stand.Y},remaining=drops.Length});
                Walk(stand);pickupTarget=drop.Source;pickupWalking=true;pickupWalks++;pickupBestDistance.Clear();pickupProgressAt=DateTime.UtcNow;Current!.phase="pickup_walk";return true;
            }
        }
        DeferPickup(drops,"pickup_unreachable_after_clearance_search");return true;
    }
}
