using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private string mineAccessMode="";
    private int mineAccessLevel;
    private Point? mineAccessTile;
    private void StartMineAccess(JsonElement args) {
        mineAccessMode=AgentToolRegistry.Text(args,"mode","enter");mineAccessLevel=AgentToolRegistry.Number(args,"level",1);mineAccessTile=null;
        if(mineAccessMode is not ("enter" or "leave" or "elevator")||mineAccessLevel is <0 or >120)throw new InvalidOperationException("invalid_mine_access");
        if(mineAccessMode=="elevator"&&(mineAccessLevel%5!=0||mineAccessLevel>MineShaft.lowestLevelReached))throw new InvalidOperationException("mine_elevator_stop_not_unlocked");
        if(mineAccessMode=="enter"&&mineAccessLevel!=1)throw new InvalidOperationException("mine_entrance_starts_on_floor_one_use_elevator_for_unlocked_stops");
        destination="Mine";Current!.phase="mine_access_route";
    }
    private void TickMineAccess() {
        if(Game1.locationRequest!=null||Game1.fadeToBlack)return;
        if(Current!.phase=="mine_access_transition") {
            bool arrived=mineAccessMode=="leave"||mineAccessLevel==0?Game1.currentLocation.NameOrUniqueName=="Mine":Game1.currentLocation is MineShaft m&&m.mineLevel==mineAccessLevel;
            if(arrived){Current.effects.Add(new{kind="native_mine_access",mode=mineAccessMode,level=Game1.CurrentMineLevel,location=Game1.currentLocation.NameOrUniqueName,deepest=Game1.player.deepestMineLevel});Finish("succeeded");return;}
            if(DateTime.UtcNow>nextInteraction)throw new InvalidOperationException("mine_access_transition_not_verified");return;
        }
        if(Game1.activeClickableMenu is MineElevatorMenu elevator&&mineAccessMode=="elevator") {
            var choice=elevator.elevators.FirstOrDefault(e=>e.name==mineAccessLevel.ToString())??throw new InvalidOperationException("native_elevator_choice_missing");
            if(Game1.CurrentMineLevel==mineAccessLevel){elevator.exitThisMenu();Finish("succeeded");return;}
            elevator.receiveLeftClick(choice.bounds.Center.X,choice.bounds.Center.Y);Current.phase="mine_access_transition";nextInteraction=DateTime.UtcNow.AddSeconds(20);return;
        }
        if(Game1.activeClickableMenu is DialogueBox dialogue&&mineAccessMode=="leave") {
            if(!dialogue.isQuestion)throw new InvalidOperationException("unexpected_mine_exit_dialogue");
            int index=Array.FindIndex(dialogue.responses,r=>r.responseKey=="Leave");if(index<0)throw new InvalidOperationException("native_leave_mine_option_missing");
            dialogue.finishTyping();if(dialogue.responseCC==null||dialogue.responseCC.Count<=index)return;
            var b=dialogue.responseCC[index].bounds;dialogue.performHoverAction(b.Center.X,b.Center.Y);dialogue.receiveLeftClick(b.Center.X,b.Center.Y);Current.phase="mine_access_transition";nextInteraction=DateTime.UtcNow.AddSeconds(20);return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("mine_access_menu_requires_review");
        if(!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(mineAccessMode=="leave"&&Game1.currentLocation is not MineShaft){Finish("succeeded");return;}
        if(Game1.currentLocation is not MineShaft&&Game1.currentLocation.NameOrUniqueName!="Mine"){Travel();return;}
        var l=Game1.currentLocation;
        if(mineAccessTile==null) {
            var candidates=new List<Point>();
            for(int y=0;y<l.Map.Layers[0].LayerHeight;y++)for(int x=0;x<l.Map.Layers[0].LayerWidth;x++) {
                var action=l.GetTilePropertySplitBySpaces("Action","Buildings",x,y);int tile=l.getTileIndexAt(x,y,"Buildings");
                bool match=mineAccessMode=="leave"?tile==115||action.FirstOrDefault()=="ExitMine":mineAccessMode=="elevator"?l is MineShaft?tile==112:action.FirstOrDefault()=="MineElevator":action.FirstOrDefault() is "Mine" or "NextMineLevel";
                if(match)candidates.Add(new(x,y));
            }
            foreach(var at in candidates.OrderBy(t=>Vector2.DistanceSquared(t.ToVector2(),Game1.player.Tile)))try{Walk(Approach(at,true));mineAccessTile=at;break;}catch(InvalidOperationException){}
            if(mineAccessTile==null)throw new InvalidOperationException("native_mine_access_unreachable");Current.phase="mine_access_walk";
        }
        if(Game1.player.TilePoint!=target){MonitorWalk();return;}StopWalk();Face(mineAccessTile.Value);Adjacent(mineAccessTile.Value);
        if(!Game1.tryToCheckAt(mineAccessTile.Value.ToVector2(),Game1.player))throw new InvalidOperationException("native_mine_access_rejected");
        if(mineAccessMode=="enter"){Current.phase="mine_access_transition";nextInteraction=DateTime.UtcNow.AddSeconds(20);}
    }
}
