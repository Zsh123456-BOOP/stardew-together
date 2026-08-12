using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed class PlayerAction {
    public string command_id {get;set;}="player:"+Guid.NewGuid().ToString("N");
    public string skill {get;set;}="";
    public string status {get;set;}="running";
    public string phase {get;set;}="starting";
    public string? error {get;set;}
    public object? before {get;set;}
    public object? after {get;set;}
    public int completed {get;set;}
    public List<object> effects {get;set;}=new();
}
public sealed class PlayerExecutor {
    private readonly Dictionary<string,PlayerAction> receipts=new();
    public PlayerAction? Current {get;private set;}
    public bool Busy=>Current?.status=="running";
    public bool NeedsMenuChoice=>Busy && Current!.skill=="player.sleep" && Current.phase=="overnight" && Game1.activeClickableMenu is not (null or ShippingMenu or SaveGameMenu);
    private Point target,lastTile;
    private string origin="",destination="";
    private DateTime started,lastProgress,nextInteraction;
    private int startDay,retries;
    private bool saved,sleepConfirmed,startedUsing;
    private PathFindController? ownedController;
    private Warp? edge;
    private List<Point> workTiles=new();
    private string workSkill="";
    private int workSlot,workIndex;
    private object? workBefore;
    public void ClearStopped(){if(!Busy){Current=null;receipts.Clear();}}
    public object Poll(string id)=>receipts.TryGetValue(id,out var r)?r:throw new InvalidOperationException("unknown_player_action");
    public object Cancel(string? id=null) {
        if(id!=null && (!receipts.TryGetValue(id,out var receipt) || receipt!=Current))return Poll(id);
        if(Busy) {
            if(sleepConfirmed)return new{command_id=Current!.command_id,status="running",error="native_save_in_progress_cannot_cancel"};
            Finish("cancelled","cancelled_by_controller");
        }
        return (object?)Current??new{status="idle"};
    }
    private static object Snapshot()=>new{day=Game1.Date.TotalDays,time=Game1.timeOfDay,location=Game1.currentLocation.NameOrUniqueName,tile=new[]{Game1.player.TilePoint.X,Game1.player.TilePoint.Y},
        money=Game1.player.Money,stamina=Game1.player.Stamina,inventory=AgentToolRegistry.Inventory(),menu=Game1.activeClickableMenu?.GetType().Name};
    public object Start(string skill,JsonElement args) {
        if(Busy)throw new InvalidOperationException("player_busy");
        if(Game1.activeClickableMenu!=null || Game1.eventUp || Game1.currentMinigame!=null || !Game1.player.CanMove || Game1.player.UsingTool)
            throw new InvalidOperationException("player_not_free_read_menu");
        Current=new(){skill=skill,before=Snapshot()};receipts[Current.command_id]=Current;
        foreach(string id in receipts.Keys.Take(Math.Max(0,receipts.Count-96)).ToArray())receipts.Remove(id);
        started=lastProgress=nextInteraction=DateTime.UtcNow;origin=Game1.currentLocation.NameOrUniqueName;
        startDay=Game1.Date.TotalDays;lastTile=Game1.player.TilePoint;retries=0;saved=false;sleepConfirmed=false;startedUsing=false;edge=null;
        try {
            switch(skill) {
                case "player.work":
                    workSkill=AgentToolRegistry.Text(args,"skill");workSlot=AgentToolRegistry.Number(args,"slot",-1);
                    if(workSkill is not ("water" or "till" or "plant" or "harvest"))throw new InvalidOperationException("unsupported_work_skill");
                    if(!args.TryGetProperty("tiles",out var tiles)||tiles.ValueKind!=JsonValueKind.Array||tiles.GetArrayLength() is <1 or >36)throw new InvalidOperationException("work_requires_1_to_36_tiles");
                    workTiles=tiles.EnumerateArray().Select(t=>Tile(t)).Distinct().ToList();workIndex=0;
                    if(workSkill!="harvest")SelectSlot(JsonSerializer.SerializeToElement(new{slot=workSlot}),true);
                    if(workSkill=="water" && Game1.player.CurrentTool is not StardewValley.Tools.WateringCan || workSkill=="till" && Game1.player.CurrentTool is not StardewValley.Tools.Hoe)throw new InvalidOperationException("wrong_tool_for_work");
                    Current.phase="work_next";break;
                case "player.move":target=Tile(args);destination=origin;Current.phase="walking";Walk(target);break;
                case "player.travel":destination=AgentToolRegistry.Text(args,"location");Current.phase="travelling";if(Game1.getLocationFromName(destination)==null)throw new InvalidOperationException("unknown_location");break;
                case "player.sleep":destination=Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName;Current.phase="returning_home";break;
                case "player.use_tool":
                    target=Tile(args);Adjacent(target);SelectSlot(args,true);
                    if(Game1.player.CurrentTool==null)throw new InvalidOperationException("slot_is_not_tool");
                    Face(target);Game1.player.lastClick=target.ToVector2()*64+new Vector2(32);Game1.player.BeginUsingTool();startedUsing=Game1.player.UsingTool;
                    if(!startedUsing)throw new InvalidOperationException("tool_did_not_start");Current.phase="tool_animation";break;
                case "player.interact":
                    target=Tile(args);Adjacent(target);SelectSlot(args,false);Face(target);Current.phase="interacting";
                    if(!Game1.tryToCheckAt(target.ToVector2(),Game1.player))throw new InvalidOperationException("interaction_not_available");break;
                case "player.place":
                    target=Tile(args);Adjacent(target);SelectSlot(args,true);Face(target);
                    if(Game1.player.ActiveObject==null)throw new InvalidOperationException("slot_not_placeable_object");
                    if(!Utility.tryToPlaceItem(Game1.currentLocation,Game1.player.ActiveObject,target.X*64,target.Y*64))throw new InvalidOperationException("native_placement_rejected");
                    Finish("succeeded");break;
                default:throw new InvalidOperationException("unsupported_player_skill");
            }
        }catch(Exception e){Finish("failed",e is InvalidOperationException?e.Message:e.GetType().Name);}
        return Current;
    }
    private static Point Tile(JsonElement a) {
        if(!a.TryGetProperty("x",out var x) || !a.TryGetProperty("y",out var y) || !x.TryGetInt32(out int ix) || !y.TryGetInt32(out int iy))throw new InvalidOperationException("tile_required");
        var l=Game1.currentLocation;if(ix<0||iy<0||ix>=l.Map.Layers[0].LayerWidth||iy>=l.Map.Layers[0].LayerHeight)throw new InvalidOperationException("tile_out_of_bounds");return new(ix,iy);
    }
    private static void Adjacent(Point p){if(Math.Abs(Game1.player.TilePoint.X-p.X)+Math.Abs(Game1.player.TilePoint.Y-p.Y)>1)throw new InvalidOperationException("move_adjacent_first");}
    private static void Face(Point p){if(p!=Game1.player.TilePoint)Game1.player.faceGeneralDirection(p.ToVector2()*64+new Vector2(32));}
    private static void SelectSlot(JsonElement a,bool required) {
        if(!a.TryGetProperty("slot",out var s)){if(required)throw new InvalidOperationException("slot_required");return;}
        if(!s.TryGetInt32(out int i)||i<0||i>=Game1.player.Items.Count||Game1.player.Items[i]==null)throw new InvalidOperationException("invalid_inventory_slot");
        Game1.player.CurrentToolIndex=i;Game1.player.netItemStowed.Value=false;
    }
    public static bool Passable(GameLocation l,Point p) {
        if(p.X<0||p.Y<0||p.X>=l.Map.Layers[0].LayerWidth||p.Y>=l.Map.Layers[0].LayerHeight)return false;
        var box=Game1.player.GetBoundingBox();box.Offset(p.X*64+32-box.Center.X,p.Y*64+48-box.Center.Y);
        return !l.isCollidingPosition(box,Game1.viewport,true,0,false,Game1.player,true,false,false,true);
    }
    private void StopWalk(){if(Game1.player.controller==ownedController)Game1.player.controller=null;ownedController=null;Game1.player.Halt();}
    private void Walk(Point p) {
        StopWalk();target=p;if(Game1.player.TilePoint==p)return;
        var controller=new PathFindController(Game1.player,Game1.currentLocation,p,-1);
        if(controller.pathToEndPoint==null || controller.pathToEndPoint.Count==0)throw new InvalidOperationException("no_path");
        ownedController=controller;Game1.player.controller=controller;lastProgress=DateTime.UtcNow;lastTile=Game1.player.TilePoint;
    }
    private Point Approach(Point p) {
        foreach(var option in new[]{p,new Point(p.X,p.Y+1),new Point(p.X-1,p.Y),new Point(p.X+1,p.Y),new Point(p.X,p.Y-1)}.OrderBy(t=>Vector2.Distance(t.ToVector2(),Game1.player.Tile)))
            if(Passable(Game1.currentLocation,option)) {
                var path=new PathFindController(Game1.player,Game1.currentLocation,option,-1);
                if(option==Game1.player.TilePoint || path.pathToEndPoint?.Count>0)return option;
            }
        throw new InvalidOperationException("exit_unreachable");
    }
    public void Tick() {
        if(!Busy || !Context.IsWorldReady)return;
        try {
            if(Current!.skill=="player.sleep" && sleepConfirmed){TickNight();return;}
            if((DateTime.UtcNow-started).TotalSeconds>180){Finish("failed","action_timeout");return;}
            if(Game1.eventUp){Finish("failed","event_interrupted_read_state");return;}
            if(Current.skill=="player.work"){TickWork();return;}
            if(Current.skill=="player.use_tool") {
                if(!Game1.player.UsingTool && startedUsing)Finish("succeeded");return;
            }
            if(Current.skill=="player.interact"){if(!Game1.player.UsingTool)Finish("succeeded");return;}
            if(Current.skill=="player.move") {
                if(Game1.currentLocation.NameOrUniqueName!=origin){Finish("failed","location_changed_before_destination");return;}
                if(Game1.player.TilePoint==target){Finish("succeeded");return;}
            } else if(Current.skill is "player.travel" or "player.sleep") {
                if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
                if(Current.skill=="player.travel"){Finish("succeeded");return;}
                var house=Utility.getHomeOfFarmer(Game1.player);var bed=house.GetPlayerBed();
                if(bed==null)throw new InvalidOperationException("home_has_no_player_bed");
                var spot=house.GetPlayerBedSpot();
                if(Game1.player.TilePoint!=spot) {
                    if(Current.phase!="walking_to_bed"){Current.phase="walking_to_bed";Walk(spot);}
                } else {
                    StopWalk();
                    if(Game1.activeClickableMenu is DialogueBox dialog && dialog.responses.Any(r=>r.responseKey=="Yes"))Game1.activeClickableMenu=null;
                    else if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("unexpected_bed_menu");
                    // Same native response as confirming the actual bed prompt, only after walking onto its real spot.
                    Game1.player.isInBed.Value=true;sleepConfirmed=true;Current.phase="overnight";
                    house.answerDialogueAction("Sleep_Yes",Array.Empty<string>());return;
                }
            }
            if(Game1.activeClickableMenu!=null){Finish("failed","menu_interrupted_read_menu");return;}
            MonitorWalk();
        }catch(Exception e){Finish("failed",e is InvalidOperationException?e.Message:e.GetType().Name);}
    }
    private static object TileState(Point p) {
        var l=Game1.currentLocation;var v=p.ToVector2();l.objects.TryGetValue(v,out var o);l.terrainFeatures.TryGetValue(v,out var f);var dirt=f as HoeDirt;
        return new{x=p.X,y=p.Y,item=o?.QualifiedItemId,stack=o?.Stack,terrain=f?.GetType().Name,watered=dirt?.state.Value,crop=dirt?.crop?.indexOfHarvest.Value,phase=dirt?.crop?.currentPhase.Value,ready=dirt?.readyForHarvest()};
    }
    private void TickWork() {
        if(Game1.currentLocation.NameOrUniqueName!=origin)throw new InvalidOperationException("work_location_changed");
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("work_interrupted_by_menu");
        if(workIndex>=workTiles.Count){Finish("succeeded");return;}
        Point tile=workTiles[workIndex];
        if(Current!.phase=="work_next") {
            workBefore=TileState(tile);var v=tile.ToVector2();Game1.currentLocation.terrainFeatures.TryGetValue(v,out var f);var dirt=f as HoeDirt;
            bool already=workSkill=="water"&&dirt?.state.Value==1 || workSkill=="till"&&dirt!=null;
            if(already){Current.effects.Add(new{tile=workBefore,status="already_satisfied"});workIndex++;return;}
            if(workSkill=="water"&&dirt?.crop==null || workSkill=="plant"&&(dirt==null||dirt.crop!=null) || workSkill=="harvest"&&dirt?.readyForHarvest()!=true)
                throw new InvalidOperationException("work_target_not_eligible");
            Walk(Approach(tile));Current.phase="work_walk";
        }
        if(Current.phase=="work_walk") {
            if(Game1.player.TilePoint!=target){MonitorWalk();return;}
            StopWalk();Adjacent(tile);Face(tile);
            if(workSkill!="harvest")SelectSlot(JsonSerializer.SerializeToElement(new{slot=workSlot}),true);
            if(workSkill is "water" or "till") {
                Game1.player.lastClick=tile.ToVector2()*64+new Vector2(32);Game1.player.BeginUsingTool();
                if(!Game1.player.UsingTool)throw new InvalidOperationException("work_tool_not_started");
            } else if(workSkill=="plant") {
                if(Game1.player.ActiveObject==null || !Utility.tryToPlaceItem(Game1.currentLocation,Game1.player.ActiveObject,tile.X*64,tile.Y*64))throw new InvalidOperationException("native_plant_rejected");
            } else if(!Game1.tryToCheckAt(tile.ToVector2(),Game1.player))throw new InvalidOperationException("native_harvest_rejected");
            Current.phase="work_impact";return;
        }
        if(Current.phase=="work_impact" && !Game1.player.UsingTool) {
            var after=TileState(tile);
            if(JsonSerializer.Serialize(workBefore)==JsonSerializer.Serialize(after))throw new InvalidOperationException("work_effect_not_observed");
            Current.effects.Add(new{before=workBefore,after});Current.completed++;workIndex++;retries=0;Current.phase="work_next";
        }
    }
    private void MonitorWalk() {
        if(Game1.player.TilePoint!=lastTile){lastTile=Game1.player.TilePoint;lastProgress=DateTime.UtcNow;}
        if((DateTime.UtcNow-lastProgress).TotalSeconds<3)return;
        if(++retries>2)throw new InvalidOperationException("path_stalled");Walk(target);
    }
    private void Travel() {
        var l=Game1.currentLocation;
        if(origin!=l.NameOrUniqueName || edge==null) {
            origin=l.NameOrUniqueName;edge=NextExit(l,destination)??throw new InvalidOperationException("no_known_route");
            Walk(Approach(new(edge.X,edge.Y)));nextInteraction=DateTime.UtcNow;retries=0;
        }
        var at=new Point(edge.X,edge.Y);float distance=Vector2.Distance(Game1.player.Tile,at.ToVector2());
        if(distance<=1.1f && DateTime.UtcNow>=nextInteraction && Game1.activeClickableMenu==null) {
            nextInteraction=DateTime.UtcNow.AddSeconds(2);StopWalk();
            // Doors execute the native action; boundary warps are reached by walking, never by arbitrary teleport.
            if(at.X>=0&&at.Y>=0&&at.X<l.Map.Layers[0].LayerWidth&&at.Y<l.Map.Layers[0].LayerHeight && Game1.tryToCheckAt(at.ToVector2(),Game1.player))return;
            var warp=l.warps.FirstOrDefault(w=>w.X==edge.X&&w.Y==edge.Y&&w.TargetName==edge.TargetName);
            if(warp!=null) {
                int direction=Math.Abs(at.X-Game1.player.TilePoint.X)>Math.Abs(at.Y-Game1.player.TilePoint.Y)?(at.X>Game1.player.TilePoint.X?1:3):(at.Y>Game1.player.TilePoint.Y?2:0);
                Game1.player.faceDirection(direction);Game1.player.setMovingInFacingDirection();
            }
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("travel_menu_requires_choice");
        MonitorWalk();
    }
    private void TickNight() {
        if((DateTime.UtcNow-started).TotalSeconds>240){Finish("failed","overnight_timeout_check_save");return;}
        // Non-branching shipping confirmation is deterministic; profession/reward choices stay with the model.
        if(Game1.activeClickableMenu is ShippingMenu shipping && DateTime.UtcNow>=nextInteraction) {
            nextInteraction=DateTime.UtcNow.AddMilliseconds(400);
            var b=shipping.okButton.bounds;shipping.receiveLeftClick(b.Center.X,b.Center.Y);
        }
    }
    public void Saved(){if(Busy && sleepConfirmed)saved=true;}
    public bool DayStarted() {
        if(!Busy || !sleepConfirmed)return false;
        bool advanced=Game1.Date.TotalDays==startDay+1;
        Finish(advanced&&saved?"succeeded":"failed",advanced&&saved?null:"day_transition_not_verified");return advanced&&saved;
    }
    private void Finish(string status,string? error=null) {
        if(Current==null)return;StopWalk();Current.status=status;Current.error=error;Current.phase=status;Current.after=Context.IsWorldReady?Snapshot():null;
    }
    public static IEnumerable<Warp> Exits(GameLocation location) {
        foreach(var warp in location.warps)if(!warp.npcOnly.Value)yield return warp;
        foreach(var door in location.doors.Pairs) {
            var a=location.GetTilePropertySplitBySpaces("Action","Buildings",door.Key.X,door.Key.Y);
            if(a.Length>=4 && a[0] is "Warp" or "LockedDoorWarp" && int.TryParse(a[1],out int x)&&int.TryParse(a[2],out int y))
                yield return new Warp(door.Key.X,door.Key.Y,a[3],x,y,false);
        }
        foreach(var building in location.buildings) {
            var inside=building.GetIndoors();if(inside==null || building.daysOfConstructionLeft.Value>0 || building.humanDoor.Value.X<0)continue;
            var exit=inside.warps.FirstOrDefault(w=>w.TargetName==location.NameOrUniqueName || w.TargetName==location.Name);if(exit==null)continue;
            yield return new Warp(building.tileX.Value+building.humanDoor.Value.X,building.tileY.Value+building.humanDoor.Value.Y,inside.NameOrUniqueName,exit.X,exit.Y-1,false);
        }
    }
    private static Warp? NextExit(GameLocation from,string destination) {
        var queue=new Queue<(GameLocation Location,Warp? First)>();queue.Enqueue((from,null));var visited=new HashSet<string>{from.NameOrUniqueName};
        while(queue.Count>0&&visited.Count<150) {
            var (l,first)=queue.Dequeue();foreach(var edge in Exits(l)) {
                var next=Game1.getLocationFromName(edge.TargetName);if(next==null||!visited.Add(next.NameOrUniqueName))continue;
                if(next.NameOrUniqueName==destination)return first??edge;queue.Enqueue((next,first??edge));
            }
        }
        return null;
    }
}
