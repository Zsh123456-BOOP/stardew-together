using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;

namespace Together;
public sealed partial class ModEntry {
    private const string WorkChestRole="stardewagent.together/chest-role";
    internal object ConfigureStoragePolicy(JsonElement args) {
        var p=Data.Storage;
        int cap=AgentToolRegistry.Number(args,"max_shared_chests",p.MaxSharedChests),budget=AgentToolRegistry.Number(args,"wood_budget_per_day",p.WoodBudgetPerDay);
        if(cap is <0 or >32||budget is <0 or >999)throw new InvalidOperationException("invalid_storage_expansion_policy");
        if(args.TryGetProperty("auto_expand",out var enabled)) {
            if(enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("invalid_auto_expand");
            p.AutoExpand=enabled.GetBoolean();
        }
        p.MaxSharedChests=cap;p.WoodBudgetPerDay=budget;
        return new{policy=p,note="预算调整不退回今日已预留木材额度；原生制作仍保护其它目标材料，满包无制作空位会明确报告。"};
    }
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
            foreach(var item in contents.Where(i=>i?.QualifiedItemId==job.Item&&(job.ExactQuality?i.Quality==job.MinimumQuality:i.Quality>=job.MinimumQuality)).OrderBy(i=>i.Quality).ToArray()) {
                int want=Math.Min(item.Stack,job.requested-job.gained-moved);if(want<=0)break;
                var copy=item.getOne();copy.Stack=want;
                int actual=want-(p.addItemToInventory(copy)?.Stack??0);item.Stack-=actual;moved+=actual;
                if(item.Stack<=0)contents.Remove(item);
            }
            Data.Autoplay.Memory.Diary.Add(job.command_id+":withdraw:"+job.gained,Game1.Date.TotalDays,Game1.timeOfDay,"取出仓库",job.Item,Game1.currentLocation.NameOrUniqueName+":"+tile,moved);
            job.gained+=moved;job.evidence.Add(new{kind="native_withdraw",location=Game1.currentLocation.NameOrUniqueName,tile,item=job.Item,moved});job.StorageTile=null;
            if(moved==0){StopSemanticWork(job,contents.Any(i=>i?.QualifiedItemId==job.Item)?"capacity_no_stackable_room":"shared_stock_changed");return;}
            if(job.gained>=job.requested){StopSemanticWork(job,"requested_amount_withdrawn",true);return;}
        }
        foreach(var storage in SharedStorage().OrderBy(s=>s.Location==Game1.currentLocation?0:1).ThenBy(s=>Vector2.DistanceSquared(s.Tile,p.Tile))) {
            if(storage.Chest.GetMutex().IsLocked()||!storage.Chest.GetItemsForPlayer().Any(i=>i?.QualifiedItemId==job.Item&&(job.ExactQuality?i.Quality==job.MinimumQuality:i.Quality>=job.MinimumQuality)))continue;
            if(Game1.currentLocation!=storage.Location){WorkChild(job,"player.travel",new{location=storage.Location.NameOrUniqueName},"withdraw_travel");return;}
            var stand=WorkStand(storage.Location,storage.Tile.ToPoint());if(!stand.HasValue)continue;
            job.StorageTile=storage.Tile.ToPoint();WorkChild(job,"player.move",new{x=stand.Value.X,y=stand.Value.Y},"withdraw_move");return;
        }
        if(ReceivePartnerCargo(job,job.Item,job.requested-job.gained,true))return;
        StopSemanticWork(job,"insufficient_reachable_shared_stock_or_idle_partner_cargo");
    }
    private static bool OutputChest(Chest c)=>c.playerChest.Value&&c.modData.TryGetValue(WorkChestRole,out var role)&&role=="output";
    private bool PlayerNeedsWorkStorage(SemanticJob job) {
        if(job.goal=="fish"&&!CapacityAdapter.HasSlots(Game1.player,1))return true;
        if(job.RequiredSlots>0&&!OperationsPolicy.CapacityReady(CapacityAdapter.Of(Game1.player).FreeSlots,job.RequiredSlots))return true;
        bool output=job.goal is "cleanup" or "milk" or "shear" or "animal_collect" or "resource" or "hardwood" or "stone" or "wood" or "fiber" or "harvest" or "forage" or "collect" or "tend";
        string item=job.Item.Length>0?job.Item:job.goal switch{"stone"=>"(O)390","wood"=>"(O)388","fiber"=>"(O)771","hardwood"=>"(O)709",_=>""};
        bool stacks=item.Length>0&&CapacityAdapter.CanReceive(Game1.player,ItemRegistry.Create(item));
        // Unknown side drops are handled by the real pickup-capacity check, which
        // retains their positions and resumes after unloading. Planting consumes
        // seeds; it must not run off to store merely because one slot remains.
        return StorageTiming.NeedsRoom(CapacityAdapter.Of(Game1.player).FreeSlots,output,stacks);
    }
    private int StoreCount(Item item) {
        if(semanticJobs.Values.Any(j=>j.status=="running"&&j.goal=="plant"&&farmPlantPlans.TryGetValue(j.PlanId,out var plan)&&plan.Fertilizer==item.QualifiedItemId))return 0;
        if(item is not StardewValley.Object o||o.bigCraftable.Value||o.questItem.Value||o.Category==-74)return 0;
        // Storage is not consumption: reserved materials remain owned in shared
        // chests and are withdrawn by dependency tasks when actually needed.
        // One supply selection for the whole bag, not two of every edible type.
        var food=Game1.player.Items.OfType<StardewValley.Object>().Where(f=>f.Edibility>0&&!f.questItem.Value&&!f.bigCraftable.Value&&f.Category!=-74)
            .OrderByDescending(f=>f.staminaRecoveredOnConsumption()).FirstOrDefault();
        int required=semanticJobs.Values.Any(j=>j.actor=="player"&&j.status=="running"&&j.goal is "fish" or "mine_trip" or "volcano_trip")?80:40;
        int keep=ReferenceEquals(o,food)?Math.Min(o.Stack,(int)Math.Ceiling(required/(double)Math.Max(1,o.staminaRecoveredOnConsumption()))):0;
        return Math.Max(0,item.Stack-keep);
    }
    private object InventoryPlanning() {
        var processing=BusinessRawReserves();
        return new{
        slot_count=Game1.player.MaxItems,free_slots=CapacityAdapter.Of(Game1.player).FreeSlots,occupied=Game1.player.MaxItems-CapacityAdapter.Of(Game1.player).FreeSlots,
        capacity_release_conditions="实际背包/仓储/保护预留/批准用途版本变化才解除；换日、换措辞不解除",
        capacity_constraints=Data.Autoplay.Capacity.Constraints,capacity_version=Data.Autoplay.Capacity.Version,
        ownership=new {observed_day=Game1.Date.TotalDays,observed_time=Game1.timeOfDay,
            carried=Game1.player.Items.Select((i,slot)=>new{slot,item=i}).Where(x=>x.item!=null).Select(x=>new{x.slot,id=x.item.QualifiedItemId,name=x.item.DisplayName,count=x.item.Stack,quality=x.item.Quality,storable=StoreCount(x.item)}).ToArray(),
            warehouse=SharedStorage().Select(s=>new{location=s.Location.NameOrUniqueName,x=(int)s.Tile.X,y=(int)s.Tile.Y,items=s.Chest.GetItemsForPlayer().Where(i=>i!=null).Select(i=>new{id=i.QualifiedItemId,name=i.DisplayName,count=i.Stack,quality=i.Quality}).ToArray()}).ToArray(),
            transferable_to_warehouse=Game1.player.Items.Where(i=>i!=null).Sum(StoreCount),
            note="carried才是当前随身背包；warehouse不占背包格。日记只记录过去事件，不能当作当前库存；没有可转移物且空格满足要求时无需存货。"},
        tools=Game1.player.Items.OfType<Tool>().Select(t=>new{id=t.QualifiedItemId,state="carried",location=Game1.currentLocation.NameOrUniqueName}).Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer().OfType<Tool>().Select(t=>new{id=t.QualifiedItemId,state=s.Chest.GetMutex().IsLocked()?"storage_busy":"retrievable",location=s.Location.NameOrUniqueName}))).ToArray(),
        tool_upgrade=Game1.player.toolBeingUpgraded.Value is {} upgraded?new{id=upgraded.QualifiedItemId,state="in_upgrade",days_remaining=Game1.player.daysLeftForToolUpgrade.Value}:null,
        preparation=preparationSummary,
        player_free_slots=CapacityAdapter.Of(Game1.player).FreeSlots,
        storage_trigger="只在后续实际产物放不下、必要材料交接或收工整理时存箱；有可叠加空间就继续。下游工作需要多个空格时可提前整理，使用required_free_slots。",
        keep_policy="工具、种子、设备、任务物品保留，食物按总恢复预算选择一组，不逐品种保留。目标预留材料可存共享箱但不能被其他用途消耗；需要时由依赖任务取回。",
        storable=Game1.player.Items.Select((item,slot)=>new{item,slot}).Where(x=>x.item!=null&&StoreCount(x.item)>0).Select(x=>new{x.slot,id=x.item.QualifiedItemId,count=StoreCount(x.item)}),
        sale_reserves=BusinessRetention.Materials.Select(id=>new{item=id,allocation=AllocateMaterial(id,processing),automatic_sale=ApprovedMaterialSale(id)}),
        expansion_policy=Data.Storage,
        shared_storage_chests=SharedStorage().Select(s=>new{location=s.Location.NameOrUniqueName,x=(int)s.Tile.X,y=(int)s.Tile.Y,capacity=s.Chest.GetActualCapacity(),used=s.Chest.GetItemsForPlayer().Count(i=>i!=null)})
    };
    }
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
        Data.Autoplay.Record("supply_verified",AgentJson.Encode(new{job.command_id,job.goal,job.RequiredSlots,free_slots=CapacityAdapter.Of(Game1.player).FreeSlots,moved,transfers,resume_location=job.goal=="fish"?job.FishLocation:job.location}));
        Data.Autoplay.Memory.Diary.Transfers(job.command_id+":store:"+job.deposited,JsonSerializer.SerializeToElement(transfers),Game1.Date.TotalDays,Game1.timeOfDay,l.NameOrUniqueName+":"+tile);
        job.evidence.Add(new{kind="native_storage",location=l.NameOrUniqueName,x=tile.X,y=tile.Y,transfers});job.deposited+=moved;
        if(moved==0)throw new InvalidOperationException("capacity_no_stackable_room");
    }
    private void TickWorkStorage(SemanticJob j) {
        var origin=AgentMapOrigin(j.actor);
        if(j.actor!="player") {
            var actor=WorkActor(j.actor);
            int storable=actor.TryGetProperty("storable_cargo",out var raw)?raw.GetInt32():actor.GetProperty("cargo").EnumerateObject().Sum(p=>p.Value.GetInt32());
            if(storable==0) {
                j.Storing=false;
                if(j.goal=="store"){StopSemanticWork(j,"stored_available_cargo",true);return;}
                if(CompanionCargoSlots(actor)>=8)StopSemanticWork(j,"companion_inventory_contains_only_protected_items");
                return;
            }
            var candidate=actor.GetProperty("candidates").EnumerateArray().FirstOrDefault(c=>c.GetProperty("skill").GetString()=="deposit"&&!AgentTileBusy(origin.Location.NameOrUniqueName,c.GetProperty("tile")[0].GetInt32(),c.GetProperty("tile")[1].GetInt32()));
            if(candidate.ValueKind==JsonValueKind.Object) {
                WorkChild(j,"companion.assign",new{actor_id=j.actor,skill="deposit",target_id=candidate.GetProperty("target_id").GetString()},"storage_deposit");return;
            }
            var reachable=actor.GetProperty("reachable_locations").EnumerateArray().Where(v=>v.ValueKind==JsonValueKind.String).Select(v=>v.GetString()).ToHashSet();
            foreach(var storage in SharedStorage().Where(s=>s.Location!=origin.Location&&reachable.Contains(s.Location.NameOrUniqueName))) {
                if(storage.Chest.GetMutex().IsLocked())continue;
                bool accepts=actor.TryGetProperty("cargo_storage",out var destinations)?destinations.EnumerateArray().Any(d=>d.GetProperty("location").GetString()==storage.Location.NameOrUniqueName&&d.GetProperty("x").GetInt32()==(int)storage.Tile.X&&d.GetProperty("y").GetInt32()==(int)storage.Tile.Y&&d.GetProperty("accepts_cargo").GetBoolean()):storage.Chest.GetItemsForPlayer().Count(i=>i!=null)<storage.Chest.GetActualCapacity();
                if(!accepts)continue;
                WorkChild(j,"companion.assign",new{actor_id=j.actor,skill="travel",destination=storage.Location.NameOrUniqueName},"storage_travel");return;
            }
            if(actor.TryGetProperty("can_reach_farm",out var farmReach)&&!farmReach.GetBoolean()){StopSemanticWork(j,"companion_shared_storage_farm_unreachable");return;}
            if(RequestCompanionStorageSupport(j))return;
            StopSemanticWork(j,"companion_needs_reachable_shared_capacity_or_expansion_budget");return;
        }
        if(j.ExpansionTile.HasValue){TickStorageExpansion(j);return;}
        if(j.StorageTile is {} tile) {
            if(Game1.currentLocation.NameOrUniqueName!=j.StorageLocation){WorkChild(j,"player.travel",new{location=j.StorageLocation},"storage_travel");return;}
            if(Math.Abs(Game1.player.TilePoint.X-tile.X)+Math.Abs(Game1.player.TilePoint.Y-tile.Y)>1) {
                var stand=WorkStand(Game1.currentLocation,tile);if(!stand.HasValue)throw new InvalidOperationException("selected_storage_unreachable");
                WorkChild(j,"player.move",new{x=stand.Value.X,y=stand.Value.Y},"storage_move");return;
            }
            StorePlayerAt(tile,j);j.ReliefDepth=0;j.ReliefAction="";j.Excluded.Add("storage:"+j.StorageLocation+":"+tile.X+":"+tile.Y);j.StorageTile=null;
            if(j.goal!="store"&&!PlayerNeedsWorkStorage(j)&&(!j.PickupPending||CapacityAdapter.Of(Game1.player).FreeSlots>0)){j.Storing=false;j.Excluded.RemoveWhere(x=>x.StartsWith("storage:"));return;}
        }
        StartCapacityRelief(j);
    }
}
