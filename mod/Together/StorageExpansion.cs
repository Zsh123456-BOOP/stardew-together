using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace Together;
public sealed partial class ModEntry {
    private bool TryStartStorageExpansion(SemanticJob job) {
        var policy=Data.Storage;
        if(!policy.AutoExpand||SharedStorage().Count()>=Math.Clamp(policy.MaxSharedChests,0,32))return false;
        if(Game1.currentLocation!=Game1.getFarm()){WorkChild(job,"player.travel",new{location="Farm"},"storage_expansion_travel");return true;}
        int chest=WorkSlot(i=>i.QualifiedItemId=="(BC)130");
        if(chest<0) {
            if(policy.BudgetDay!=Game1.Date.TotalDays){policy.BudgetDay=Game1.Date.TotalDays;policy.WoodReserved=0;}
            var recipe=Game1.player.craftingRecipes.ContainsKey("Chest")?new CraftingRecipe("Chest",false):null;
            if(recipe==null||recipe.recipeList.Count!=1||!recipe.recipeList.TryGetValue("388",out int woodCost)||woodCost<=0||!recipe.doesFarmerHaveIngredientsInInventory())return false;
            if(policy.WoodReserved+woodCost>Math.Clamp(policy.WoodBudgetPerDay,0,999))return false;
            CompactPlayerStacks();
            if(!Game1.player.Items.Any(i=>i==null))throw new InvalidOperationException("storage_expansion_requires_one_crafting_slot_keep_two_free");
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
            if(farm.objects.ContainsKey(v)||farm.terrainFeatures.ContainsKey(v)||farm.doesTileHaveProperty(p.X,p.Y,"Action","Buildings")!=null||farm.doesTileHaveProperty(p.X,p.Y,"TouchAction","Back")!=null||anchors.Contains(cell.Tile))continue;
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
        if(farm.objects.ContainsKey(tile.ToVector2())||farm.terrainFeatures.ContainsKey(tile.ToVector2()))throw new InvalidOperationException("storage_placement_site_changed");
        WorkChild(job,"player.place",new{x=tile.X,y=tile.Y,slot},"storage_expansion_place");
    }
    private static void CompactPlayerStacks() {
        var bag=Game1.player.Items;
        for(int i=0;i<bag.Count;i++)if(bag[i] is {} target)for(int j=i+1;j<bag.Count;j++)if(bag[j] is {} source&&target.canStackWith(source)&&target.Stack<target.maximumStackSize()) {
            source.Stack=target.addToStack(source);if(source.Stack==0)bag[j]=null;
        }
    }
}
