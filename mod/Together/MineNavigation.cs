using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;

namespace Together;
public sealed partial class PlayerExecutor {
    private int mineBefore;
    private void StartMineDescent() {
        if(Game1.currentLocation is not MineShaft mine)throw new InvalidOperationException("must_be_in_native_mine_floor");
        mineBefore=mine.mineLevel;var exits=new List<Point>();
        for(int y=0;y<mine.Map.Layers[0].LayerHeight;y++)for(int x=0;x<mine.Map.Layers[0].LayerWidth;x++)
            if(mine.getTileIndexAt(new xTile.Dimensions.Location(x,y),"Buildings","mine")==173)exits.Add(new(x,y));
        foreach(var at in exits.OrderBy(e=>Vector2.DistanceSquared(e.ToVector2(),Game1.player.Tile))) {
            try{target=at;var stand=Approach(at,true);Walk(stand);mineLadder=at;Current!.phase="mine_ladder_walk";return;}
            catch(InvalidOperationException){StopWalk();}
        }
        throw new InvalidOperationException("no_reachable_revealed_ladder_mine_or_fight_first");
    }
    private Point mineLadder;
    private void TickMineDescent() {
        if(Current!.phase=="mine_transition") {
            if(Game1.fadeToBlack||Game1.locationRequest!=null)return;
            if(Game1.currentLocation is MineShaft next&&next.mineLevel>mineBefore) {
                Current.effects.Add(new{kind="native_ladder_transition",from=mineBefore,to=next.mineLevel,deepest=Game1.player.deepestMineLevel});Finish("succeeded");return;
            }
            if(DateTime.UtcNow>nextInteraction)throw new InvalidOperationException("native_mine_transition_not_observed");return;
        }
        if(Game1.currentLocation is not MineShaft mine||mine.mineLevel!=mineBefore)throw new InvalidOperationException("mine_floor_changed");
        if(Game1.player.TilePoint!=target){MonitorWalk();return;}StopWalk();Adjacent(mineLadder);
        if(mine.getTileIndexAt(new xTile.Dimensions.Location(mineLadder.X,mineLadder.Y),"Buildings","mine")!=173)throw new InvalidOperationException("observed_ladder_changed");
        Face(mineLadder);
        if(!Game1.tryToCheckAt(mineLadder.ToVector2(),Game1.player))throw new InvalidOperationException("native_ladder_interaction_rejected");
        Current.phase="mine_transition";nextInteraction=DateTime.UtcNow.AddSeconds(15);
    }
}
