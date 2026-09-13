using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Tools;

namespace Together;
public sealed partial class PlayerExecutor {
    private string volcanoMode="",volcanoOrigin="",volcanoAction="";
    private Point volcanoObstacle;
    private int volcanoBefore,volcanoWaterBefore;
    internal static object ReadVolcano()=>Game1.currentLocation is VolcanoDungeon volcano?new{active=true,level=volcano.level.Value,start=volcano.startPosition,end=volcano.endPosition,cooled=volcano.cooledLavaTiles.Keys.Select(v=>new{x=(int)v.X,y=(int)v.Y}),gates=volcano.dwarfGates.Select(g=>new{opened=g.opened.Value,switches=g.switches.Pairs.Select(s=>new{x=s.Key.X,y=s.Key.Y,pressed=s.Value})}),exits=Exits(volcano).Select(w=>new{x=w.X,y=w.Y,to=w.TargetName}),note="只读取当前已生成层；不提前生成后续地图"}:new{active=false,location=Game1.currentLocation.NameOrUniqueName};
    private void StartVolcanoStep(JsonElement args) {
        volcanoMode=AgentToolRegistry.Text(args,"mode","advance");if(volcanoMode is not ("advance" or "retreat" or "enter"))throw new InvalidOperationException("invalid_volcano_mode");
        volcanoOrigin=Game1.currentLocation.NameOrUniqueName;volcanoBefore=(Game1.currentLocation as VolcanoDungeon)?.level.Value??-1;volcanoAction="";
        Current!.phase="volcano_plan";
        if(Game1.currentLocation is not VolcanoDungeon) {
            if(volcanoMode!="enter")throw new InvalidOperationException("must_be_in_native_volcano");
            destination="IslandNorth";Current.phase="volcano_enter_route";
        }
    }
    private void TickVolcanoStep() {
        if(Game1.fadeToBlack||Game1.locationRequest!=null)return;
        if(volcanoMode=="enter"&&Game1.currentLocation is VolcanoDungeon||volcanoMode!="enter"&&Game1.currentLocation.NameOrUniqueName!=volcanoOrigin) {
            int after=(Game1.currentLocation as VolcanoDungeon)?.level.Value??(Game1.currentLocation is Caldera?10:-1);
            bool expected=volcanoMode=="enter"?after==0:volcanoMode=="retreat"?after==volcanoBefore-1:after==volcanoBefore+1;
            Current!.effects.Add(new{kind="native_volcano_transition",mode=volcanoMode,from=volcanoBefore,to=after,location=Game1.currentLocation.NameOrUniqueName});Current.completed=expected?1:0;Finish(expected?"succeeded":"failed",expected?null:"volcano_unexpected_transition");return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("volcano_route_menu_interrupted");
        if(!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(Current!.phase=="volcano_enter_route") {
            if(Game1.currentLocation.NameOrUniqueName!="IslandNorth"){Travel();return;}
            var entrance=Exits(Game1.currentLocation).Where(w=>w.TargetName==VolcanoDungeon.GetLevelName(0)).OrderBy(w=>Vector2.DistanceSquared(new Vector2(w.X,w.Y),Game1.player.Tile)).FirstOrDefault();
            if(entrance==null)throw new InvalidOperationException("observed_volcano_entrance_missing");
            Walk(new(entrance.X,entrance.Y));volcanoAction="enter";Current.phase="volcano_walking";return;
        }
        if(Current.phase=="volcano_walking") {
            if(Game1.player.TilePoint!=target){MonitorWalk();return;}StopWalk();
            if(volcanoAction is "exit" or "enter") {
                if(DateTime.UtcNow-lastProgress>TimeSpan.FromSeconds(4))throw new InvalidOperationException("native_volcano_warp_not_triggered");return;
            }
            Current.effects.Add(new{kind="native_volcano_walk",level=volcanoBefore,tile=target,reason=volcanoAction});Current.completed=1;Finish("succeeded");return;
        }
        if(Game1.currentLocation is not VolcanoDungeon location)throw new InvalidOperationException("volcano_floor_changed");
        if(Current.phase=="volcano_cooling") {
            if(!location.IsCooledLava(volcanoObstacle.X,volcanoObstacle.Y))throw new InvalidOperationException("native_lava_cooling_not_verified");
            Current.effects.Add(new{kind="native_lava_cooled",level=location.level.Value,tile=volcanoObstacle,water_before=volcanoWaterBefore,water_after=(Game1.player.CurrentTool as WateringCan)?.WaterLeft});Current.completed=1;Finish("succeeded");return;
        }
        if(volcanoAction=="clear"){TickWork();return;}
        PlanVolcanoStep(location);
    }
    private void PlanVolcanoStep(VolcanoDungeon location) {
        string next=volcanoMode=="retreat"?location.level.Value==0?"IslandNorth":VolcanoDungeon.GetLevelName(location.level.Value-1):location.level.Value==9?"Caldera":VolcanoDungeon.GetLevelName(location.level.Value+1);
        var exits=Exits(location).Where(w=>w.TargetName==next).Select(w=>new Point(w.X,w.Y)).ToArray();
        var grid=new Dictionary<Point,int>();
        int can=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is WateringCan,-1),pick=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is Pickaxe,-1);
        for(int y=0;y<location.Map.Layers[0].LayerHeight;y++)for(int x=0;x<location.Map.Layers[0].LayerWidth;x++) {
            var p=new Point(x,y);if(Passable(location,p)){grid[p]=1;continue;}
            if(IsVolcanoLava(location,p)&&can>=0)grid[p]=9;
            else if(pick>=0&&location.objects.TryGetValue(p.ToVector2(),out var rock)&&rock.IsBreakableStone())grid[p]=12+Math.Max(0,rock.MinutesUntilReady);
        }
        Point start=Game1.player.TilePoint;grid[start]=1;
        var frontier=new PriorityQueue<Point,int>();frontier.Enqueue(start,0);var distance=new Dictionary<Point,int>{{start,0}};var previous=new Dictionary<Point,Point>();
        while(frontier.TryDequeue(out var cell,out int cost)) {
            if(distance[cell]!=cost)continue;
            foreach(var offset in new[]{new Point(0,1),new Point(1,0),new Point(-1,0),new Point(0,-1)}) {
                var neighbor=new Point(cell.X+offset.X,cell.Y+offset.Y);if(!grid.TryGetValue(neighbor,out int weight))continue;
                int value=cost+weight;if(distance.TryGetValue(neighbor,out int old)&&old<=value)continue;
                distance[neighbor]=value;previous[neighbor]=cell;frontier.Enqueue(neighbor,value);
            }
        }
        Point? goal=exits.Where(distance.ContainsKey).OrderBy(p=>distance[p]).Select(p=>(Point?)p).FirstOrDefault();volcanoAction="exit";
        if(goal==null&&volcanoMode!="retreat") {
            goal=location.dwarfGates.Where(g=>!g.opened.Value).SelectMany(g=>g.switches.Pairs.Where(s=>!s.Value).Select(s=>s.Key)).Where(distance.ContainsKey).OrderBy(p=>distance[p]).Select(p=>(Point?)p).FirstOrDefault();volcanoAction="switch";
        }
        if(goal==null)throw new InvalidOperationException("volcano_no_route_through_floor_lava_or_breakable_stone");
        if(goal.Value==start) {
            // Step away and back if the native touch switch has not registered yet.
            var adjacent=grid.Keys.Where(p=>Math.Abs(p.X-start.X)+Math.Abs(p.Y-start.Y)==1&&grid[p]==1).OrderBy(p=>p.Y).ToArray();
            if(adjacent.Length==0)throw new InvalidOperationException("volcano_switch_touch_not_available");Walk(adjacent[0]);Current!.phase="volcano_walking";volcanoAction="switch_reset";return;
        }
        var path=new List<Point>{goal.Value};while(path[^1]!=start)path.Add(previous[path[^1]]);path.Reverse();
        int obstacle=path.FindIndex(1,p=>grid[p]>1);
        if(obstacle==1) {
            volcanoObstacle=path[1];Face(volcanoObstacle);Adjacent(volcanoObstacle);
            if(IsVolcanoLava(location,volcanoObstacle)) {
                if(can<0||Game1.player.Items[can] is not WateringCan water||water.WaterLeft==0)throw new InvalidOperationException("volcano_watering_can_refill_required");
                if(Game1.player.Stamina<18)throw new InvalidOperationException("volcano_cooling_needs_stamina_reserve");
                PlayerSelection.Set(Game1.player,can);volcanoWaterBefore=water.WaterLeft;Game1.player.lastClick=volcanoObstacle.ToVector2()*64+new Vector2(32);Game1.player.BeginUsingTool();
                if(!Game1.player.UsingTool)throw new InvalidOperationException("native_volcano_watering_did_not_start");Current!.phase="volcano_cooling";return;
            }
            workSlot=pick;workSkill="clear";workTiles=new(){volcanoObstacle};workIndex=workHits=0;Current!.phase="work_next";volcanoAction="clear";return;
        }
        int end=obstacle>0?obstacle-1:path.Count-1;end=Math.Min(end,8);
        if(end<path.Count-1)volcanoAction="approach";Walk(path[end]);Current!.phase="volcano_walking";
    }
    private static bool IsVolcanoLava(VolcanoDungeon location,Point tile)=>location.level.Value!=5&&location.isTileOnMap(tile.ToVector2())&&location.waterTiles[tile.X,tile.Y]&&!location.IsCooledLava(tile.X,tile.Y)&&!location.CanRefillWateringCanOnTile(tile.X,tile.Y);
}
