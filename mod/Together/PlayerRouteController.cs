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
    protected override void moveCharacter(GameTime time) {
        if(pathToEndPoint.Count>0&&pathToEndPoint.Peek()!=Game1.player.TilePoint&&!PlayerExecutor.Passable(Game1.currentLocation,pathToEndPoint.Peek())){Blocked=true;BlockedTile=pathToEndPoint.Peek();Game1.player.Halt();return;}
        int count=pathToEndPoint.Count;var before=Game1.player.Position;var map=Game1.currentLocation;
        base.moveCharacter(time);
        if(pathToEndPoint.Count>0&&pathToEndPoint.Count<count&&Game1.player.Position==before&&Game1.currentLocation==map&&!Game1.fadeToBlack&&Game1.locationRequest==null&&Game1.player.CanMove&&!Game1.player.UsingTool)
            base.moveCharacter(time);
    }
}
