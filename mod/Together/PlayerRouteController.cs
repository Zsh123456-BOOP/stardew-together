using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;

namespace Together;

// Native NPC pathing spends a whole update stopping at every intermediate tile.
// Consume that zero-movement waypoint in the same frame, then let the native
// controller perform ONE movement update. Speed, collisions and warps stay native.
internal sealed class PlayerRouteController : PathFindController {
    public PlayerRouteController(Stack<Point>? path,GameLocation location,Farmer player,Point end):base(path,location,player,end){finalFacingDirection=-1;}
    internal bool Blocked {get;private set;}
    internal Point BlockedTile {get;private set;}
    internal string? DynamicBlocker {get;private set;}
    private DateTime blockedSince;
    internal double BlockedSeconds=>Blocked?(DateTime.UtcNow-blockedSince).TotalSeconds:0;
    private void Block(Point tile,string? npc=null){if(!Blocked||DynamicBlocker!=npc)blockedSince=DateTime.UtcNow;Blocked=true;BlockedTile=tile;DynamicBlocker=npc;Game1.player.Halt();}
    protected override void moveCharacter(GameTime time) {
        if(pathToEndPoint.Count>0) {
            var p=Game1.player;var tile=pathToEndPoint.Peek();var box=p.GetBoundingBox();var cell=new Rectangle(tile.X*64+2,tile.Y*64,60,64);var next=box;int step=(int)Math.Ceiling(p.getMovementSpeed());
            bool arrived=(cell.Contains(box)||box.Width>cell.Width&&cell.Contains(box.Center))&&cell.Bottom-box.Bottom>=2;
            if(!arrived) {
                if(box.Left<cell.Left&&box.Right<cell.Right)next.X+=step;else if(box.Right>cell.Right&&box.Left>cell.Left)next.X-=step;else if(box.Top<=cell.Top)next.Y+=step;else if(box.Bottom>=cell.Bottom-2)next.Y-=step;
                var npc=Game1.currentLocation.characters.FirstOrDefault(n=>!n.IsInvisible&&!n.farmerPassesThrough&&!box.Intersects(n.GetBoundingBox())&&next.Intersects(n.GetBoundingBox()));
                if(npc!=null&&!p.temporarilyInvincible&&p.TemporaryPassableTiles.IsEmpty()){Block(tile,npc.Name);return;}
            }
            if(tile!=p.TilePoint&&!PlayerExecutor.Passable(Game1.currentLocation,tile)){Block(tile);return;}
        }
        Blocked=false;DynamicBlocker=null;
        int count=pathToEndPoint.Count;var before=Game1.player.Position;var map=Game1.currentLocation;
        base.moveCharacter(time);
        if(pathToEndPoint.Count>0&&pathToEndPoint.Count<count&&Game1.player.Position==before&&Game1.currentLocation==map&&!Game1.fadeToBlack&&Game1.locationRequest==null&&Game1.player.CanMove&&!Game1.player.UsingTool)
            base.moveCharacter(time);
    }
}
