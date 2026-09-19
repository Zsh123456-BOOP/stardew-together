using StardewValley;
using StardewValley.Quests;
namespace Together;
// The same native list feeds observation, declaration and execution; no mirrored quest state.
internal static class SocialObservation {
    internal static object? Quest(Quest q)=>q is SocializeQuest intro?new{remaining_npcs=intro.whoToGreet.ToArray(),remaining_count=intro.whoToGreet.Count,total=intro.total.Value,tool="player.social",mode="greet",quest_id=NativeQuestIdentity.Id(q)}:null;
    internal static object Read(Farmer p)=>new{introductions=p.questLog.OfType<SocializeQuest>().Select(q=>new{quest_id=NativeQuestIdentity.Id(q),complete=q.completed.Value,details=Quest(q)}).ToArray(),talked_today=p.friendshipData.Pairs.Where(x=>x.Value.TalkedToToday).Select(x=>x.Key).ToArray()};
    internal static string? Satisfied(Farmer p,string name,string mode,string questId) {
        if(mode is not ("talk" or "greet")||Game1.getCharacterFromName(name)==null)return null;
        var quest=p.questLog.OfType<SocializeQuest>().FirstOrDefault(q=>NativeQuestIdentity.Id(q)==questId);
        if(quest!=null)return !quest.whoToGreet.Contains(name)?"introduction_target_already_met":null;
        if(questId.Length>0||mode=="greet")return null;
        return p.friendshipData.GetValueOrDefault(name)?.TalkedToToday==true?"talked_today":null;
    }
}
