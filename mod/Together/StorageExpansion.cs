using Microsoft.Xna.Framework;
using System.Text.Json;
using StardewValley;
using StardewValley.Objects;

namespace Together;
public sealed partial class ModEntry {
    private void TickStorageSupport(SemanticJob job) {
        if(job.ExpansionTile.HasValue) {
            TickStorageExpansion(job);
            if(!job.ExpansionTile.HasValue){job.completed=1;StopSemanticWork(job,"shared_storage_expanded",true);}
            return;
        }
        if(!TryStartStorageExpansion(job))StopSemanticWork(job,"storage_expansion_requires_materials_capacity_and_budget");
    }
    private bool RequestCompanionStorageSupport(SemanticJob job) {
        if(job.StorageSupportId.Length>0) {
            if(!semanticJobs.TryGetValue(job.StorageSupportId,out var prior)){StopSemanticWork(job,"storage_support_receipt_missing");return true;}
            if(prior.status=="running"){job.phase="waiting_player_storage_support";return true;}
            job.evidence.Add(new{kind="player_storage_support",command_id=prior.command_id,status=prior.status,reason=prior.stop_reason});job.StorageSupportId="";
            if(prior.status!="succeeded"){StopSemanticWork(job,"player_storage_support_failed:"+prior.stop_reason);return true;}
        }
        var policy=Data.Storage;if(!policy.AutoExpand||SharedStorage().Count()>=policy.MaxSharedChests||job.StorageSupportAttempts>=4)return false;
        // Reuse one active support job across companions. It owns only the Farmer;
        // the companion retains its original work and resumes after unloading.
        var active=semanticJobs.Values.FirstOrDefault(j=>j.actor=="player"&&j.status=="running"&&j.StorageSupportFor.Length>0);
        if(active!=null){job.StorageSupportId=active.command_id;job.phase="waiting_player_storage_support";return true;}
        if(playerExecutor.Busy||WorkActorBusy("player")){job.phase="waiting_player_available_for_storage";return true;}
        if(Game1.activeClickableMenu!=null||Game1.eventUp||Game1.timeOfDay>=2100)return false;
        var recipe=Game1.player.craftingRecipes.ContainsKey("Chest")?new CraftingRecipe("Chest",false):null;
        if(recipe==null||recipe.recipeList.Count!=1||!recipe.recipeList.TryGetValue("388",out int cost))return false;
        int reserved=policy.BudgetDay==Game1.Date.TotalDays?policy.WoodReserved:0;
        bool hasChest=Game1.player.Items.Any(i=>i?.QualifiedItemId=="(BC)130");if(!hasChest&&policy.WoodBudgetPerDay-reserved<cost)return false;
        int missing=Math.Max(0,cost-Game1.player.Items.Where(i=>i?.QualifiedItemId=="(O)388").Sum(i=>i.Stack));
        object args;
        if(hasChest||missing==0)args=new{goal="storage_expand",until=2200};
        else {
            int stored=SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer()).Where(i=>i?.QualifiedItemId=="(O)388").Sum(i=>i.Stack);
            if(stored>0)args=new{goal="withdraw",item="(O)388",count=Math.Min(missing,stored),until=2200};
            else if(job.actor.EndsWith(":"+PartnerName)&&CargoItem(WorkActor(job.actor),"(O)388") is >0 and var carried)args=new{goal="withdraw",item="(O)388",count=Math.Min(missing,carried),until=2200};
            else {
                if(!Game1.player.couldInventoryAcceptThisItem(ItemRegistry.Create("(O)388"))){StopSemanticWork(job,"storage_support_needs_player_pickup_space_or_existing_wood");return true;}
                args=new{goal="wood",location="Farm",count=missing,include_trees=true,until=2200};
            }
        }
        var helper=(SemanticJob)StartSemanticWork(JsonSerializer.SerializeToElement(args));helper.StorageSupportFor=job.command_id;job.StorageSupportId=helper.command_id;job.StorageSupportAttempts++;job.phase="waiting_player_storage_support";
        Data.Autoplay.Record("storage_support_requested",AgentJson.Encode(new{companion=job.actor,work=job.command_id,helper=helper.command_id,goal=helper.goal}));return true;
    }
    private bool TryStartStorageExpansion(SemanticJob job) {
        var policy=Data.Storage;
        if(!policy.AutoExpand||SharedStorage().Count()>=Math.Clamp(policy.MaxSharedChests,0,32))return false;
        if(Game1.currentLocation!=Game1.getFarm()){WorkChild(job,"player.travel",new{location="Farm"},"storage_expansion_travel");return true;}
        int chest=WorkSlot(i=>i.QualifiedItemId=="(BC)130");
        if(chest<0) {
            if(policy.BudgetDay!=Game1.Date.TotalDays){policy.BudgetDay=Game1.Date.TotalDays;policy.WoodReserved=0;}
            var recipe=Game1.player.craftingRecipes.ContainsKey("Chest")?new CraftingRecipe("Chest",false):null;
            if(recipe==null||recipe.recipeList.Count!=1||!recipe.recipeList.TryGetValue("388",out int woodCost)||woodCost<=0)return false;
            if(policy.WoodReserved+woodCost>Math.Clamp(policy.WoodBudgetPerDay,0,999))return false;
            int missing=Math.Max(0,woodCost-Game1.player.Items.Where(i=>i?.QualifiedItemId=="(O)388").Sum(i=>i.Stack));
            if(missing>0&&ReceivePartnerCargo(job,"(O)388",missing))return true;
            if(!recipe.doesFarmerHaveIngredientsInInventory())return false;
            CompactPlayerStacks();
            if(!Game1.player.Items.Any(i=>i==null))throw new InvalidOperationException("storage_expansion_requires_one_crafting_slot");
            // Conservative budget reservation survives interrupted crafting. Native
            // production still checks all shared-goal material reservations.
            policy.WoodReserved+=woodCost;WorkChild(job,"player.craft",new{recipe="Chest",count=1},"storage_expansion_craft");return true;
        }
        var farm=Game1.getFarm();var grid=new List<LayoutCell>();var anchors=PlayerExecutor.Exits(farm).Select(e=>new FarmCell(e.X,e.Y)).ToList();
        foreach(var b in farm.buildings)if(b.humanDoor.Value.X>=0)anchors.Add(new(b.tileX.Value+b.humanDoor.Value.X,b.tileY.Value+b.humanDoor.Value.Y+1));
        foreach(var entry in farm.objects.Pairs.Where(x=>x.Value.bigCraftable.Value))if(WorkStand(farm,entry.Key.ToPoint()) is {} stand)anchors.Add(new(stand.X,stand.Y));
        foreach(var area in Data.FarmPolicy.Areas.Where(a=>a.Enabled&&a.Location=="Farm"))for(int y=area.Y;y<area.Y+area.Height;y++)for(int x=area.X;x<area.X+area.Width;x++)anchors.Add(new(x,y));
        for(int y=0;y<farm.Map.Layers[0].LayerHeight;y++)for(int x=0;x<farm.Map.Layers[0].LayerWidth;x++)grid.Add(new(new(x,y),false,PlayerExecutor.Passable(farm,new(x,y)),false,false,false,false,0));
        var start=new FarmCell(Game1.player.TilePoint.X,Game1.player.TilePoint.Y);
        foreach(var cell in grid.Where(c=>c.Passable&&c.Tile!=start).OrderBy(c=>Math.Abs(c.Tile.X-start.X)+Math.Abs(c.Tile.Y-start.Y))) {
            var p=new Point(cell.Tile.X,cell.Tile.Y);var v=p.ToVector2();
            if(IsPlacementProtected("Farm",p)||farm.objects.ContainsKey(v)||farm.terrainFeatures.ContainsKey(v)||farm.doesTileHaveProperty(p.X,p.Y,"Action","Buildings")!=null||farm.doesTileHaveProperty(p.X,p.Y,"TouchAction","Back")!=null||anchors.Contains(cell.Tile))continue;
            if(!Game1.player.Items[chest].canBePlacedHere(farm,v))continue;
            var stand=WorkStand(farm,p);if(!stand.HasValue||!FarmLayout.KeepsAccess(grid,start,anchors,new[]{cell.Tile},new[]{new FarmCell(stand.Value.X,stand.Value.Y)}))continue;
            job.ExpansionTile=p;WorkChild(job,"player.move",new{x=stand.Value.X,y=stand.Value.Y},"storage_expansion_move");return true;
        }
        throw new InvalidOperationException("storage_no_clear_site_with_preserved_access");
    }
    private void TickStorageExpansion(SemanticJob job) {
        var tile=job.ExpansionTile!.Value;var farm=Game1.getFarm();
        if(Game1.currentLocation!=farm)throw new InvalidOperationException("storage_expansion_location_changed");
        if(job.ChildKind=="storage_expansion_place") {
            if(!farm.objects.TryGetValue(tile.ToVector2(),out var placed)||placed is not Chest chest||!chest.playerChest.Value||chest.QualifiedItemId!="(BC)130")throw new InvalidOperationException("native_storage_placement_not_verified");
            chest.modData[WorkChestRole]="output";job.evidence.Add(new{kind="native_shared_storage_expanded",location="Farm",tile,item=chest.QualifiedItemId});job.ExpansionTile=null;job.StorageTile=tile;job.StorageLocation="Farm";return;
        }
        int slot=WorkSlot(i=>i.QualifiedItemId=="(BC)130");if(slot<0)throw new InvalidOperationException("crafted_chest_missing");
        if(IsPlacementProtected("Farm",tile)||farm.objects.ContainsKey(tile.ToVector2())||farm.terrainFeatures.ContainsKey(tile.ToVector2()))throw new InvalidOperationException("storage_placement_site_changed");
        WorkChild(job,"player.place",new{x=tile.X,y=tile.Y,slot},"storage_expansion_place");
    }
    private static void CompactPlayerStacks() {
        var bag=Game1.player.Items;
        for(int i=0;i<bag.Count;i++)if(bag[i] is {} target)for(int j=i+1;j<bag.Count;j++)if(bag[j] is {} source&&target.canStackWith(source)&&target.Stack<target.maximumStackSize()) {
            source.Stack=target.addToStack(source);if(source.Stack==0)bag[j]=null;
        }
    }
}
