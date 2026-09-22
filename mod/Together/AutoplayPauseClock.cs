using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
namespace Together;
// Hold native time evaluation, never rewrite the clock or an existing menu.
internal static class AutoplayPauseClock {
    internal static bool Held {get;private set;}
    internal static void Hold()=>Held=true;
    internal static void Release()=>Held=false;
    internal static void Install(string id)=>new Harmony(id+".pause-clock").Patch(AccessTools.Method(typeof(Game1),nameof(Game1.shouldTimePass)),postfix:new HarmonyMethod(typeof(AutoplayPauseClock),nameof(After)));
    private static void After(ref bool __result){if(Held&&Context.IsWorldReady&&!Context.IsMultiplayer)__result=false;}
}
