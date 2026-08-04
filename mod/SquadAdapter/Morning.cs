using StardewModdingAPI;
using StardewValley;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    // Only Together's persisted, explicitly recruited companions use this API.
    // Recruitment changes control ownership; it never changes NPC coordinates.
    public bool ResumeDay(string name) {
        if(Context.IsMultiplayer || !Context.IsPlayerFree || Game1.eventUp)return false;
        var existing=Members.FirstOrDefault(m=>m.Npc.Name==name);
        if(existing!=null){managed.Add(Id(existing));return true;}
        var npc=Game1.getCharacterFromName(name);
        if(npc?.currentLocation==null || !mod.BehaviorManager.CanRecruit(npc) || npc.currentLocation.currentEvent!=null)return false;
        var mate=mod.SquadMateFactory.Create(npc);
        mod.RecruitmentManager.Recruit(mate,Game1.player,isSilent:true);
        if(!Members.Contains(mate))return false;
        managed.Add(Id(mate));return true;
    }
}
