using HarmonyLib;
using StardewValley.Locations;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    private static void KeepOccupiedMines(out List<MineShaft> __state) {
        // Vanilla evicts floors without farmers every ten game minutes. A real
        // companion is also an occupant: retain its existing instance, not a
        // newly generated floor with the same name and different objects.
        __state=instance?.Members.Where(IsManaged).Select(m=>m.Npc.currentLocation).OfType<MineShaft>()
            .Where(m=>MineShaft.activeMines.Contains(m)).Distinct().ToList()??new();
        foreach(var mine in __state)MineShaft.activeMines.Remove(mine);
    }
    private static void RestoreOccupiedMines(List<MineShaft>? __state) {
        if(__state==null)return;
        foreach(var mine in __state)if(!MineShaft.activeMines.Contains(mine))MineShaft.activeMines.Add(mine);
    }
    private static Exception? RestoreMinesOnError(Exception? __exception,List<MineShaft>? __state) {
        RestoreOccupiedMines(__state);return __exception;
    }
    private static void PatchMineLifetime(Harmony harmony) {
        harmony.Patch(AccessTools.Method(typeof(MineShaft),"clearInactiveMines"),
            prefix:new HarmonyMethod(typeof(CompanionControl),nameof(KeepOccupiedMines)),
            postfix:new HarmonyMethod(typeof(CompanionControl),nameof(RestoreOccupiedMines)),
            finalizer:new HarmonyMethod(typeof(CompanionControl),nameof(RestoreMinesOnError)));
    }
}
