using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace Together;
public sealed partial class ModEntry {
    private const string WorkChestRole="stardewagent.together/chest-role";
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
