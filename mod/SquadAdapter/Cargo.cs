using HarmonyLib;
using StardewValley;
using TheStardewSquad.Framework.Squad;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    [ThreadStatic] private static ISquadMate? outputOwner;
    private static bool cargoPatched;
    private sealed class OutputScope:IDisposable {
        private readonly ISquadMate? previous;
        public OutputScope(ISquadMate mate){previous=outputOwner;outputOwner=IsManaged(mate)?mate:null;}
        public void Dispose(){outputOwner=previous;}
    }
    public static IDisposable OwnOutput(ISquadMate mate)=>new OutputScope(mate);
    public static bool HasHarvestRoom(ISquadMate mate) => !IsManaged(mate) || Pouch(mate).Count(i=>i!=null)<=9;
    public static bool? CargoAccept(Item item,bool add) {
        if(outputOwner==null)return null;
        var inventory=Pouch(outputOwner);
        if(item==null || item.Stack<=0)return false;
        int room=inventory.Where(i=>i!=null && i.canStackWith(item)).Sum(i=>Math.Max(0,i.maximumStackSize()-i.Stack));
        if(inventory.Count(i=>i!=null)<12)room+=item.maximumStackSize();
        if(room<item.Stack)return false;
        if(!add)return true;
        int left=item.Stack;
        foreach(var existing in inventory.Where(i=>i!=null && i.canStackWith(item))) {
            int moved=Math.Min(left,existing.maximumStackSize()-existing.Stack);existing.Stack+=moved;left-=moved;
            if(left==0)break;
        }
        if(left>0){var copy=item.getOne();copy.Stack=left;inventory.Add(copy);}
        return true;
    }
    private static bool InterceptInventory(Item item,ref bool __result) {
        bool? result=CargoAccept(item,true);if(!result.HasValue)return true;__result=result.Value;return false;
    }
    private static bool InterceptCapacity(Item item,ref bool __result) {
        bool? result=CargoAccept(item,false);if(!result.HasValue)return true;__result=result.Value;return false;
    }
    private static void CollectOwnDebris(Item item,GameLocation location,Debris __result) {
        if(outputOwner==null || location==null || location!=outputOwner.Npc.currentLocation || __result==null)return;
        if(CargoAccept(item,true)==true) {
            location.debris.Remove(__result);
            var r=instance?.records.Values.LastOrDefault(r=>r.Actor==Id(outputOwner) && r.Status=="running");
            if(r!=null){r.Catches.Add(item.QualifiedItemId);r.ResourceChanges[item.QualifiedItemId+":"+item.Quality]=r.ResourceChanges.GetValueOrDefault(item.QualifiedItemId+":"+item.Quality)+item.Stack;}
        }
    }
    private static bool CaptureObjectDrop(object[] __args) {
        if(outputOwner==null || __args[0] is not string id)return true;
        var location=__args.OfType<GameLocation>().FirstOrDefault()??outputOwner.Npc.currentLocation;
        if(location!=outputOwner.Npc.currentLocation)return true;
        var item=ItemRegistry.Create(id);
        if(__args.Length==7 && __args[4] is int quality)item.Quality=quality;
        if(CargoAccept(item,true)!=true)return true;
        var r=instance?.records.Values.LastOrDefault(r=>r.Actor==Id(outputOwner) && r.Status=="running");
        if(r!=null){r.Catches.Add(item.QualifiedItemId);string key=item.QualifiedItemId+":"+item.Quality;r.ResourceChanges[key]=r.ResourceChanges.GetValueOrDefault(key)+item.Stack;}
        return false;
    }
    private static bool CaptureStoneDebris(int debrisType,int numberOfChunks,GameLocation location) {
        // Vanilla breakStone uses createDebris(14), distinct from visual radial debris.
        // Capture only its real stone output in the scoped NPC action, never a player drop.
        if(outputOwner==null||debrisType!=14||numberOfChunks<=0||location!=outputOwner.Npc.currentLocation)return true;
        var item=ItemRegistry.Create("(O)390",numberOfChunks);if(CargoAccept(item,true)!=true)return true;
        var r=instance?.records.Values.LastOrDefault(r=>r.Actor==Id(outputOwner)&&r.Status=="running");
        if(r!=null)r.ResourceChanges["(O)390:0"]=r.ResourceChanges.GetValueOrDefault("(O)390:0")+numberOfChunks;
        return false;
    }
    private static void PatchCargo() {
        if(cargoPatched)return;
        var harmony=new Harmony("stardewagent.together.cargo");
        PatchMineLifetime(harmony);
        harmony.Patch(AccessTools.Method(typeof(Game1),nameof(Game1.createDebris),new[]{typeof(int),typeof(int),typeof(int),typeof(int),typeof(GameLocation)}),prefix:new HarmonyMethod(typeof(CompanionControl),nameof(CaptureStoneDebris)));
        harmony.Patch(AccessTools.Method(typeof(Farmer),nameof(Farmer.addItemToInventoryBool),new[]{typeof(Item),typeof(bool)}),prefix:new HarmonyMethod(typeof(CompanionControl),nameof(InterceptInventory)));
        harmony.Patch(AccessTools.Method(typeof(Farmer),nameof(Farmer.couldInventoryAcceptThisItem),new[]{typeof(Item)}),prefix:new HarmonyMethod(typeof(CompanionControl),nameof(InterceptCapacity)));
        harmony.Patch(AccessTools.Method(typeof(Game1),nameof(Game1.createItemDebris)),postfix:new HarmonyMethod(typeof(CompanionControl),nameof(CollectOwnDebris)));
        foreach(var method in typeof(Game1).GetMethods().Where(m=>m.Name==nameof(Game1.createObjectDebris) && m.GetParameters().Length!=4 || m.Name==nameof(Game1.createObjectDebris) && m.GetParameters()[3].ParameterType==typeof(long)))
            harmony.Patch(method,prefix:new HarmonyMethod(typeof(CompanionControl),nameof(CaptureObjectDrop)));
        cargoPatched=true;
    }
}
