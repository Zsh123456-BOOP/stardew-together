using HarmonyLib;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Quests;

namespace Together;

// Daily native quests often have no data ID. Give those objects an external
// handle in serializable mod metadata, never change the game's quest ID.
internal static class NativeQuestIdentity {
    private const string HandleKey="Together/quest-handle", CompletedPrefix="Together/quest-completed/";
    internal static string Id(Quest quest) {
        if(!string.IsNullOrEmpty(quest.id.Value))return quest.id.Value;
        if(!quest.modData.TryGetValue(HandleKey,out string handle)||string.IsNullOrWhiteSpace(handle))
            quest.modData[HandleKey]=handle="generated-"+Guid.NewGuid().ToString("N");
        return handle;
    }
    internal static Quest? Find(string id)=>Game1.player.questLog.FirstOrDefault(q=>Id(q)==id);
    internal static bool Completed(string id)=>Find(id)?.completed.Value==true||Game1.player.modData.ContainsKey(CompletedPrefix+id);
    internal static void Install(string owner)=>new Harmony(owner+".quest-evidence").Patch(AccessTools.Method(typeof(Quest),nameof(Quest.questComplete)),prefix:new HarmonyMethod(typeof(NativeQuestIdentity),nameof(Before)),postfix:new HarmonyMethod(typeof(NativeQuestIdentity),nameof(After)));
    private static void Before(Quest __instance,out bool __state)=>__state=Context.IsWorldReady&&!__instance.completed.Value&&Game1.player.questLog.Contains(__instance);
    private static void After(Quest __instance,bool __state) {
        if(!__state||!__instance.completed.Value)return;
        // Called only AFTER genuine native completion. Saving this evidence in
        // the save itself makes old-save rollback discard future completions.
        Game1.player.modData[CompletedPrefix+Id(__instance)]=Game1.Date.TotalDays.ToString(System.Globalization.CultureInfo.InvariantCulture);
    }
    internal static (int Current,int Required) Count(Quest quest)=>quest switch {
        ResourceCollectionQuest q=>(q.numberCollected.Value,q.number.Value),
        FishingQuest q=>(q.numberFished.Value,q.numberToFish.Value),
        SlayMonsterQuest q=>(q.numberKilled.Value,q.numberToKill.Value),
        SocializeQuest q=>(Math.Max(0,q.total.Value-q.whoToGreet.Count),q.total.Value),
        ItemHarvestQuest q=>(0,Math.Max(0,q.Number.Value)),
        _=>(quest.completed.Value?1:0,1)
    };
}
