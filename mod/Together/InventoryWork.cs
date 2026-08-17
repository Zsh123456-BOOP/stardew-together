using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace Together;
public sealed partial class ModEntry {
    private const string WorkChestRole="stardewagent.together/chest-role";
    internal object ConfigureStorage(JsonElement args) {
        if(playerExecutor.Busy||WorkActorBusy("player"))throw new InvalidOperationException("wait_for_player_before_storage_configuration");
        string location=AgentToolRegistry.Text(args,"location",Game1.currentLocation.NameOrUniqueName);
        var l=Game1.getLocationFromName(location)??throw new InvalidOperationException("unknown_location");
        var tile=new Vector2(AgentToolRegistry.Number(args,"x",-1),AgentToolRegistry.Number(args,"y",-1));
        if(!l.objects.TryGetValue(tile,out var o)||o is not Chest c||!c.playerChest.Value||c.giftbox.Value)throw new InvalidOperationException("player_chest_required");
        string role=AgentToolRegistry.Text(args,"role","output");if(role is not ("output" or "none"))throw new InvalidOperationException("invalid_chest_role");
        c.modData[WorkChestRole]=role;return new{status="configured",location,x=(int)tile.X,y=(int)tile.Y,role,note="只改变同行用途标记，不移动物品。"};
    }
    private IEnumerable<(GameLocation Location,Vector2 Tile,Chest Chest)> SharedStorage() {
        var farm=Game1.getFarm();var locations=new[]{(GameLocation)farm}.Concat(farm.buildings.Select(b=>b.GetIndoors()).Where(l=>l!=null));
        foreach(var l in locations)foreach(var p in l.objects.Pairs)if(p.Value is Chest c&&OutputChest(c))yield return (l,p.Key,c);
    }
    private void TickWorkWithdraw(SemanticJob job) {
        var p=Game1.player;
        if(job.StorageTile.HasValue) {
            var tile=job.StorageTile.Value;
            if(!Game1.currentLocation.objects.TryGetValue(tile.ToVector2(),out var obj)||obj is not Chest chest||!OutputChest(chest))throw new InvalidOperationException("designated_storage_changed");
            if(Math.Abs(p.TilePoint.X-tile.X)+Math.Abs(p.TilePoint.Y-tile.Y)>1)throw new InvalidOperationException("storage_not_adjacent");
            if(chest.GetMutex().IsLocked())throw new InvalidOperationException("storage_busy");
            int moved=0;
            var contents=chest.GetItemsForPlayer();
            foreach(var item in contents.Where(i=>i?.QualifiedItemId==job.Item&&i.Quality>=job.MinimumQuality).OrderBy(i=>i.Quality).ToArray()) {
                int want=Math.Min(item.Stack,job.requested-job.gained-moved);if(want<=0)break;
                var copy=item.getOne();copy.Stack=want;
                int actual=want-(p.addItemToInventory(copy)?.Stack??0);item.Stack-=actual;moved+=actual;
                if(item.Stack<=0)contents.Remove(item);
            }
            job.gained+=moved;job.evidence.Add(new{kind="native_withdraw",location=Game1.currentLocation.NameOrUniqueName,tile,item=job.Item,moved});job.StorageTile=null;
            if(moved==0){StopSemanticWork(job,"inventory_full_or_shared_stock_changed");return;}
            if(job.gained>=job.requested){StopSemanticWork(job,"requested_amount_withdrawn",true);return;}
        }
        foreach(var storage in SharedStorage().OrderBy(s=>s.Location==Game1.currentLocation?0:1).ThenBy(s=>Vector2.DistanceSquared(s.Tile,p.Tile))) {
            if(storage.Chest.GetMutex().IsLocked()||!storage.Chest.GetItemsForPlayer().Any(i=>i?.QualifiedItemId==job.Item&&i.Quality>=job.MinimumQuality))continue;
            if(Game1.currentLocation!=storage.Location){WorkChild(job,"player.travel",new{location=storage.Location.NameOrUniqueName},"withdraw_travel");return;}
            var stand=WorkStand(storage.Location,storage.Tile.ToPoint());if(!stand.HasValue)continue;
            job.StorageTile=storage.Tile.ToPoint();WorkChild(job,"player.move",new{x=stand.Value.X,y=stand.Value.Y},"withdraw_move");return;
        }
        StopSemanticWork(job,"insufficient_reachable_shared_stock");
    }
    private static bool OutputChest(Chest c)=>c.playerChest.Value&&c.modData.TryGetValue(WorkChestRole,out var role)&&role=="output";
    private int StoreCount(Item item) {
        if(item is not StardewValley.Object o||o.bigCraftable.Value||o.questItem.Value||o.Category==-74)return 0;
        // Keep all tools/seeds/placeables, known reservations, and a small food stack.
        int keep=Math.Max(Data.Reservations.GetValueOrDefault(item.QualifiedItemId),o.Edibility>0?2:0);
        return Math.Max(0,item.Stack-keep);
    }
    private object InventoryPlanning()=>new{
        player_free_slots=Game1.player.Items.Count(i=>i==null),target_free_slots=2,
        keep_policy="工具、种子、可放置设备、任务物品保留；已声明预留保留，食物每种至少2个。其它产物存入标记output的共享箱，不丢弃/出售。",
        storable=Game1.player.Items.Select((item,slot)=>new{item,slot}).Where(x=>x.item!=null&&StoreCount(x.item)>0).Select(x=>new{x.slot,id=x.item.QualifiedItemId,count=StoreCount(x.item)}),
        output_chests=Game1.getFarm().objects.Pairs.Where(p=>p.Value is Chest c&&OutputChest(c)).Select(p=>new{location="Farm",x=(int)p.Key.X,y=(int)p.Key.Y,capacity=((Chest)p.Value).GetActualCapacity(),used=((Chest)p.Value).GetItemsForPlayer().Count(i=>i!=null)})
    };
    private void StorePlayerAt(Point tile,SemanticJob job) {
        var l=Game1.currentLocation;
        if(!l.objects.TryGetValue(tile.ToVector2(),out var obj)||obj is not Chest chest||!OutputChest(chest))throw new InvalidOperationException("designated_storage_changed");
        if(Math.Abs(Game1.player.TilePoint.X-tile.X)+Math.Abs(Game1.player.TilePoint.Y-tile.Y)>1)throw new InvalidOperationException("storage_not_adjacent");
        if(chest.GetMutex().IsLocked())throw new InvalidOperationException("storage_busy");
        int moved=0;var transfers=new List<object>();
        // On the single-player game update thread, addItem is the same native stack/
        // capacity routine used by ItemGrabMenu. Only subtract what it accepted.
        for(int slot=0;slot<Game1.player.Items.Count;slot++) {
            var item=Game1.player.Items[slot];if(item==null)continue;int count=StoreCount(item);if(count==0)continue;
            var copy=item.getOne();copy.Stack=count;int actual=count-(chest.addItem(copy)?.Stack??0);
            if(actual==0)continue;item.Stack-=actual;moved+=actual;transfers.Add(new{slot,id=item.QualifiedItemId,count=actual});
            if(item.Stack==0)Game1.player.Items[slot]=null;
        }
        Game1.player.faceGeneralDirection(tile.ToVector2()*64);if(moved>0)Game1.playSound("Ship");
        job.evidence.Add(new{kind="native_storage",location=l.NameOrUniqueName,x=tile.X,y=tile.Y,transfers});job.deposited+=moved;
        if(moved==0)throw new InvalidOperationException("storage_full_or_inventory_protected");
    }
    private void TickWorkStorage(SemanticJob j) {
        var origin=AgentMapOrigin(j.actor);
        if(origin.Location.NameOrUniqueName!="Farm") {
            WorkChild(j,j.actor=="player"?"player.travel":"companion.assign",j.actor=="player"?(object)new{location="Farm"}:new{actor_id=j.actor,skill="travel",destination="Farm"},"storage_travel");return;
        }
        if(j.actor!="player") {
            var actor=WorkActor(j.actor);var candidate=actor.GetProperty("candidates").EnumerateArray().FirstOrDefault(c=>c.GetProperty("skill").GetString()=="deposit");
            if(candidate.ValueKind!=JsonValueKind.Object){StopSemanticWork(j,"no_available_designated_storage");return;}
            WorkChild(j,"companion.assign",new{actor_id=j.actor,skill="deposit",target_id=candidate.GetProperty("target_id").GetString()},"storage_deposit");return;
        }
        if(j.StorageTile is {} tile) {
            StorePlayerAt(tile,j);j.StorageTile=null;j.Storing=false;
            if(j.goal=="store"){StopSemanticWork(j,"stored_available_cargo",true);return;}
            if(!Game1.player.Items.Any(i=>i==null)){StopSemanticWork(j,"inventory_protected_or_storage_insufficient");return;}
            return;
        }
        if(!Game1.player.Items.Any(i=>i!=null&&StoreCount(i)>0)){StopSemanticWork(j,"inventory_contains_only_protected_items");return;}
        foreach(var pair in Game1.getFarm().objects.Pairs.Where(p=>p.Value is Chest c&&OutputChest(c)).OrderBy(p=>Vector2.DistanceSquared(p.Key,Game1.player.Tile))) {
            var chest=(Chest)pair.Value;if(chest.GetMutex().IsLocked())continue;
            bool room=chest.GetItemsForPlayer().Count(i=>i!=null)<chest.GetActualCapacity()||Game1.player.Items.Any(i=>i!=null&&StoreCount(i)>0&&chest.GetItemsForPlayer().Any(s=>s!=null&&s.canStackWith(i)&&s.Stack<s.maximumStackSize()));
            if(!room)continue;var at=WorkStand(Game1.getFarm(),pair.Key.ToPoint());if(at==null)continue;
            j.StorageTile=pair.Key.ToPoint();WorkChild(j,"player.move",new{x=at.Value.X,y=at.Value.Y},"storage_move");return;
        }
        StopSemanticWork(j,"no_available_designated_storage");
    }
}
