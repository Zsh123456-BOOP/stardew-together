using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace Together;

// Native NPC pathing spends a whole update stopping at every intermediate tile.
// Consume that zero-movement waypoint in the same frame, then let the native
// controller perform ONE movement update. Speed, collisions and warps stay native.
internal sealed class PlayerRouteController : PathFindController {
    public PlayerRouteController(Stack<Point>? path,GameLocation location,Farmer player,Point end):base(path,location,player,end){finalFacingDirection=-1;
        // BFS includes its origin. Re-centering there can push into a nearby NPC
        // even when the first real edge leads safely away from that NPC.
        if(pathToEndPoint?.Count>1&&pathToEndPoint.Peek()==player.TilePoint)pathToEndPoint.Pop();
    }
    internal Action<object>? RecoveryObserved;
    public override bool update(GameTime time) {
        // Native update (not only its constructor) teleports to endPoint when
        // the map has no farmers. A controller retained through a warp must end.
        if(location!=Game1.currentLocation||Game1.player.currentLocation!=location){RecoveryObserved?.Invoke(new{kind="stale_route_location_released",route_location=location.NameOrUniqueName,current=Game1.currentLocation.NameOrUniqueName});return true;}
        if(!isPlayerPresent())return false;
        return base.update(time);
    }
    private Queue<Point>? localRoute;
    private DateTime nextLocalSearch;
    private Point? localSearchTarget;
    private static bool Arrived(Rectangle box,Point tile) {
        var cell=new Rectangle(tile.X*64+2,tile.Y*64,60,64);
        return (cell.Contains(box)||box.Width>cell.Width&&cell.Contains(box.Center))&&cell.Bottom-box.Bottom>=2;
    }
    private static bool Clear(Rectangle box) {
        var p=Game1.player;var l=Game1.currentLocation;
        if(l.characters.Any(n=>!n.IsInvisible&&!n.farmerPassesThrough&&box.Intersects(n.GetBoundingBox())))return false;
        return !l.isCollidingPosition(box,Game1.viewport,true,0,false,p,false,false,false,true);
    }
    internal static Point? EscapeTile() {
        var origin=Game1.player.GetBoundingBox();var start=Game1.player.TilePoint;Point? reached=null;
        Rectangle At(FarmCell v)=>new(origin.X+v.X,origin.Y+v.Y,origin.Width,origin.Height);
        var route=LocalWalkRouting.Find(v=>{
            var box=At(v);var tile=new Point(box.Center.X/64,box.Center.Y/64);
            if(tile==start||!Arrived(box,tile)||!PlayerExecutor.Passable(Game1.currentLocation,tile))return false;
            reached=tile;return true;
        },(a,b)=>{
            for(int d=2;d<=8;d+=2)if(!Clear(At(new(a.X+(b.X-a.X)*d/8,a.Y+(b.Y-a.Y)*d/8))))return false;
            return true;
        });
        return route==null?null:reached;
    }
    private bool LocalStep(GameTime time) {
        if(localRoute==null||localRoute.Count==0){localRoute=null;return false;}
        var p=Game1.player;var box=p.GetBoundingBox();int step=(int)Math.Ceiling(p.getMovementSpeed());
        while(localRoute.Count>0&&Math.Abs(box.X-localRoute.Peek().X)+Math.Abs(box.Y-localRoute.Peek().Y)<step)localRoute.Dequeue();
        if(localRoute.Count==0){localRoute=null;return false;}
        var to=localRoute.Peek();int direction=box.X<to.X?1:box.X>to.X?3:box.Y<to.Y?2:0;
        var next=box;next.Offset(direction==1?step:direction==3?-step:0,direction==2?step:direction==0?-step:0);
        if(!Clear(next)){localRoute=null;return false;}
        Blocked=false;DynamicBlocker=null;p.movementDirections.Clear();
        switch(direction){case 0:p.SetMovingUp(true);break;case 1:p.SetMovingRight(true);break;case 2:p.SetMovingDown(true);break;case 3:p.SetMovingLeft(true);break;}
        p.MovePosition(time,Game1.viewport,Game1.currentLocation);return true;
    }
    private bool TryLocalStep(GameTime time,Point tile) {
        if(localSearchTarget==tile&&DateTime.UtcNow<nextLocalSearch)return false;localSearchTarget=tile;nextLocalSearch=DateTime.UtcNow.AddSeconds(1);
        var origin=Game1.player.GetBoundingBox();var clock=System.Diagnostics.Stopwatch.StartNew();int probes=0;
        Rectangle At(FarmCell v)=>new(origin.X+v.X,origin.Y+v.Y,origin.Width,origin.Height);
        var path=LocalWalkRouting.Find(v=>Arrived(At(v),tile),(a,b)=>{
            // Check intermediate positions too: no jumping across a thin obstacle.
            for(int d=2;d<=8;d+=2){var at=new FarmCell(a.X+(b.X-a.X)*d/8,a.Y+(b.Y-a.Y)*d/8);probes++;if(!Clear(At(at)))return false;}return true;
        });
        RecoveryObserved?.Invoke(new{kind="local_collision_route",target=new[]{tile.X,tile.Y},origin=origin.ToString(),points=path?.Count??0,probes,elapsed_ms=clock.Elapsed.TotalMilliseconds});
        if(path==null)return false;localRoute=new Queue<Point>(path.Select(v=>new Point(origin.X+v.X,origin.Y+v.Y)));return LocalStep(time);
    }
    internal bool Blocked {get;private set;}
    internal Point BlockedTile {get;private set;}
    internal string? DynamicBlocker {get;private set;}
    private DateTime blockedSince;
    internal double BlockedSeconds=>Blocked?(DateTime.UtcNow-blockedSince).TotalSeconds:0;
    private void Block(Point tile,string? npc=null){if(!Blocked||DynamicBlocker!=npc)blockedSince=DateTime.UtcNow;Blocked=true;BlockedTile=tile;DynamicBlocker=npc;Game1.player.Halt();}
    protected override void moveCharacter(GameTime time) {
        if(LocalStep(time))return;
        if(pathToEndPoint.Count>0) {
            var p=Game1.player;var tile=pathToEndPoint.Peek();var box=p.GetBoundingBox();var cell=new Rectangle(tile.X*64+2,tile.Y*64,60,64);var next=box;int step=(int)Math.Ceiling(p.getMovementSpeed());
            bool arrived=(cell.Contains(box)||box.Width>cell.Width&&cell.Contains(box.Center))&&cell.Bottom-box.Bottom>=2;
            if(!arrived) {
                if(box.Left<cell.Left&&box.Right<cell.Right)next.X+=step;else if(box.Right>cell.Right&&box.Left>cell.Left)next.X-=step;else if(box.Top<=cell.Top)next.Y+=step;else if(box.Bottom>=cell.Bottom-2)next.Y-=step;
                var npc=Game1.currentLocation.characters.FirstOrDefault(n=>!n.IsInvisible&&!n.farmerPassesThrough&&!box.Intersects(n.GetBoundingBox())&&next.Intersects(n.GetBoundingBox()));
                if(npc!=null&&!p.temporarilyInvincible&&p.TemporaryPassableTiles.IsEmpty()){
                    if(TryLocalStep(time,tile))return;Block(tile,npc.Name);return;
                }
            }
            if(tile!=p.TilePoint&&!PlayerExecutor.Passable(Game1.currentLocation,tile)){
                // A blocked tile-centre box does not prove every offset corridor
                // is blocked. Use the same swept-body route as corner recovery.
                if(TryLocalStep(time,tile))return;
                Block(tile);return;
            }
        }
        Blocked=false;DynamicBlocker=null;
        int count=pathToEndPoint.Count;var before=Game1.player.Position;var map=Game1.currentLocation;
        base.moveCharacter(time);
        // Tile-centre occupancy is insufficient beside a building/bin corner.
        // Walk the same swept pixel corridor used by the read-only escape proof.
        if(pathToEndPoint.Count>0&&Game1.player.Position==before&&Game1.currentLocation==map&&Game1.player.CanMove&&!Game1.fadeToBlack&&!Game1.player.UsingTool)
            if(TryLocalStep(time,pathToEndPoint.Peek()))return;
        if(pathToEndPoint.Count>0&&pathToEndPoint.Count<count&&Game1.player.Position==before&&Game1.currentLocation==map&&!Game1.fadeToBlack&&Game1.locationRequest==null&&Game1.player.CanMove&&!Game1.player.UsingTool)
            base.moveCharacter(time);
    }
}
