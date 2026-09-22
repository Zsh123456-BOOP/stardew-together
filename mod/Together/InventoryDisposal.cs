using System.Text.Json;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private int Discardable(Item item) {
        if(item is Tool||item is not StardewValley.Object o||o.questItem.Value||o.bigCraftable.Value||!item.canBeTrashed())return 0;
        return Math.Min(item.Stack,DisposableStock(item.QualifiedItemId,item.Quality));
    }
    private void ValidateDiscard(Item item,int count) {
        RefreshFacts(true);
        if(count<1||count>Discardable(item))throw new InvalidOperationException("discard_protected_or_stock_changed");
        ValidatePlayerConsumption(new Dictionary<Item,int>{{item,count}},"","discard");
    }
    internal object ReadCapacityOptions() {
        RefreshFacts(true);var p=Game1.player;int free=CapacityAdapter.Of(p).FreeSlots;
        object Row(Item item,int slot,string source,string location,int x,int y) {
            int available=Discardable(item);
            return new{source,location,x,y,slot,item=item.QualifiedItemId,name=item.DisplayName,quality=item.Quality,count=item.Stack,disposable=available,
                free_slots_after_full_disposal=free+(source=="backpack"&&available==item.Stack?1:0),storage_slots_released=source=="storage"&&available==item.Stack?1:0,
                estimated_sale_value=item is StardewValley.Object o?(long)o.sellToStorePrice()*available:0,
                protected_count=item.Stack-available,action=available>0?(object)new{tool="player.discard",args=new{source,location,x,y,slot,item=item.QualifiedItemId,quality=item.Quality,count=available,expected_stack=item.Stack,reason="由模型根据用途和腾格效果选择"}}:null};
        }
        return new{slot_count=p.MaxItems,free_slots=free,occupied=CapacityAdapter.Of(p).Occupied,
            backpack=p.Items.Select((item,slot)=>(item,slot)).Where(v=>v.item!=null).Select(v=>Row(v.item,v.slot,"backpack",p.currentLocation.NameOrUniqueName,0,0)).ToArray(),
            storage=SharedStorage().Select(s=>new{location=s.Location.NameOrUniqueName,x=(int)s.Tile.X,y=(int)s.Tile.Y,busy=s.Chest.GetMutex().IsLocked(),items=s.Chest.GetItemsForPlayer().Select((item,slot)=>(item,slot)).Where(v=>v.item!=null).Select(v=>Row(v.item,v.slot,"storage",s.Location.NameOrUniqueName,(int)s.Tile.X,(int)s.Tile.Y)).ToArray()}).ToArray(),
            planting=farmPlantPlans.Values.Where(f=>f.Epoch==agentSaveEpoch&&f.Day==Game1.Date.TotalDays).Select(f=>new{f.Id,f.Seed,tiles=f.Tiles.Count,carried=p.Items.Where(i=>i?.QualifiedItemId==f.Seed).Sum(i=>i.Stack),next=new{tool="work.run",args=new{goal="plant",plan_id=f.Id}},note="执行后按实际种子消耗核验；规划不等于已腾格"}).ToArray(),
            rule="采集并入已有堆叠不会腾格；部分丢弃不腾格。优先比较存仓、播种、投料等用途，也可自主选择销毁未预留普通物品。player.discard通过原生垃圾桶不可逆销毁；仓库销毁只释放仓库格，不能冒充背包腾格。失败重试须对应实际解除条件。"};
    }
    private object LabSplitInventory() {
        if(!Settings.EnableLab||!StardewModdingAPI.Context.IsWorldReady||Game1.player.Name!="AgentLab")throw new InvalidOperationException("isolated_lab_required");
        if(playerExecutor.Busy||Game1.activeClickableMenu!=null||Game1.player.CursorSlotItem!=null)throw new InvalidOperationException("player_not_free");
        var p=Game1.player;var before=p.Items.Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
        int free=CapacityAdapter.Of(p).FreeSlots,slot=Enumerable.Range(0,p.Items.Count).FirstOrDefault(n=>p.Items[n]?.QualifiedItemId=="(O)771"&&p.Items[n].Stack>free,-1);
        if(slot<0)throw new InvalidOperationException("native_fiber_stack_required_for_split");
        var menu=new StardewValley.Menus.GameMenu(0);Game1.activeClickableMenu=menu;var page=(StardewValley.Menus.InventoryPage)menu.GetCurrentPage();var kb=Game1.oldKBState;
        try {
            Game1.oldKBState=default;
            while(CapacityAdapter.Of(p).FreeSlots>0) {
                int empty=Enumerable.Range(0,p.MaxItems).First(n=>p.Items[n]==null);var src=page.inventory.inventory[slot].bounds;var dest=page.inventory.inventory[empty].bounds;
                page.receiveRightClick(src.Center.X,src.Center.Y);page.receiveLeftClick(dest.Center.X,dest.Center.Y);
                if(p.CursorSlotItem!=null)throw new InvalidOperationException("native_split_failed");
            }
        }finally{Game1.oldKBState=kb;}
        menu.exitThisMenu();var after=p.Items.Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
        if(!before.OrderBy(v=>v.Key).SequenceEqual(after.OrderBy(v=>v.Key)))throw new InvalidOperationException("loadout_conservation_failed:native_split");
        return new{before,after,free_slots=CapacityAdapter.Of(p).FreeSlots,source="native_inventory_right_click_split_no_new_items"};
    }

}
