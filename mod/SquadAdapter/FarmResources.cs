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
        resourceReservations=JsonSerializer.Deserialize<List<ResourceReservation>>(json)??new();
        return Json(new{configured=true});
    }
    private static string PouchId(ISquadMate mate)=>$"Together_Pouch_{mate.RecruiterUniqueId}_{mate.Npc.Name}";
    private static Inventory Pouch(ISquadMate mate)=>Game1.player.team.GetOrCreateGlobalInventory(PouchId(mate));
    private static string Role(Chest chest)=>chest.modData.TryGetValue(ChestRoleKey,out var role)?role:"none";
    private static Dictionary<string,int> Counts(IEnumerable<Item> items)=>items.Where(i=>i!=null && i.Stack>0)
        .GroupBy(i=>i.QualifiedItemId+":"+i.Quality).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
    private bool Reserved(Item item)=>resourceReservations.Any(r=>r.Count>0 && item.Quality>=r.Quality &&
        (r.Item==item.QualifiedItemId || (int.TryParse(r.Item.Replace("(O)",""),out int category) && category<0 && item.Category==category)));
    private sealed record Take(Item Item,int Count);
    private sealed class ResourceWork {
        public Chest? Chest;
        public Point PickupStand;
        public List<Take> Takes=new();
        public string Input="";
        public int Quality;
        public bool PickedUp;
    }
    // Conservative reservation: any matching reserved stack is excluded from processing.
    // This can leave surplus unused, but never spends promised high-quality ingredients.
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
                bool valid=item.Stack>=trigger.RequiredCount;
                foreach(var fuel in data.AdditionalConsumedItems??new()) {
                    var stack=inventory.FirstOrDefault(i=>i!=null && i.QualifiedItemId==ItemRegistry.QualifyItemId(fuel.ItemId) && !Reserved(i));
                    if(stack==null){valid=false;break;}
                    takes.Add(new(stack,fuel.RequiredCount));
                }
                takes=takes.GroupBy(t=>t.Item).Select(g=>new Take(g.Key,g.Sum(t=>t.Count))).ToList();
                if(valid && takes.All(t=>t.Count>0 && t.Item.Stack>=t.Count) && takes.Count<=8)
                    return new(){Chest=chest,PickupStand=stand.Value,Takes=takes,Input=item.QualifiedItemId,Quality=item.Quality,PickedUp=carrying};
            }
        }
        return null;
    }
    private IEnumerable<Candidate> ResourceCandidates(ISquadMate mate) {
        if(Pouch(mate).Any(i=>i!=null && i.Stack>0)) {
            foreach(var chest in mate.Npc.currentLocation.objects.Values.OfType<Chest>().Where(c=>Role(c)=="output")) {
                var stand=StandingSpot(mate,chest.TileLocation.ToPoint());
                if(stand.HasValue)yield return new(TargetId(chest)+":deposit","deposit",chest.TileLocation.ToPoint(),chest,stand.Value);
            }
        }
        foreach(var machine in mate.Npc.currentLocation.objects.Values.Where(o=>o.bigCraftable.Value && o.heldObject.Value==null).Take(32)) {
            var stand=StandingSpot(mate,machine.TileLocation.ToPoint());
            if(stand.HasValue && SupplyFor(mate,machine)!=null)yield return new(TargetId(machine)+":refill","refill",machine.TileLocation.ToPoint(),machine,stand.Value);
        }
    }
    private bool ResourcePending(Record r)=>r.Location.objects.TryGetValue(r.Target.ToVector2(),out var current) && ReferenceEquals(current,r.Source)
        && (r.Skill=="deposit"?current is Chest c && Role(c)=="output" && Pouch(r.Mate).Any(i=>i!=null && i.Stack>0):current.heldObject.Value==null);
    private void DriveResources(Record r,bool slow,Farmer player) {
        var mate=r.Mate;var npc=mate.Npc;var work=r.Resources;
        var spot=r.Skill=="refill" && work?.PickedUp==false?work.PickupStand:r.Stand;
        if(npc.TilePoint!=spot){mod.FollowerManager.WalkAgent(mate,spot,slow,player);return;}
        mate.Halt();npc.faceGeneralDirection((r.Skill=="refill" && work?.PickedUp==false?work.Chest!.TileLocation:r.Target.ToVector2())*64);
        r.WorkSeconds+=Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
        if(r.WorkSeconds<.5)return;
        r.WorkSeconds=0;
        var pouch=Pouch(mate);
        if(r.Skill=="deposit") {
            if(r.Source is not Chest chest || Role(chest)!="output"){Finish(r,"failed","chest_permission_changed");return;}
            var item=pouch.FirstOrDefault(i=>i!=null && i.Stack>0);if(item==null){Finish(r,"failed","pouch_empty");return;}
            int count=item.Stack;string key=item.QualifiedItemId+":"+item.Quality;
            var copy=item.getOne();copy.Stack=count;
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
                || work.Takes.Any(t=>!inventory.Contains(t.Item) || t.Item.Stack<t.Count || Reserved(t.Item))) {
                Finish(r,"failed","supply_changed_or_reserved");return;
            }
            foreach(var take in work.Takes) {
                var copy=take.Item.getOne();copy.Stack=take.Count;pouch.Add(copy);
                take.Item.Stack-=take.Count;if(take.Item.Stack==0)inventory.Remove(take.Item);
            }
            work.PickedUp=true;r.PickupTile=Tile(npc.TilePoint);return;
        }
        if(work.Takes.Any(t=>Reserved(t.Item))){Finish(r,"failed","supply_now_reserved");return;}
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
