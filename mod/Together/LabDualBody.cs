using System.Reflection;
using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private static object DualBodyMetadata() {
        var property=typeof(Game1).GetProperty("player",BindingFlags.Public|BindingFlags.NonPublic|BindingFlags.Static)!;
        var field=typeof(Game1).GetField("_player",BindingFlags.NonPublic|BindingFlags.Static);
        return new {assembly=typeof(Game1).Assembly.FullName,public_set=property.GetSetMethod()!=null,
            nonpublic_set=property.GetSetMethod(true)?.ToString(),field=field?.ToString(),
            field_private=field?.IsPrivate,field_static=field?.IsStatic,field_readonly=field?.IsInitOnly,
            field_matches_player=ReferenceEquals(field?.GetValue(null),Game1.player),farmer_sealed=typeof(Farmer).IsSealed};
    }
}

// No overrides: exercise the native Farmer implementations, including GetToolLocation.
internal sealed class BotFarmer:Farmer { }
public sealed partial class ModEntry {
    private BotFarmer? dualFarmer;
    private Farmer? dualPlayer;
    private NPC? dualNpc;
    private string dualChain="",dualPhase="",dualError="";
    private string DualFolder=>Path.Combine(Helper.DirectoryPath,"dual-body-evidence");
    private int dualStarted,dualChanged;
    private int dualRenders,dualMoves,dualHits,dualWoodProduced,dualWoodCollected;
    private Microsoft.Xna.Framework.Vector2 dualPrevious;
    private StardewValley.TerrainFeatures.Tree? dualTree;
    private readonly HashSet<Debris> dualOldDebris=new();
    private readonly HashSet<Debris> dualDrops=new();
    private readonly Dictionary<Debris,int> dualWoodGroups=new();
    private readonly Dictionary<Chunk,int> dualChunkIds=new();
    private Chunk? dualChunk;
    private Debris? dualChunkDebris;
    private int dualChunkStarted;
    private object PickupDiagnostic() {
        var b=dualFarmer;var p=dualChunk?.position.Value;
        return new{produced=dualWoodProduced,collected=dualWoodCollected,remaining=dualWoodProduced-dualWoodCollected,
            chunk_id=dualChunk==null?0:dualChunkIds.GetValueOrDefault(dualChunk),chunk=p==null?null:new[]{p.Value.X,p.Value.Y},
            standing=b==null?null:new[]{b.StandingPixel.X,b.StandingPixel.Y},
            dx=p==null||b==null?(float?)null:Math.Abs(p.Value.X+32-b.StandingPixel.X),dy=p==null||b==null?(float?)null:Math.Abs(p.Value.Y+32-b.StandingPixel.Y),limit=64,
            npc=dualNpc==null?null:new[]{dualNpc.Position.X,dualNpc.Position.Y},chunk_started=dualChunkStarted,tick=Game1.ticks,
            outstanding=dualWoodGroups.Keys.Select(d=>new{in_world=Game1.getFarm().debris.Contains(d),chunks=d.Chunks.Select(c=>new{id=dualChunkIds.GetValueOrDefault(c),position=new[]{c.position.X,c.position.Y}}).ToArray()}).ToArray()};
    }
    private string dualPlayerInventory="",dualPlayerRecipes="";
    private object? dualBefore,dualAfter;
    private bool dualCapture;
    private static object Body(Farmer f)=>new {f.Name,id=f.UniqueMultiplayerID,position=new[]{f.Position.X,f.Position.Y},tile=new[]{f.TilePoint.X,f.TilePoint.Y},f.FacingDirection,location=f.currentLocation?.NameOrUniqueName,
        items=f.Items.Select((i,n)=>new{slot=n,id=i?.QualifiedItemId,count=i?.Stack??0,quality=i?.Quality??0}).ToArray(),recipes=f.craftingRecipes.Pairs.ToDictionary(p=>p.Key,p=>p.Value),stamina=f.Stamina,experience=f.experiencePoints.ToArray(),trees_chopped=f.stats.Get("TreesChopped")};
    private static string Inventory(Farmer f)=>JsonSerializer.Serialize(f.Items.Select((i,n)=>new{slot=n,id=i?.QualifiedItemId,count=i?.Stack??0,quality=i?.Quality??0}));
    private static string Recipes(Farmer f)=>JsonSerializer.Serialize(f.craftingRecipes.Pairs.OrderBy(p=>p.Key).ToDictionary(p=>p.Key,p=>p.Value));
    private static string CombinedItems(Farmer a,Farmer b)=>JsonSerializer.Serialize(a.Items.Concat(b.Items).Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId+":"+i.Quality).OrderBy(g=>g.Key).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack)));
    private static int Wood(Farmer f)=>f.Items.Where(i=>i?.QualifiedItemId=="(O)388").Sum(i=>i.Stack);
    private void DualLog(string kind,object value) {
        Directory.CreateDirectory(DualFolder);
        File.AppendAllText(Path.Combine(DualFolder,dualChain+".jsonl"),JsonSerializer.Serialize(new{tick=Game1.ticks,kind,value})+"\n");
    }
    private string ReadDualBodyProbe()=>JsonSerializer.Serialize(new{chain=dualChain,phase=dualPhase,error=dualError,tick=Game1.ticks,started=dualStarted,renders=dualRenders,movement_frames=dualMoves,hits=dualHits,wood_produced=dualWoodProduced,wood_collected=dualWoodCollected,
        player_restored=dualPlayer==null||ReferenceEquals(Game1.player,dualPlayer),player=dualPlayer==null?null:Body(dualPlayer),hidden=dualFarmer==null?null:Body(dualFarmer),before=dualBefore,after=dualAfter});
    private void DualFail(Exception error) {
        dualError=error.ToString();dualPhase="failed";Game1.paused=true;
        DualLog("FAILED",new{error=dualError,player_restored=ReferenceEquals(Game1.player,dualPlayer),pickup=dualChain=="A"?PickupDiagnostic():null});
        File.WriteAllText(Path.Combine(DualFolder,dualChain+"-result.json"),ReadDualBodyProbe());
        Monitor.Log("Dual body spike stopped: "+dualError,StardewModdingAPI.LogLevel.Error);
    }
    private string StartDualBodyProbe(string chain) {
        if(!Settings.EnableLab||!StardewModdingAPI.Context.IsWorldReady||Game1.player.Name!="AgentLab"||StardewModdingAPI.Context.IsMultiplayer)throw new InvalidOperationException("isolated_lab_required");
        if(chain is not ("A" or "B")||dualPhase=="failed"||dualPhase!=""&&dualPhase!="passed")throw new InvalidOperationException("probe_start_not_allowed");
        PauseAutoplay("dual_body_spike");Settings.Autonomy=false;
        // The pre-existing Together panel is not a native interaction; do not close a native menu for the probe.
        if(Game1.activeClickableMenu is AutoplayMenu)Game1.exitActiveMenu();
        if(Game1.activeClickableMenu!=null||Game1.eventUp)throw new InvalidOperationException("close_native_interaction_before_probe");
        dualChain=chain;dualPhase="prepare";dualError="";dualStarted=dualChanged=Game1.ticks;dualRenders=dualMoves=dualHits=0;dualBefore=dualAfter=null;dualWoodProduced=dualWoodCollected=0;dualWoodGroups.Clear();dualChunkIds.Clear();dualChunk=null;dualChunkDebris=null;dualChunkStarted=0;
        dualPlayer=Game1.player;dualFarmer=new BotFarmer{Name="小禾隐形身体",UniqueMultiplayerID=Helper.Multiplayer.GetNewID(),MaxItems=12};
        while(dualFarmer.Items.Count<dualFarmer.MaxItems)dualFarmer.Items.Add(null);
        Directory.CreateDirectory(DualFolder);DualLog("metadata",DualBodyMetadata());
        return ReadDualBodyProbe();
    }
    private bool TickDualBodyProbe() {
        if(dualPhase=="")return false;
        if(!Settings.EnableLab||!StardewModdingAPI.Context.IsWorldReady||dualPlayer?.Name!="AgentLab")return false;
        if(dualPhase=="failed")return true;
        try {
            if(!ReferenceEquals(Game1.player,dualPlayer))throw new InvalidOperationException("FATAL_player_reference_changed_outside_scope");
            if(dualChain=="A"&&dualNpc!=null&&dualFarmer!=null) {
                dualFarmer.Position=dualNpc.Position;dualFarmer.FacingDirection=dualNpc.FacingDirection;dualFarmer.currentLocation=dualNpc.currentLocation;
                if(dualNpc.Position!=dualPrevious){dualMoves++;dualPrevious=dualNpc.Position;}
                if(dualPhase!="passed")DualLog("sync",new{npc=new[]{dualNpc.Position.X,dualNpc.Position.Y},hidden=new[]{dualFarmer.Position.X,dualFarmer.Position.Y},facing=dualNpc.FacingDirection,hidden_facing=dualFarmer.FacingDirection,location=dualNpc.currentLocation.NameOrUniqueName,same_location=ReferenceEquals(dualFarmer.currentLocation,dualNpc.currentLocation),phase=dualPhase});
            }
            if(dualPhase=="passed")return true;
            if(Game1.ticks-dualStarted>7200)throw new InvalidOperationException("probe_timeout_120_seconds");
            if(Game1.activeClickableMenu!=null||Game1.eventUp)throw new InvalidOperationException("native_interaction_interrupted_probe");
            if(dualChain=="B")TickDualCraft();else TickDualTree();
        }catch(Exception ex){DualFail(ex);}
        return true;
    }
    private void TickDualCraft() {
        var bot=dualFarmer!;var player=dualPlayer!;
        if(dualPhase=="prepare") {
            bot.Position=player.Position;bot.FacingDirection=player.FacingDirection;bot.currentLocation=player.currentLocation;
            // Explicit material fixture only. Default recipe knowledge comes from Farmer's own constructor.
            bot.addItemToInventory(ItemRegistry.Create("(O)388",50));
            dualPlayerInventory=Inventory(player);dualPlayerRecipes=Recipes(player);
            dualBefore=new{player=Body(player),hidden=Body(bot),menu=Game1.activeClickableMenu?.GetType().Name};DualLog("before_craft",dualBefore);
            dualCapture=true;dualPhase="craft";dualChanged=Game1.ticks;return;
        }
        if(dualPhase=="craft"&&dualRenders>0) {
            var viewport=Game1.viewport;var location=Game1.currentLocation;var renderer=player.FarmerRenderer;
            int count=bot.craftingRecipes["Chest"];
            using(ActingFarmer.As(bot)) {
                var menu=new StardewValley.Menus.CraftingPage(0,0,928,728,cooking:false,standaloneMenu:true);
                // Synchronous private native handler, never publish this menu to player interaction/rendering.
                var method=typeof(StardewValley.Menus.CraftingPage).GetMethod("clickCraftingRecipe",BindingFlags.Instance|BindingFlags.NonPublic)??throw new InvalidOperationException("native_crafting_handler_changed");
                var page=menu.pagesOfCraftingRecipes.Select((p,n)=>new{recipes=p,index=n}).Single(p=>p.recipes.Any(x=>x.Value.name=="Chest"));
                menu.currentCraftingPage=page.index;var choice=page.recipes.Single(p=>p.Value.name=="Chest");
                method.Invoke(menu,new object[]{choice.Key,false});
                if(menu.heldItem!=null)menu.heldItem=bot.addItemToInventory(menu.heldItem);
                if(menu.heldItem!=null)throw new InvalidOperationException("native_craft_output_not_stored");
                DualLog("inside_scope",new{is_hidden=ReferenceEquals(Game1.player,bot),player=Body(player),hidden=Body(bot)});
            }
            if(!ReferenceEquals(Game1.player,player))throw new InvalidOperationException("FATAL_player_not_restored");
            if(Inventory(player)!=dualPlayerInventory||Recipes(player)!=dualPlayerRecipes)throw new InvalidOperationException("player_inventory_or_crafting_counts_changed");
            if(Wood(bot)!=0||bot.Items.Where(i=>i?.QualifiedItemId=="(BC)130").Sum(i=>i.Stack)!=1||bot.craftingRecipes["Chest"]!=count+1)throw new InvalidOperationException("hidden_crafting_not_native_verified");
            if(!Game1.viewport.Equals(viewport)||!ReferenceEquals(Game1.currentLocation,location)||!ReferenceEquals(player.FarmerRenderer,renderer)||Game1.activeClickableMenu!=null)throw new InvalidOperationException("render_or_menu_identity_changed");
            dualAfter=new{player=Body(player),hidden=Body(bot),same_renderer=true,same_viewport=true,same_location=true,scope_tick=Game1.ticks};
            DualLog("after_scope",dualAfter);dualPhase="verify_tick";dualChanged=Game1.ticks;dualCapture=true;return;
        }
        if(dualPhase=="verify_tick"&&Game1.ticks-dualChanged>=2&&dualRenders>=3) {
            if(Inventory(player)!=dualPlayerInventory||Recipes(player)!=dualPlayerRecipes)throw new InvalidOperationException("next_tick_player_changed");
            DualPass();
        }
    }
    private void DualWalk(Microsoft.Xna.Framework.Point target) {
        api!.LabDualBodyWalk(PartnerName,target.X,target.Y,true);
    }
    private void TickDualTree() {
        var bot=dualFarmer!;var player=dualPlayer!;var farm=Game1.getFarm();
        var treeTile=new Microsoft.Xna.Framework.Vector2(50,25);var stand=new Microsoft.Xna.Framework.Point(50,26);
        if(dualPhase=="prepare") {
            // Disposable AgentLab fixture: clear the small test patch, place one mature tree and both starting bodies.
            var area=new Microsoft.Xna.Framework.Rectangle(39,19,20,14);
            foreach(var p in farm.objects.Keys.Where(p=>area.Contains(p.ToPoint())).ToArray())farm.objects.Remove(p);
            foreach(var p in farm.terrainFeatures.Keys.Where(p=>area.Contains(p.ToPoint())).ToArray())farm.terrainFeatures.Remove(p);
            foreach(var c in farm.resourceClumps.Where(c=>new Microsoft.Xna.Framework.Rectangle(area.X*64,area.Y*64,area.Width*64,area.Height*64).Intersects(c.getBoundingBox())).ToArray())farm.resourceClumps.Remove(c);
            Game1.warpFarmer("Farm",42,28,false);
            Data.Partner.Enabled=true;EnsureCustomPartner();dualNpc=api!.GetCharacter(PartnerName)??throw new InvalidOperationException("custom_partner_missing");
            Game1.warpCharacter(dualNpc,farm,new Microsoft.Xna.Framework.Vector2(45,26));dualPrevious=dualNpc.Position;
            dualTree=new StardewValley.TerrainFeatures.Tree("1",5);farm.terrainFeatures[treeTile]=dualTree;
            bot.addItemToInventory(new StardewValley.Tools.Axe());PlayerSelection.Set(bot,0);
            dualOldDebris.Clear();dualDrops.Clear();foreach(var d in farm.debris)dualOldDebris.Add(d);
            dualPlayerInventory=Inventory(player);dualBefore=new{player=Body(player),hidden=Body(bot),tree=new[]{50,25},fixture="one native mature tree; no wood added to either inventory"};DualLog("before_tree",dualBefore);
            dualPhase="walk_tree";DualWalk(stand);dualCapture=true;return;
        }
        var npc=dualNpc!;
        foreach(var d in farm.debris)if(!dualOldDebris.Contains(d)&&dualDrops.Add(d)&&d.itemId.Value is "388" or "(O)388") {
            foreach(var c in d.Chunks)dualChunkIds[c]=dualChunkIds.Count+1;
            int units=d.item?.Stack??d.Chunks.Count;dualWoodProduced+=units;dualWoodGroups[d]=units;
            DualLog("wood_spawn_observed",new{group=dualWoodGroups.Count,units,chunks=d.Chunks.Count,item=d.itemId.Value,type=d.debrisType.Value.ToString(),positions=d.Chunks.Select(c=>new[]{c.position.X,c.position.Y}).ToArray(),total=dualWoodProduced});
        }
        if(dualPhase=="walk_tree") {
            if(npc.TilePoint!=stand)return;
            api!.LabDualBodyWalk(PartnerName,0,0,false);npc.faceDirection(0);bot.FacingDirection=npc.FacingDirection;bot.Position=npc.Position;bot.currentLocation=npc.currentLocation;
            dualPhase="chop";dualChanged=Game1.ticks;return;
        }
        if(dualPhase=="chop") {
            if(dualTree!.stump.Value){dualPhase="falling";return;}
            if(Game1.ticks-dualChanged<30)return;dualChanged=Game1.ticks;
            Microsoft.Xna.Framework.Vector2 at;float before=dualTree.health.Value;
            int scopeTick=Game1.ticks;
            using(ActingFarmer.As(bot)) {
                at=bot.GetToolLocation(true);
                if((at/64).ToPoint()!=treeTile.ToPoint()||bot.Position!=npc.Position||bot.FacingDirection!=npc.FacingDirection||!ReferenceEquals(bot.currentLocation,npc.currentLocation))throw new InvalidOperationException("tool_coordinate_does_not_match_synchronized_body");
                bot.CurrentTool.DoFunction(bot.currentLocation,(int)at.X,(int)at.Y,1,bot);
            }
            DualLog("atomic_scope",new{operation="axe",enter_tick=scopeTick,exit_tick=Game1.ticks,restored=ReferenceEquals(Game1.player,player)});dualHits++;
            DualLog("axe_impact",new{npc=new[]{npc.Position.X,npc.Position.Y},hidden=new[]{bot.Position.X,bot.Position.Y},facing=npc.FacingDirection,tool_pixel=new[]{at.X,at.Y},tool_tile=new[]{(int)at.X/64,(int)at.Y/64},tree_health_before=before,tree_health_after=dualTree.health.Value,stump=dualTree.stump.Value,player_wood=Wood(player),hidden_wood=Wood(bot)});
            return;
        }
        if(dualPhase=="falling") {if(dualTree!.falling.Value)return;dualPhase="pickup";dualChanged=Game1.ticks;DualLog("tree_fallen",new{stump=dualTree.stump.Value,falling=dualTree.falling.Value});return;}
        if(dualPhase=="pickup") {
            var pending=dualDrops.Where(d=>farm.debris.Contains(d)&&d.Chunks.Count>0&&(d.itemId.Value is "388" or "(O)388")).ToArray();
            if(pending.Length==0) {
                if(Wood(bot)<=0)throw new InvalidOperationException("fallen_tree_produced_no_collectable_wood");
                if(Inventory(player)!=dualPlayerInventory)throw new InvalidOperationException("wood_reached_player_before_handoff");
                if(dualWoodProduced!=dualWoodCollected||Wood(bot)!=dualWoodProduced)throw new InvalidOperationException($"wood_yield_mismatch_produced_{dualWoodProduced}_collected_{dualWoodCollected}_bag_{Wood(bot)}");
                DualLog("wood_yield_verified",new{produced=dualWoodProduced,collected=dualWoodCollected,hidden=Wood(bot),difference=dualWoodProduced-Wood(bot),groups=dualWoodGroups.Values.ToArray()});
                DualLog("hidden_inventory_before_return",new{player=Body(player),hidden=Body(bot)});
                dualPhase="return";DualWalk(new(43,28));return;
            }
            if(dualChunk==null) {
                dualChunkDebris=pending.OrderBy(d=>Microsoft.Xna.Framework.Vector2.Distance(d.Chunks[0].position.Value,npc.Position)).First();
                dualChunk=dualChunkDebris.Chunks[0];dualChunkStarted=Game1.ticks;
                DualLog("pickup_target",PickupDiagnostic());
            }
            var debris=dualChunkDebris!;var chunk=dualChunk;var p=chunk.position.Value;
            if(!farm.debris.Contains(debris)||!debris.Chunks.Contains(chunk))throw new InvalidOperationException("tracked_chunk_disappeared: "+JsonSerializer.Serialize(PickupDiagnostic()));
            if(Game1.ticks-dualChunkStarted>900)throw new InvalidOperationException("chunk_approach_timeout: "+JsonSerializer.Serialize(PickupDiagnostic()));
            if(Game1.ticks%30==0)DualLog("pickup_distance",PickupDiagnostic());
            if(Math.Abs(p.X+32-bot.StandingPixel.X)>64||Math.Abs(p.Y+32-bot.StandingPixel.Y)>64) {
                api!.LabDualBodyCollectWalk(PartnerName,p.X+32,p.Y+32,bot.StandingPixel.X-(int)bot.Position.X,bot.StandingPixel.Y-(int)bot.Position.Y);return;
            }
            api!.LabDualBodyWalk(PartnerName,0,0,false);
            DualLog("pickup_in_native_range",PickupDiagnostic());
            int before=Wood(bot);var chunkPos=new[]{p.X,p.Y};
            int scopeTick=Game1.ticks;bool collected;
            using(ActingFarmer.As(bot))collected=debris.collect(bot,chunk);
            DualLog("atomic_scope",new{operation="collect",enter_tick=scopeTick,exit_tick=Game1.ticks,restored=ReferenceEquals(Game1.player,player),collected});
            if(!collected)throw new InvalidOperationException("native_debris_collect_rejected");
            dualWoodCollected+=Wood(bot)-before;
            debris.Chunks.Remove(chunk);if(debris.Chunks.Count==0)farm.debris.Remove(debris);
            DualLog("native_collect",new{item=debris.itemId.Value,chunk_position=chunkPos,hidden_before=before,hidden_after=Wood(bot),player_wood=Wood(player),chunk_id=dualChunkIds[chunk]});dualChunk=null;dualChunkDebris=null;return;
        }
        if(dualPhase=="return") {
            if(npc.TilePoint!=new Microsoft.Xna.Framework.Point(43,28))return;
            api!.LabDualBodyWalk(PartnerName,0,0,false);
            int beforeBot=Wood(bot),beforePlayer=Wood(player);
            var beforeItems=new{player=Body(player),hidden=Body(bot)};string combinedBefore=CombinedItems(player,bot);
            for(int i=0;i<bot.Items.Count;i++)if(bot.Items[i]?.QualifiedItemId=="(O)388") {
                var stack=bot.Items[i];bot.Items[i]=null;bot.Items[i]=player.addItemToInventory(stack);
            }
            int afterBot=Wood(bot),afterPlayer=Wood(player);
            dualAfter=new{before=beforeItems,after=new{player=Body(player),hidden=Body(bot)},all_items_before=combinedBefore,all_items_after=CombinedItems(player,bot),wood_total_before=beforeBot+beforePlayer,wood_total_after=afterBot+afterPlayer,transferred=afterPlayer-beforePlayer,npc_tile=new[]{npc.TilePoint.X,npc.TilePoint.Y},player_tile=new[]{player.TilePoint.X,player.TilePoint.Y}};
            DualLog("handoff",dualAfter);
            if(combinedBefore!=CombinedItems(player,bot)||beforeBot<=0||afterBot!=0||afterPlayer-beforePlayer!=beforeBot||beforeBot+beforePlayer!=afterBot+afterPlayer||dualMoves==0)throw new InvalidOperationException("handoff_conservation_or_walk_failed");
            dualCapture=true;DualPass();
        }
    }
    private void DualPass() {dualPhase="passed";DualLog("PASS",new{reference_restored=ReferenceEquals(Game1.player,dualPlayer),renders=dualRenders,movement_frames=dualMoves,hits=dualHits});File.WriteAllText(Path.Combine(DualFolder,dualChain+"-result.json"),ReadDualBodyProbe());}
    private void RenderDualBodyProbe() {
        if(dualPhase==""||dualPhase=="failed"||dualPlayer==null)return;
        try {
            if(!ReferenceEquals(Game1.player,dualPlayer))throw new InvalidOperationException("FATAL_render_saw_wrong_player");
            dualRenders++;
            if(dualPhase!="passed")DualLog("render_identity",new{player_id=Game1.player.UniqueMultiplayerID,expected_id=dualPlayer.UniqueMultiplayerID,menu=Game1.activeClickableMenu?.GetType().Name});
            if(dualCapture&&Game1.game1.screen is {} screen) {
                using var output=File.Create(Path.Combine(DualFolder,$"{dualChain}-{dualPhase}-{Game1.ticks}.png"));screen.SaveAsPng(output,screen.Width,screen.Height);dualCapture=false;
            }
        }catch(Exception ex){DualFail(ex);}
    }
}
