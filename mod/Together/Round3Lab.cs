using System.Reflection;
using System.Text.Json;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class ModEntry {
    private bool labEarlyStorage;
    private object Round3NativeFixture(string mode) {
        if(!Settings.EnableLab||!Context.IsWorldReady||Game1.player.Name!="AgentLab")throw new InvalidOperationException("isolated_lab_required");
        if(mode=="bootstrap"){labEarlyStorage=true;return new{armed=true,note="only removes probe suppression of existing authorized storage; no native stock changes"};}
        if(mode=="read")return new{snapshot=AgentSnapshot(),inventory=AgentToolRegistry.Inventory(),candidates=OperatingOpportunities(),policy=Data.Business,routine=Data.Autoplay.Routine,goals=Data.SharedGoals,stores=SharedStorage().Select(s=>new{location=s.Location.NameOrUniqueName,tile=new[]{(int)s.Tile.X,(int)s.Tile.Y},items=KitInventoryEvidence(s.Chest)}),farm=Game1.getFarm().terrainFeatures.Pairs.Where(p=>p.Value is StardewValley.TerrainFeatures.HoeDirt {crop:not null}).Select(p=>new{tile=new[]{(int)p.Key.X,(int)p.Key.Y},water=((StardewValley.TerrainFeatures.HoeDirt)p.Value).state.Value})};
        if(playerExecutor.Busy)throw new InvalidOperationException("lab_requires_idle");
        if(mode is "pack" or "unpack") {
            if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("lab_requires_closed_menu");
            var bag=Game1.player.Items;var before=AgentToolRegistry.Inventory();
            var ui=new InventoryMenu(0,0,true,bag,capacity:Game1.player.MaxItems,rows:3);
            var totals=bag.Where(i=>i!=null).GroupBy(i=>(i.QualifiedItemId,i.Quality)).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
            int moves=0;
            if(mode=="unpack")for(int src=bag.Count-1;src>=0;src--) {
                if(bag[src]==null)continue;
                int dst=Enumerable.Range(0,src).FirstOrDefault(n=>bag[n]!=null&&bag[n].canStackWith(bag[src])&&bag[n].Stack+bag[src].Stack<=bag[n].maximumStackSize(),-1);
                if(dst<0)continue;
                var a=ui.inventory[src].bounds.Center;var b=ui.inventory[dst].bounds.Center;
                var held=ui.leftClick(a.X,a.Y,null,false);held=ui.leftClick(b.X,b.Y,held,false);
                if(held!=null)throw new InvalidOperationException("lab_native_merge_held_remainder");moves++;
            }
            while(mode=="pack"&&bag.Any(i=>i==null)) {
                int dst=bag.ToList().FindIndex(i=>i==null),src=bag.ToList().FindIndex(i=>i!=null&&i.QualifiedItemId!="(O)388"&&i.Stack>1);
                if(src<0)throw new InvalidOperationException("lab_existing_nonwood_units_insufficient");
                var a=ui.inventory[src].bounds.Center;var b=ui.inventory[dst].bounds.Center;
                var held=ui.rightClick(a.X,a.Y,null,false);held=ui.leftClick(b.X,b.Y,held,false);
                if(held!=null)throw new InvalidOperationException("lab_native_repack_held_remainder");moves++;
            }
            var after=bag.Where(i=>i!=null).GroupBy(i=>(i.QualifiedItemId,i.Quality)).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
            bool conserved=totals.Count==after.Count&&totals.All(p=>after.GetValueOrDefault(p.Key)==p.Value);
            if(!conserved){PauseAutoplay("loadout_conservation_failed:round3_native_repack");throw new InvalidOperationException("loadout_conservation_failed");}
            var result=new{before,after=AgentToolRegistry.Inventory(),moves,conserved};RecordMenuEvidence("lab_round3_native_repack",result);return result;
        }
        if(mode=="held") {
            // Deliberately reproduce the old native menu path in this gated
            // fixture only. All ingredients/counts are consumed by the game.
            var m=Game1.activeClickableMenu is GameMenu g?g.GetCurrentPage():Game1.activeClickableMenu;
            if(m is not CraftingPage cp||cp.heldItem!=null)throw new InvalidOperationException("lab_requires_empty_native_crafting_menu");
            var pair=cp.pagesOfCraftingRecipes[cp.currentCraftingPage].First(p=>p.Value.name=="Chest");
            if(!pair.Value.doesFarmerHaveIngredientsInInventory())throw new InvalidOperationException("lab_native_ingredients_missing");
            var before=AgentToolRegistry.Inventory();int count=Game1.player.craftingRecipes["Chest"];
            typeof(CraftingPage).GetMethod("clickCraftingRecipe",BindingFlags.Instance|BindingFlags.NonPublic)!.Invoke(cp,new object[]{pair.Key,true});
            var result=new{before,after=AgentToolRegistry.Inventory(),crafts_before=count,crafts_after=Game1.player.craftingRecipes["Chest"],held=cp.heldItem==null?null:AgentToolRegistry.ItemInfo(cp.heldItem)};
            RecordMenuEvidence("lab_round3_native_held_output",result);return result;
        }
        throw new InvalidOperationException("unknown_round3_fixture");
    }
}
