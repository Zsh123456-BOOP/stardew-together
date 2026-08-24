using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.GameData.Machines;
using StardewValley.Inventories;
using StardewValley.Objects;
using TheStardewSquad.Framework.Squad;

namespace TheStardewSquad;

public sealed partial class CompanionControl {
    private const string ChestRoleKey="stardewagent.together/chest-role";
    private List<ResourceReservation> resourceReservations=new();
    public sealed class ResourceReservation {
        public string Item {get;set;}="";
        public int Quality {get;set;}
        public int Count {get;set;}
    }
    public string ConfigureFarm(string json) {
        using var doc=JsonDocument.Parse(json);
        if(doc.RootElement.ValueKind==JsonValueKind.Array)resourceReservations=JsonSerializer.Deserialize<List<ResourceReservation>>(json)??new();
        else {
            resourceReservations=JsonSerializer.Deserialize<List<ResourceReservation>>(doc.RootElement.GetProperty("reservations"))??new();
            farmPolicy=JsonSerializer.Deserialize<FarmPolicy>(doc.RootElement.GetProperty("policy"))??new(){Enabled=false};
        }
        reservationTick=-1;return Json(new{configured=true});
    }
    private static string PouchId(ISquadMate mate)=>$"Together_Pouch_{mate.RecruiterUniqueId}_{mate.Npc.Name}";
    private static Inventory Pouch(ISquadMate mate)=>Game1.player.team.GetOrCreateGlobalInventory(PouchId(mate));
    private static string Role(Chest chest)=>chest.modData.TryGetValue(ChestRoleKey,out var role)?role:"none";
    private object[] CargoStorage(ISquadMate mate) {
        var cargo=Pouch(mate).Where(i=>i!=null&&StoreableCargo(i)>0).ToArray();var farm=Game1.getFarm();
        return new[]{(GameLocation)farm}.Concat(farm.buildings.Select(b=>b.GetIndoors()).Where(l=>l!=null)).SelectMany(l=>l.objects.Pairs.Where(pair=>pair.Value is Chest c&&Role(c)=="output"&&!c.GetMutex().IsLocked()).Select(pair=>{
            var c=(Chest)pair.Value;var items=c.GetItemsForPlayer();bool room=items.Count(i=>i!=null)<c.GetActualCapacity()||cargo.Any(i=>items.Any(s=>s!=null&&s.canStackWith(i)&&s.Stack<s.maximumStackSize()));
            return (object)new{location=l.NameOrUniqueName,x=(int)pair.Key.X,y=(int)pair.Key.Y,accepts_cargo=room};
        })).ToArray();
    }
    private static Dictionary<string,int> Counts(IEnumerable<Item> items)=>items.Where(i=>i!=null && i.Stack>0)
        .GroupBy(i=>i.QualifiedItemId+":"+i.Quality).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
    private static bool Matches(Item item,ResourceReservation r)=>item.Quality>=r.Quality &&
        (r.Item==item.QualifiedItemId || (int.TryParse(r.Item.Replace("(O)",""),out int category) && category<0 && item.Category==category));
    private long reservationTick=-1;
    private readonly Dictionary<Item,int> reservedCounts=new();
    private int FreeCount(Item item) {
        long tick=Game1.currentGameTime.TotalGameTime.Ticks;
        if(reservationTick!=tick) {
            reservationTick=tick;reservedCounts.Clear();
            var items=new List<Item>();items.AddRange(Game1.player.Items.Where(i=>i!=null));
            items.AddRange(Game1.player.team.GetOrCreateGlobalInventory($"TheStardewSquad_SquadInventory_{Game1.player.UniqueMultiplayerID}").Where(i=>i!=null));
            var visited=new HashSet<GameLocation>();
            void Visit(GameLocation l) {
                if(!visited.Add(l))return;
                foreach(var c in l.objects.Values.OfType<Chest>())items.AddRange(c.GetItemsForPlayer(Game1.player.UniqueMultiplayerID).Where(i=>i!=null));
                foreach(var b in l.buildings)if(b.GetIndoors() is {} inside)Visit(inside);
            }
            Visit(Game1.getFarm());foreach(var m in Members)items.AddRange(Pouch(m).Where(i=>i!=null));
            var stacks=items.Distinct().OrderBy(i=>i.Quality).ThenBy(i=>i.QualifiedItemId,StringComparer.Ordinal).ToArray();
            var demands=resourceReservations.Where(r=>r.Count>0&&r.Item!="(O)-1").OrderByDescending(r=>r.Quality).ThenBy(r=>r.Item,StringComparer.Ordinal).ToArray();
            var allocation=Together.Shared.ResourceFlow.Allocate(stacks.Select(i=>new Together.Shared.ResourceFlow.Stock(i.QualifiedItemId,i.Category,i.Quality,i.Stack)).ToArray(),
                demands.Select(r=>new Together.Shared.ResourceFlow.Demand(r.Item,r.Quality,r.Count)).ToArray());
            for(int i=0;i<stacks.Length;i++)reservedCounts[stacks[i]]=allocation.StockUsed[i];
        }
        return reservedCounts.TryGetValue(item,out int reserved)?Math.Max(0,item.Stack-reserved):0;
    }
    private bool Reserved(Item item)=>FreeCount(item)<=0;
    private sealed record Take(Item Item,int Count);
    private sealed class ResourceWork {
        public Chest? Chest;
        public Point PickupStand;
        public List<Take> Takes=new();
        public string Input="";
        public int Quality;
        public bool PickedUp;
    }
    // Only unreserved quantities can be selected or consumed.
    private ResourceWork? SupplyFor(ISquadMate mate,StardewValley.Object machine) {
        var data=machine.GetMachineData();if(data==null || machine.heldObject.Value!=null || machine.GetType()!=typeof(StardewValley.Object))return null;
        bool carrying=Pouch(mate).Any(i=>i!=null);
        var sources=carrying?new Chest?[]{null}:mate.Npc.currentLocation.objects.Values.OfType<Chest>().Where(c=>Role(c)=="supplies").Cast<Chest?>().ToArray();
        foreach(var chest in sources) {
            var stand=chest==null?mate.Npc.TilePoint:StandingSpot(mate,chest.TileLocation.ToPoint());if(!stand.HasValue)continue;
            var inventory=chest==null?Pouch(mate):chest.GetItemsForPlayer(mate.RecruiterUniqueId);
            foreach(var item in inventory.Where(i=>i!=null && i.Stack>0 && !Reserved(i))) {
                if(!MachineDataUtility.TryGetMachineOutputRule(machine,data,MachineOutputTrigger.ItemPlacedInMachine,item,Game1.player,machine.Location,
                    out _,out var trigger,out _,out _) || trigger.RequiredCount<=0)continue;
                var takes=new List<Take>{new(item,trigger.RequiredCount)};
                bool valid=FreeCount(item)>=trigger.RequiredCount;
                foreach(var fuel in data.AdditionalConsumedItems??new()) {
                    var stack=inventory.FirstOrDefault(i=>i!=null && i.QualifiedItemId==ItemRegistry.QualifyItemId(fuel.ItemId) && !Reserved(i));
                    if(stack==null){valid=false;break;}
                    takes.Add(new(stack,fuel.RequiredCount));
                }
                takes=takes.GroupBy(t=>t.Item).Select(g=>new Take(g.Key,g.Sum(t=>t.Count))).ToList();
                if(valid && takes.All(t=>t.Count>0 && FreeCount(t.Item)>=t.Count) && takes.Count<=8)
                    return new(){Chest=chest,PickupStand=stand.Value,Takes=takes,Input=item.QualifiedItemId,Quality=item.Quality,PickedUp=carrying};
            }
        }
        return null;
    }
    private static bool ChestHasRoom(Chest chest,ISquadMate mate,Item item) {
        var inventory=chest.GetItemsForPlayer(mate.RecruiterUniqueId);
        return inventory.Count(i=>i!=null)<chest.GetActualCapacity() || inventory.Any(i=>i!=null && i.canStackWith(item) && i.Stack<i.maximumStackSize());
    }
    private int StoreableCargo(Item item) {
        if(item is not StardewValley.Object o||o.questItem.Value||o.Category==-74||o.bigCraftable.Value)return 0;
        // Depositing preserves ownership and quality. Goal reservations apply to
        // consuming/selling, never to moving the same materials into shared stock.
        return item.Stack;
    }
    private IEnumerable<Candidate> ResourceCandidates(ISquadMate mate) {
        if(Pouch(mate).Any(i=>i!=null && i.Stack>0)) {
            foreach(var chest in mate.Npc.currentLocation.objects.Values.OfType<Chest>().Where(c=>Role(c)=="output"&&!c.GetMutex().IsLocked())) {
                var stand=StandingSpot(mate,chest.TileLocation.ToPoint());
                if(stand.HasValue && Pouch(mate).Any(i=>i!=null && StoreableCargo(i)>0 && ChestHasRoom(chest,mate,i))) {
                    yield return new(TargetId(chest)+":deposit","deposit",chest.TileLocation.ToPoint(),chest,stand.Value);
                    if(Pouch(mate).Any(i=>i!=null && i.Category is -4 or -80 && FreeCount(i)>0 && ChestHasRoom(chest,mate,i)))yield return new(TargetId(chest)+":gift","gift",chest.TileLocation.ToPoint(),chest,stand.Value);
                }
            }
        }
        foreach(var machine in mate.Npc.currentLocation.objects.Values.Where(o=>o.bigCraftable.Value && o.heldObject.Value==null).Take(32)) {
            var stand=StandingSpot(mate,machine.TileLocation.ToPoint());
            if(stand.HasValue && SupplyFor(mate,machine)!=null)yield return new(TargetId(machine)+":refill","refill",machine.TileLocation.ToPoint(),machine,stand.Value);
        }
    }
    private bool ResourcePending(Record r)=>r.Location.objects.TryGetValue(r.Target.ToVector2(),out var current) && ReferenceEquals(current,r.Source)
        && (r.Skill is "deposit" or "gift"?current is Chest c && Role(c)=="output" && Pouch(r.Mate).Any(i=>i!=null && (r.Skill=="gift"?i.Stack>0:StoreableCargo(i)>0)):current.heldObject.Value==null);
    private void DriveResources(Record r,bool slow,Farmer player) {
        var mate=r.Mate;var npc=mate.Npc;var work=r.Resources;
        var spot=r.Skill=="refill" && work?.PickedUp==false?work.PickupStand:r.Stand;
        if(npc.TilePoint!=spot){mod.FollowerManager.WalkAgent(mate,spot,slow,player);return;}
        mate.Halt();npc.faceGeneralDirection((r.Skill=="refill" && work?.PickedUp==false?work.Chest!.TileLocation:r.Target.ToVector2())*64);
        r.WorkSeconds+=Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
        if(r.WorkSeconds<.5)return;
        r.WorkSeconds=0;
        var pouch=Pouch(mate);reservationTick=-1;
        if(r.Skill is "deposit" or "gift") {
            if(r.Source is not Chest chest || Role(chest)!="output"){Finish(r,"failed","chest_permission_changed");return;}
            if(chest.GetMutex().IsLocked()){Finish(r,"failed","storage_busy");return;}
            var item=pouch.FirstOrDefault(i=>i!=null && i.Stack>0 && (r.Skill=="gift"||StoreableCargo(i)>0) && ChestHasRoom(chest,mate,i) && (r.Skill!="gift" || (i.Category is -4 or -80 && FreeCount(i)>0)));if(item==null){Finish(r,"failed","pouch_empty");return;}
            int count=r.Skill=="gift"?1:StoreableCargo(item);string key=item.QualifiedItemId+":"+item.Quality;
            var copy=item.getOne();copy.Stack=count;
            if(r.Skill=="gift")copy.modData["stardewagent.together/gift-from"]=r.Mate.Npc.Name;
            var remainder=chest.addItem(copy);int moved=count-(remainder?.Stack??0);
            if(moved==0){Finish(r,"failed","output_chest_full");return;}
            item.Stack-=moved;if(item.Stack==0)pouch.Remove(item);
            r.ResourceChanges[key]=moved;r.EffectByActor=true;mate.ActionCooldown=24;return;
        }
        if(work==null){Finish(r,"failed","missing_supply_plan");return;}
        if(!work.PickedUp) {
            if(work.Chest==null){Finish(r,"failed","missing_supply_chest");return;}
            var inventory=work.Chest.GetItemsForPlayer(mate.RecruiterUniqueId);
            if(Role(work.Chest)!="supplies" || !r.Location.objects.Values.Any(o=>ReferenceEquals(o,work.Chest)) || pouch.Any(i=>i!=null)
                || work.Takes.Any(t=>!inventory.Contains(t.Item) || FreeCount(t.Item)<t.Count)) {
                Finish(r,"failed","supply_changed_or_reserved");return;
            }
            foreach(var take in work.Takes) {
                var copy=take.Item.getOne();copy.Stack=take.Count;pouch.Add(copy);
                take.Item.Stack-=take.Count;if(take.Item.Stack==0)inventory.Remove(take.Item);
            }
            reservationTick=-1;work.PickedUp=true;r.PickupTile=Tile(npc.TilePoint);return;
        }
        if(work.Takes.Any(t=>!pouch.Any(i=>i!=null && i.QualifiedItemId==t.Item.QualifiedItemId && i.Quality==t.Item.Quality && FreeCount(i)>=t.Count))){Finish(r,"failed","supply_now_reserved");return;}
        var machine=(StardewValley.Object)r.Source!;
        var input=pouch.FirstOrDefault(i=>i!=null && i.QualifiedItemId==work.Input && i.Quality==work.Quality);
        if(input==null || machine.heldObject.Value!=null || !ResourcePending(r)){Finish(r,"failed","machine_or_cargo_changed");return;}
        var before=Counts(pouch);var previous=StardewValley.Object.autoLoadFrom;
        try {
            StardewValley.Object.autoLoadFrom=pouch;
            bool accepted=machine.PlaceInMachine(machine.GetMachineData(),input,false,player,showMessages:false,playSounds:true);
            var after=Counts(pouch);
            foreach(var pair in before)if(pair.Value!=after.GetValueOrDefault(pair.Key))r.ResourceChanges[pair.Key]=after.GetValueOrDefault(pair.Key)-pair.Value;
            if(accepted && machine.heldObject.Value!=null && r.ResourceChanges.Values.Any(n=>n<0)) {
                r.EffectByActor=true;mate.ActionCooldown=24;r.ProcessingOutput=machine.heldObject.Value.QualifiedItemId;
            } else Finish(r,"failed","machine_rejected_cargo");
        } finally {StardewValley.Object.autoLoadFrom=previous;}
    }
}
