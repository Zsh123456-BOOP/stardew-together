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
public sealed partial class PlayerExecutor {
    private readonly Dictionary<string,PlayerAction> receipts=new();
    public PlayerAction? Current {get;private set;}
    public Func<LevelUpMenu,bool>? ApplyProfession {get;set;}
    public Action<int>? NativeSleepRequested {get;set;}
    public Action<IReadOnlyDictionary<Item,int>,string,string>? ValidateConsumption {get;set;}
    public bool Busy=>Current?.status=="running";
    public bool NeedsMenuChoice=>Busy && Current!.skill=="player.sleep" && Current.phase=="overnight" && Game1.activeClickableMenu is not (null or ShippingMenu or SaveGameMenu or LevelUpMenu {isProfessionChooser:false});
    private Point target,lastTile;
    private string origin="",destination="";
    private DateTime started,lastProgress,nextInteraction;
    private int startDay,retries;
    private bool saved,sleepConfirmed,startedUsing;
    private PathFindController? ownedController;
    private Warp? edge;
    private bool boundaryDriving;
    private List<Point> workTiles=new();
    private string workSkill="";
    private int workSlot,workIndex,workHits;
    private int eatingSlot,eatingBefore;
    private string eatingItem="";
    private object? workBefore,actionTargetBefore;
    public bool ClaimsTile(string location,int x,int y)=>Busy && origin==location && (Current!.skill=="player.work"?workTiles.Skip(workIndex).Any(p=>p.X==x&&p.Y==y):Current.skill is "player.use_tool" or "player.place" or "player.interact" && target.X==x&&target.Y==y);
    public void ClearWorld(){Current=null;receipts.Clear();ownedController=null;boundaryDriving=false;}
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
        money=Game1.player.Money,health=Game1.player.health,stamina=Game1.player.Stamina,inventory=AgentToolRegistry.Inventory(),menu=Game1.activeClickableMenu?.GetType().Name};
    internal static bool AcceptsNativeMenu(string skill)=>skill=="player.forge"&&Game1.activeClickableMenu is ForgeMenu || skill=="player.read_mail"&&Game1.activeClickableMenu is LetterViewerMenu || skill=="player.joja"&&Game1.activeClickableMenu is JojaCDMenu || skill=="player.order_donate"&&Game1.activeClickableMenu is QuestContainerMenu || skill=="player.accept_quest"&&Game1.activeClickableMenu is (Billboard or SpecialOrdersBoard) || skill=="player.geodes"&&Game1.activeClickableMenu is GeodeMenu || skill=="player.buy_animal"&&Game1.activeClickableMenu is PurchaseAnimalsMenu || skill=="player.mine_access"&&Game1.activeClickableMenu is MineElevatorMenu || skill=="player.bundle"&&Game1.activeClickableMenu is JunimoNoteMenu || skill=="player.build"&&Game1.activeClickableMenu is CarpenterMenu || skill=="player.donate_museum"&&Game1.activeClickableMenu is MuseumMenu || skill=="player.buy"&&Game1.activeClickableMenu is ShopMenu || skill=="player.collect_reward"&&Game1.activeClickableMenu is ItemGrabMenu;
    public object Start(string skill,JsonElement args) {
        if(Busy)throw new InvalidOperationException("player_busy");
        bool buying=AcceptsNativeMenu(skill);
        if(Game1.locationRequest!=null || Game1.fadeToBlack || Game1.activeClickableMenu!=null&&!buying || Game1.eventUp || Game1.currentMinigame!=null || !Game1.player.CanMove&&!buying || Game1.player.UsingTool)
            throw new InvalidOperationException("player_not_free_read_menu");
        Current=new(){skill=skill,before=Snapshot()};receipts[Current.command_id]=Current;
        foreach(string id in receipts.Keys.Take(Math.Max(0,receipts.Count-96)).ToArray())receipts.Remove(id);
        started=lastProgress=nextInteraction=DateTime.UtcNow;origin=Game1.currentLocation.NameOrUniqueName;
        actionTargetBefore=null;startDay=Game1.Date.TotalDays;lastTile=Game1.player.TilePoint;retries=0;saved=false;sleepConfirmed=false;startedUsing=false;edge=null;
        try {
            switch(skill) {
                case "player.volcano_step":StartVolcanoStep(args);break;
                case "player.treasure":StartTreasure();break;
                case "player.walnuts":StartWalnuts(args);break;
                case "player.forge":StartForge(args);break;
                case "player.island_upgrade":StartIslandUpgrade(args);break;
                case "player.arcade":StartArcade(args);break;
                case "player.read_mail":case "player.watch_tv":StartInformation(args);break;
                case "player.transport":case "player.repair_boat":StartTransit(args);break;
                case "player.read_book":StartReadBook(args);break;
                case "player.mastery":StartMastery(args);break;
                case "player.orchard":StartOrchard(args);break;
                case "player.joja":StartJoja(args);break;
                case "player.place_facility":StartFacilityPlacement(args);break;
                case "player.ship_items":StartShipping(args);break;
                case "player.order_donate":StartOrderDonation(args);break;
                case "player.equip":StartEquipment(args);break;
                case "player.attach":StartAttachment(args);break;
                case "player.accept_quest":StartQuestAcceptance(args);break;
                case "player.animal":StartAnimalManagement(args);break;
                case "player.geodes":StartGeodes(args);break;
                case "player.buy_animal":StartLivestockPurchase(args);break;
                case "player.upgrade_house":StartHouseUpgrade(args);break;
                case "player.mine_access":StartMineAccess(args);break;
                case "player.bundle":StartBundle(args);break;
                case "player.build":StartConstruction(args);break;
                case "player.donate_museum":StartMuseumDonation(args);break;
                case "player.collect_reward":StartCollectReward();break;
                case "player.service":StartService(args);break;
                case "player.machine":StartMachines(args);break;
                case "player.claim_reward":StartQuestReward(args);break;
                case "player.care":StartAnimalCare(args);break;
                case "player.social":StartSocial(args);break;
                case "player.combat":StartCombat(args);break;
                case "player.mine_descend":StartMineDescent();break;
                case "player.fish":StartFishing(args);break;
                case "player.buy":StartPurchase(args);break;
                case "player.craft":case "player.cook":StartProduction(skill,args);break;
                case "player.eat":
                    SelectSlot(args,true);
                    if(Game1.player.ActiveObject is not {} food || food.Edibility<=0 || food.questItem.Value || food.QualifiedItemId=="(O)434")throw new InvalidOperationException("item_not_ordinary_food");
                    ValidateConsumption?.Invoke(new Dictionary<Item,int>{{food,1}},"","");
                    eatingSlot=Game1.player.CurrentToolIndex;eatingItem=food.QualifiedItemId;eatingBefore=food.Stack;
                    Game1.player.mostRecentlyGrabbedItem=food;Game1.player.eatHeldObject();
                    if(!Game1.player.isEating)throw new InvalidOperationException("native_eating_rejected");
                    Current.phase="eating";break;
                case "player.work":
                    workSkill=AgentToolRegistry.Text(args,"skill");workSlot=AgentToolRegistry.Number(args,"slot",-1);
                    if(workSkill is not ("water" or "till" or "plant" or "fertilize" or "harvest" or "clear" or "chop" or "break_clump" or "clear_dead" or "forage"))throw new InvalidOperationException("unsupported_work_skill");
                    if(!args.TryGetProperty("tiles",out var tiles)||tiles.ValueKind!=JsonValueKind.Array||tiles.GetArrayLength() is <1 or >36)throw new InvalidOperationException("work_requires_1_to_36_tiles");
                    workTiles=tiles.EnumerateArray().Select(t=>Tile(t)).Distinct().ToList();workIndex=0;workHits=0;
                    if(workSkill is not ("harvest" or "forage"))SelectSlot(JsonSerializer.SerializeToElement(new{slot=workSlot}),true);
                    if(workSkill=="water" && Game1.player.CurrentTool is not StardewValley.Tools.WateringCan || workSkill=="till" && Game1.player.CurrentTool is not StardewValley.Tools.Hoe)throw new InvalidOperationException("wrong_tool_for_work");
                    Current.phase="work_next";break;
                case "player.move":target=Tile(args);destination=origin;Current.phase="walking";Walk(target);break;
                case "player.travel":destination=AgentToolRegistry.Text(args,"location");Current.phase="travelling";if(LoadedLocation(destination)==null&&NextExit(Game1.currentLocation,destination)==null)throw new InvalidOperationException("unknown_or_unobserved_route");break;
                case "player.sleep":destination=Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName;Current.phase="returning_home";break;
                case "player.use_tool":
                    target=Tile(args);Adjacent(target);actionTargetBefore=TileState(target);SelectSlot(args,true);
                    if(Game1.player.CurrentTool==null)throw new InvalidOperationException("slot_is_not_tool");
                    if(Game1.player.CurrentTool is StardewValley.Tools.WateringCan {WaterLeft:0} && !Game1.currentLocation.CanRefillWateringCanOnTile(target.X,target.Y))throw new InvalidOperationException("watering_can_empty_read_day_refill_options_or_delegate");
                    Face(target);Game1.player.lastClick=target.ToVector2()*64+new Vector2(32);Game1.player.BeginUsingTool();startedUsing=Game1.player.UsingTool;
                    if(!startedUsing)throw new InvalidOperationException("tool_did_not_start");Current.phase="tool_animation";break;
                case "player.interact":
                    target=Tile(args);Adjacent(target);actionTargetBefore=TileState(target);SelectSlot(args,false);Face(target);Current.phase="interacting";
                    if(Game1.currentLocation.objects.TryGetValue(target.ToVector2(),out var container) && container is StardewValley.Objects.Chest chest && chest.playerChest.Value && !chest.giftbox.Value) {
                        if(chest.GetMutex().IsLocked())throw new InvalidOperationException("chest_busy");
                        chest.GetMutex().RequestLock(()=>{
                            if(chest.SpecialChestType==StardewValley.Objects.Chest.SpecialChestTypes.MiniShippingBin)chest.ShowMenu();
                            else {chest.frameCounter.Value=5;Game1.playSound("openChest");Game1.player.freezePause=1000;}
                        });
                    } else if(!Game1.tryToCheckAt(target.ToVector2(),Game1.player))throw new InvalidOperationException("interaction_not_available");break;
                case "player.ship":
                    SelectSlot(args,true);
                    if(Game1.currentLocation is not Farm shippingFarm)throw new InvalidOperationException("shipping_requires_farm");
                    if(Game1.player.ActiveObject is not {} cargo || !cargo.canBeShipped())throw new InvalidOperationException("item_not_shippable");
                    ValidateConsumption?.Invoke(new Dictionary<Item,int>{{cargo,cargo.Stack}},"","");
                    bool atBin=shippingFarm.buildings.Any(b=>b.buildingType.Value=="Shipping Bin" && new Rectangle(b.tileX.Value*64-64,b.tileY.Value*64-64,(b.tilesWide.Value+2)*64,(b.tilesHigh.Value+2)*64).Intersects(Game1.player.GetBoundingBox()));
                    if(!atBin)throw new InvalidOperationException("move_next_to_shipping_bin_first");
                    shippingFarm.shipItem(cargo,Game1.player);
                    Current.effects.Add(new{shipped=cargo.QualifiedItemId,count=cargo.Stack,confirmed_in_bin=shippingFarm.getShippingBin(Game1.player).Contains(cargo),income="pending_native_overnight"});Finish("succeeded");break;
                case "player.place":
                    target=Tile(args);Adjacent(target);actionTargetBefore=TileState(target);SelectSlot(args,true);Face(target);
                    if(Game1.player.ActiveObject==null)throw new InvalidOperationException("slot_not_placeable_object");
                    var placedItem=Game1.player.ActiveObject;string placedId=placedItem.QualifiedItemId;int placeBefore=Game1.player.Items.Where(i=>i?.QualifiedItemId==placedId).Sum(i=>i.Stack);
                    ValidateConsumption?.Invoke(new Dictionary<Item,int>{{placedItem,1}},AgentToolRegistry.Text(args,"goal_id"),placedId);
                    if(!Utility.tryToPlaceItem(Game1.currentLocation,placedItem,target.X*64,target.Y*64))throw new InvalidOperationException("native_placement_rejected");
                    int placeConsumed=placeBefore-Game1.player.Items.Where(i=>i?.QualifiedItemId==placedId).Sum(i=>i.Stack);
                    if(placeConsumed!=1)throw new InvalidOperationException("native_placement_consumption_not_verified");
                    Current.effects.Add(new{kind="native_placement",item=placedId,consumed=placeConsumed,tile=target});
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
    private void StopWalk(){
        // Halt resets the sprite animation. Only halt movement owned by this executor;
        // native hold-up/receive-item animations must reach their own completion callbacks.
        if((ownedController!=null || boundaryDriving) && (Game1.player.controller==ownedController || Game1.player.controller==null)) {
            if(Game1.player.controller==ownedController)Game1.player.controller=null;Game1.player.Halt();
        }
        ownedController=null;boundaryDriving=false;
    }
    private void Walk(Point p) {
        StopWalk();target=p;if(Game1.player.TilePoint==p)return;
        var controller=new PathFindController(Game1.player,Game1.currentLocation,p,-1);
        if(controller.pathToEndPoint==null || controller.pathToEndPoint.Count==0)throw new InvalidOperationException("no_path");
        ownedController=controller;Game1.player.controller=controller;lastProgress=DateTime.UtcNow;lastTile=Game1.player.TilePoint;
    }
    private Point Approach(Point p,bool adjacentOnly=false) {
        foreach(var option in new[]{p,new Point(p.X,p.Y+1),new Point(p.X-1,p.Y),new Point(p.X+1,p.Y),new Point(p.X,p.Y-1)}.OrderBy(t=>Vector2.Distance(t.ToVector2(),Game1.player.Tile)))
            if((!adjacentOnly || option!=p) && Passable(Game1.currentLocation,option)) {
                var path=new PathFindController(Game1.player,Game1.currentLocation,option,-1);
                if(option==Game1.player.TilePoint || path.pathToEndPoint?.Count>0)return option;
            }
        throw new InvalidOperationException("exit_unreachable");
    }
    public void ObserveNativeTransition() {
        if(!Context.IsWorldReady || !Busy)return;
        if(Game1.locationRequest!=null || Game1.fadeToBlack) {
            // A Farmer path controller moves even when CanMove=false. Keeping it during
            // fade re-enters the boundary warp every frame and restarts the transition.
            if(ownedController!=null && Game1.player.controller==ownedController)Game1.player.controller=null;
            ownedController=null;boundaryDriving=false;lastProgress=DateTime.UtcNow;
        }
    }
    public void Tick() {
        if(!Busy || !Context.IsWorldReady)return;
        try {
            if(Current!.skill=="player.sleep" && sleepConfirmed){TickNight();return;}
            if((DateTime.UtcNow-started).TotalSeconds>(Current.skill=="player.arcade"?arcadeSeconds+180:Current.skill=="player.fish"?900:180)){Finish("failed","action_timeout");return;}
            if(Current.skill=="player.eat") {
                if(Game1.player.isEating || !Game1.player.CanMove)return;
                var remaining=Game1.player.Items[eatingSlot];int count=remaining?.QualifiedItemId==eatingItem?remaining.Stack:0;
                Current.effects.Add(new{kind="native_eat",item=eatingItem,consumed=eatingBefore-count,stamina=Game1.player.Stamina,health=Game1.player.health});
                Finish(eatingBefore-count==1?"succeeded":"failed",eatingBefore-count==1?null:"food_consumption_not_verified");return;
            }
            if(Current.skill=="player.arcade"){TickArcade();return;}
            if(Current.skill=="player.island_upgrade"){TickIslandUpgrade();return;}
            if(Current.skill is "player.transport" or "player.repair_boat"){TickTransit();return;}
            ObserveNativeTransition();
            // Events often set CanMove=false. Report the interruption before the
            // movement gate, otherwise a travel task waits forever behind dialogue.
            if(Game1.eventUp){
                // Completing the physical bundle menu can start its native restoration event.
                // The donation is finished; the event remains a separate, observable activity.
                if(Current.skill=="player.bundle"&&donationClosing&&Game1.RequireLocation<StardewValley.Locations.CommunityCenter>("CommunityCenter").bundles.TryGetValue(donationBundle,out bool[] donated)&&donated.All(value=>value)) {
                    Current.effects.Add(new{kind="bundle_completed_event_pending",bundle=donationBundle});Finish("succeeded");
                } else Finish("failed","event_interrupted_read_menu");return;
            }
            if(Current.skill is "player.craft" or "player.cook"){TickProduction();return;}
            if(Current.skill=="player.buy"){TickPurchase();return;}
            if(Current.skill=="player.volcano_step"){TickVolcanoStep();return;}
            if(Current.skill=="player.treasure"){TickTreasure();return;}
            if(Current.skill=="player.walnuts"){TickWalnuts();return;}
            if(Current.skill=="player.forge"){TickForge();return;}
            if(Current.skill=="player.fish"){TickFishing();return;}
            if(Current.skill is "player.read_mail" or "player.watch_tv"){TickInformation();return;}
            if(Current.skill=="player.read_book"){TickReadBook();return;}
            if(Current.skill=="player.mastery"){TickMastery();return;}
            if(Current.skill=="player.orchard"){TickOrchard();return;}
            if(Current.skill=="player.joja"){TickJoja();return;}
            if(Current.skill=="player.place_facility"){TickFacilityPlacement();return;}
            if(Current.skill=="player.ship_items"){TickShipping();return;}
            if(Current.skill=="player.order_donate"){TickOrderDonation();return;}
            if(Current.skill=="player.animal"){TickAnimalManagement();return;}
            if(Current.skill=="player.geodes"){TickGeodes();return;}
            if(Current.skill=="player.buy_animal"){TickLivestockPurchase();return;}
            if(Current.skill=="player.upgrade_house"){TickHouseUpgrade();return;}
            if(Current.skill=="player.mine_access"){TickMineAccess();return;}
            if(Current.skill=="player.bundle"){TickBundle();return;}
            if(Current.skill=="player.build"){TickConstruction();return;}
            if(Current.skill=="player.donate_museum"){TickMuseumDonation();return;}
            if(Current.skill=="player.collect_reward"){TickCollectReward();return;}
            if(Current.skill=="player.service"){TickService();return;}
            if(Current.skill=="player.machine"){TickMachines();return;}
            if(Current.skill=="player.claim_reward"){TickQuestReward();return;}
            if(Current.skill=="player.care"){TickAnimalCare();return;}
            if(Current.skill=="player.social"){TickSocial();return;}
            if(Current.skill=="player.combat"){TickCombat();return;}
            if(Current.skill=="player.mine_descend"){TickMineDescent();return;}
            if(Game1.locationRequest!=null || Game1.fadeToBlack || (!Game1.player.CanMove && Current.skill is "player.travel" or "player.sleep"))return;
            if(Current.skill=="player.work"){TickWork();return;}
            if(Current.skill=="player.use_tool") {
                if(!Game1.player.UsingTool && Game1.player.CanMove && startedUsing)Finish("succeeded");return;
            }
            if(Current.skill=="player.interact"){if((Game1.player.CanMove && !Game1.player.UsingTool && Game1.player.freezePause<=0) || Game1.activeClickableMenu!=null)Finish("succeeded");return;}
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
                    NativeSleepRequested?.Invoke(startDay);house.answerDialogueAction("Sleep_Yes",Array.Empty<string>());return;
                }
            }
            if(Game1.activeClickableMenu!=null){Finish("failed","menu_interrupted_read_menu");return;}
            MonitorWalk();
        }catch(Exception e){Finish("failed",e is InvalidOperationException?e.Message:e.GetType().Name);}
    }
    private static ResourceClump? ClumpAt(Point p)=>Game1.currentLocation.resourceClumps.FirstOrDefault(c=>p.X>=c.Tile.X&&p.X<c.Tile.X+c.width.Value&&p.Y>=c.Tile.Y&&p.Y<c.Tile.Y+c.height.Value);
    private static object TileState(Point p) {
        var l=Game1.currentLocation;var v=p.ToVector2();l.objects.TryGetValue(v,out var o);l.terrainFeatures.TryGetValue(v,out var f);var dirt=f as HoeDirt;var clump=ClumpAt(p);
        return new{clump_id=clump?.parentSheetIndex.Value,clump_health=clump?.health.Value,x=p.X,y=p.Y,item=o?.QualifiedItemId,stack=o?.Stack,health=o?.getHealth(),remaining_work=o?.MinutesUntilReady,terrain=f?.GetType().Name,tree_health=(f as Tree)?.health.Value,tree_stump=(f as Tree)?.stump.Value,watered=dirt?.state.Value,fertilizer=dirt?.fertilizer.Value,crop=dirt?.crop?.indexOfHarvest.Value,phase=dirt?.crop?.currentPhase.Value,ready=dirt?.readyForHarvest()};
    }
    private void TickWork() {
        if(Game1.currentLocation.NameOrUniqueName!=origin)throw new InvalidOperationException("work_location_changed");
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("work_interrupted_by_menu");
        if(workIndex>=workTiles.Count){
            if(!Game1.player.CanMove || Game1.player.UsingTool || Game1.player.freezePause>0)return;
            // The last impact can finish before its debris reaches the Farmer. Let native
            // collection settle so receipts include the final crop/material where picked up.
            if(workSkill is "harvest" or "clear" or "chop" or "break_clump" or "clear_dead" or "forage") {
                if(Current!.phase!="settling_drops"){Current.phase="settling_drops";nextInteraction=DateTime.UtcNow.AddSeconds(1);}
                if(DateTime.UtcNow<nextInteraction)return;
            }
            Finish("succeeded");return;
        }
        Point tile=workTiles[workIndex];
        if(Current!.phase=="work_collect_start") {
            if(!Game1.player.CanMove || Game1.player.UsingTool)return;
            // Walk over the cleared tile so a drop thrown away from the adjacent work
            // position is actually collected. Never insert loot directly into inventory.
            if(Passable(Game1.currentLocation,tile)){Walk(tile);Current.phase="work_collect_walk";}
            else {nextInteraction=DateTime.UtcNow.AddSeconds(1);Current.phase="work_collect_settle";}
            return;
        }
        if(Current.phase=="work_collect_walk") {
            if(Game1.player.TilePoint!=target){MonitorWalk();return;}
            StopWalk();nextInteraction=DateTime.UtcNow.AddMilliseconds(750);Current.phase="work_collect_settle";return;
        }
        if(Current.phase=="work_collect_settle") {
            if(DateTime.UtcNow<nextInteraction || !Game1.player.CanMove)return;
            Current.completed++;workIndex++;workHits=0;retries=0;Current.phase="work_next";return;
        }
        if(Current!.phase=="work_next") {
            workBefore=TileState(tile);var v=tile.ToVector2();Game1.currentLocation.terrainFeatures.TryGetValue(v,out var f);var dirt=f as HoeDirt;
            if(workSkill=="break_clump") {
                var clump=ClumpAt(tile)??throw new InvalidOperationException("resource_clump_gone");
                var rule=ResourceRules.Clump(clump.parentSheetIndex.Value)??throw new InvalidOperationException("unknown_resource_clump");
                var tool=Game1.player.Items[workSlot] as Tool;
                if(tool==null||tool.UpgradeLevel<rule.Level||rule.Tool=="axe"&&tool is not StardewValley.Tools.Axe||rule.Tool=="pickaxe"&&tool is not StardewValley.Tools.Pickaxe)throw new InvalidOperationException("resource_clump_tool_upgrade_required");
            }
            if(workSkill=="chop") {
                if(f is not Tree tree || tree.growthStage.Value<5 || tree.tapped.Value)throw new InvalidOperationException("not_an_eligible_mature_tree");
                if(Game1.player.Items[workSlot] is not StardewValley.Tools.Axe)throw new InvalidOperationException("chop_requires_axe");
                if(tree.falling.Value){return;}
            }
            if(workSkill=="water" && Game1.player.Items[workSlot] is StardewValley.Tools.WateringCan {WaterLeft:0})throw new InvalidOperationException("watering_can_empty_read_day_refill_options_or_delegate");
            if(workSkill=="clear") {
                if(!Game1.currentLocation.objects.TryGetValue(v,out var resource))throw new InvalidOperationException("resource_no_longer_present");
                SelectSlot(JsonSerializer.SerializeToElement(new{slot=workSlot}),true);
                if(!(resource.IsTwig() && Game1.player.CurrentTool is StardewValley.Tools.Axe || (resource.BaseName=="Stone"||ResourceRules.Nodes.ContainsKey(resource.ItemId)) && Game1.player.CurrentTool is StardewValley.Tools.Pickaxe || resource.IsWeeds() && Game1.player.CurrentTool?.isScythe()==true))throw new InvalidOperationException("wrong_resource_or_tool");
                if(Game1.player.Stamina<17 && Game1.player.CurrentTool?.isScythe()!=true)throw new InvalidOperationException("energy_reserve_reached");
            }
            if(workSkill=="clear_dead") {
                SelectSlot(JsonSerializer.SerializeToElement(new{slot=workSlot}),true);
                if(Game1.player.CurrentTool?.isScythe()!=true)throw new InvalidOperationException("clear_dead_requires_scythe");
                if(dirt==null)throw new InvalidOperationException("dead_crop_not_present");
                // A native scythe swing can also clear the next dead crop in this batch.
                if(dirt.crop==null){Current.effects.Add(new{tile=workBefore,status="already_clear"});workIndex++;return;}
                if(!dirt.crop.dead.Value)throw new InvalidOperationException("refusing_to_clear_living_crop");
            }
            if(workSkill=="forage" && (!Game1.currentLocation.objects.TryGetValue(v,out var forage) || !forage.isForage() || forage.bigCraftable.Value))throw new InvalidOperationException("not_a_forage_target");
            bool already=workSkill=="water"&&dirt?.state.Value==1 || workSkill=="till"&&dirt!=null;
            if(workSkill=="fertilize") {
                if(Game1.player.Items[workSlot] is not StardewValley.Object {Category:-19} fertilizer||dirt==null)throw new InvalidOperationException("fertilizer_and_tilled_tile_required");
                already=ItemRegistry.QualifyItemId(dirt.fertilizer.Value)==fertilizer.QualifiedItemId;
                if(!already&&!dirt.CanApplyFertilizer(fertilizer.QualifiedItemId))throw new InvalidOperationException("native_fertilizer_rules_reject_tile");
            }
            if(already){Current.effects.Add(new{tile=workBefore,status="already_satisfied"});workIndex++;return;}
            if(workSkill=="water"&&dirt?.crop==null || workSkill=="plant"&&(dirt==null||dirt.crop!=null) || workSkill=="harvest"&&dirt?.readyForHarvest()!=true)
                throw new InvalidOperationException("work_target_not_eligible");
            Walk(Approach(tile,true));Current.phase="work_walk";
        }
        if(Current.phase=="work_walk") {
            if(Game1.player.TilePoint!=target){MonitorWalk();return;}
            StopWalk();Adjacent(tile);Face(tile);
            if(workSkill is not ("harvest" or "forage"))SelectSlot(JsonSerializer.SerializeToElement(new{slot=workSlot}),true);
            if(workSkill is "harvest" or "forage")Game1.player.CurrentToolIndex=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is StardewValley.Tools.Hoe,-1);
            if(workSkill is "water" or "till" or "clear" or "chop" or "break_clump" or "clear_dead") {
                if(workSkill!="clear_dead" && Game1.player.Stamina<17 && Game1.player.CurrentTool?.isScythe()!=true)throw new InvalidOperationException("energy_reserve_reached");
                Game1.player.lastClick=tile.ToVector2()*64+new Vector2(32);Game1.player.BeginUsingTool();
                if(!Game1.player.UsingTool)throw new InvalidOperationException("work_tool_not_started");
            } else if(workSkill is "plant" or "fertilize") {
                if(Game1.player.ActiveObject is not {} seed)throw new InvalidOperationException("plant_seed_missing");
                ValidateConsumption?.Invoke(new Dictionary<Item,int>{{seed,1}},"",workSkill+":"+seed.QualifiedItemId);
                int beforeStack=Game1.player.Items.Where(i=>i?.QualifiedItemId==seed.QualifiedItemId).Sum(i=>i.Stack);
                if(!Utility.tryToPlaceItem(Game1.currentLocation,seed,tile.X*64,tile.Y*64))throw new InvalidOperationException("native_plant_rejected");
                if(beforeStack-Game1.player.Items.Where(i=>i?.QualifiedItemId==seed.QualifiedItemId).Sum(i=>i.Stack)!=1)throw new InvalidOperationException("native_seed_or_fertilizer_consumption_not_verified");
            } else if(!Game1.tryToCheckAt(tile.ToVector2(),Game1.player))throw new InvalidOperationException("native_harvest_rejected");
            Current.phase="work_impact";return;
        }
        if(Current.phase=="work_impact" && !Game1.player.UsingTool) {
            var after=TileState(tile);
            if(JsonSerializer.Serialize(workBefore)==JsonSerializer.Serialize(after))throw new InvalidOperationException("work_effect_not_observed");
            Current.effects.Add(new{before=workBefore,after});
            if(workSkill=="clear" && Game1.currentLocation.objects.ContainsKey(tile.ToVector2()) || workSkill=="chop"&&Game1.currentLocation.terrainFeatures.ContainsKey(tile.ToVector2()) || workSkill=="break_clump"&&ClumpAt(tile)!=null) {
                if(++workHits>=64)throw new InvalidOperationException("resource_hit_limit_replan");
                Current.phase="work_next";return;
            }
            if(workSkill is "clear" or "chop" or "break_clump" or "clear_dead" or "harvest" or "forage"){Current.phase="work_collect_start";return;}
            Current.completed++;workIndex++;workHits=0;retries=0;Current.phase="work_next";
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
            var warp=l.warps.FirstOrDefault(w=>w.X==edge.X&&w.Y==edge.Y&&NormalizeWarpTarget(w.TargetName)==edge.TargetName);
            if(warp!=null) {
                // Keep native path ownership through the boundary. A one-frame movement flag
                // can be cleared by the game's keyboard processing before the Farmer moves.
                var continuation=new Stack<Point>();continuation.Push(at);
                ownedController=new PathFindController(continuation,l,Game1.player,at);
                Game1.player.controller=ownedController;boundaryDriving=true;
                target=at;lastProgress=DateTime.UtcNow;lastTile=Game1.player.TilePoint;
            }
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("travel_menu_requires_choice");
        MonitorWalk();
    }
    private void TickNight() {
        if(Current!.phase=="waking" && Game1.player.CanMove && !Game1.fadeToBlack && Game1.activeClickableMenu==null){Finish("succeeded");return;}
        if((DateTime.UtcNow-started).TotalSeconds>240){Finish("failed","overnight_timeout_check_save");return;}
        if(Game1.activeClickableMenu is LevelUpMenu {isProfessionChooser:true} chooser&&ApplyProfession?.Invoke(chooser)==true){Current!.effects.Add(new{kind="native_profession_policy",professions=Game1.player.professions.ToArray()});return;}
        // LevelUpMenu.receiveLeftClick is empty in 1.6; ordinary confirmations use
        // its native handler. Actual profession choices must never be auto-confirmed.
        if(Game1.activeClickableMenu is LevelUpMenu {isProfessionChooser:false,isActive:true} level && level.CanReceiveInput()) {
            level.okButtonClicked();Current!.effects.Add(new{native_menu="LevelUpMenu",kind="non_branching_confirmation"});return;
        }
        // Native overnight announcements (e.g. the summer earthquake) block saving
        // until acknowledged. Questions/choices still belong to the model.
        if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} notice && DateTime.UtcNow>=nextInteraction) {
            nextInteraction=DateTime.UtcNow.AddMilliseconds(400);notice.finishTyping();
            Current!.effects.Add(new{native_menu="DialogueBox",kind="non_branching_overnight_notice",text=notice.getCurrentString()});
            notice.receiveLeftClick(notice.xPositionOnScreen+16,notice.yPositionOnScreen+16);return;
        }
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
        if(advanced&&saved)Current!.phase="waking";else Finish("failed","day_transition_not_verified");return advanced&&saved;
    }
    private void Finish(string status,string? error=null) {
        if(Current==null)return;
        if(Current.skill=="player.fish"&&status!="succeeded"&&Context.IsWorldReady) {
            try{ReleaseFishing();}catch{error=(error??status)+":fishing_release_needs_review";}
        }
        if(actionTargetBefore!=null && Context.IsWorldReady && Game1.currentLocation.NameOrUniqueName==origin) {
            var after=TileState(target);Current.effects.Add(new{before=actionTargetBefore,after,effect_observed=AgentJson.Encode(actionTargetBefore)!=AgentJson.Encode(after)});actionTargetBefore=null;
        }
        StopWalk();Current.status=status;Current.error=error;Current.phase=status;Current.after=Context.IsWorldReady?Snapshot():null;
    }
    public static IEnumerable<Warp> Exits(GameLocation location) {
        foreach(var warp in location.warps)if(!warp.npcOnly.Value)yield return warp.TargetName=="VolcanoEntrance"?new Warp(warp.X,warp.Y,NormalizeWarpTarget(warp.TargetName),warp.TargetX,warp.TargetY,false):warp;
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
    private static string NormalizeWarpTarget(string name)=>name=="VolcanoEntrance"?VolcanoDungeon.GetLevelName(0):name;
    // getLocationFromName generates new dungeon floors. Route search must never
    // instantiate future floors merely to inspect their outgoing edges.
    internal static GameLocation? LoadedLocation(string name) {
        if(VolcanoDungeon.IsGeneratedLevel(name))return VolcanoDungeon.activeLevels.FirstOrDefault(l=>l.NameOrUniqueName==name);
        if(MineShaft.IsGeneratedLevel(name))return MineShaft.activeMines.FirstOrDefault(l=>l.NameOrUniqueName==name);
        return Game1.getLocationFromName(name);
    }
    private static Warp? NextExit(GameLocation from,string destination) {
        var queue=new Queue<(GameLocation Location,Warp? First)>();queue.Enqueue((from,null));var visited=new HashSet<string>{from.NameOrUniqueName};
        while(queue.Count>0&&visited.Count<150) {
            var (l,first)=queue.Dequeue();foreach(var edge in Exits(l)) {
                if(edge.TargetName==destination)return first??edge;
                var next=LoadedLocation(edge.TargetName);if(next==null||!visited.Add(next.NameOrUniqueName))continue;
                if(next.NameOrUniqueName==destination)return first??edge;queue.Enqueue((next,first??edge));
            }
        }
        return null;
    }
}
