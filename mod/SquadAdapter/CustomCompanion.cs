using StardewValley;
using StardewModdingAPI;
namespace TheStardewSquad;
public sealed partial class CompanionControl {
    // Remove only our custom body from runtime membership. Global inventories
    // remain native save data; no cargo transfer/destruction happens here.
    public bool SuspendCustomCompanion(string name) {
        var npc=GetCharacter(name);if(npc==null)return true;
        if(!npc.modData.ContainsKey("stardewagent.together/custom-partner"))return false;
        var mate=Members.FirstOrDefault(m=>m.Npc==npc);
        if(mate!=null) {
            string id=Id(mate);
            foreach(var r in records.Values.Where(r=>r.Actor==id&&r.Status=="running").ToArray())Finish(r,"cancelled","single_player_mode");
            mod.FollowerManager.ClearMateTaskAndReset(mate);mate.Halt();keepDismissalPosition.Add(npc);
            try{mod.RecruitmentManager.Dismiss(mate,isSilent:true,warpBehavior:TheStardewSquad.Framework.Behaviors.DismissalWarpBehavior.RoamHere);}
            finally{keepDismissalPosition.Remove(npc);}
            managed.Remove(id);stay.Remove(id);
        }
        npc.Halt();npc.controller=null;npc.currentLocation?.characters.Remove(npc);
        return !Members.Any(m=>m.Npc==npc);
    }
    public bool AttachCustomCompanion(string name) {
        if(Context.IsMultiplayer)return false;
        var npc=Game1.getCharacterFromName(name);
        if(npc?.currentLocation==null||!npc.modData.ContainsKey("stardewagent.together/custom-partner"))return false;
        var mate=Members.FirstOrDefault(m=>m.Npc==npc);
        if(mate==null) {
            mate=mod.SquadMateFactory.Create(npc);mod.RecruitmentManager.Recruit(mate,Game1.player,isSilent:true);
            if(!Members.Contains(mate))return false;
            managed.Add(Id(mate));stay.Add(Id(mate));mod.FollowerManager.ClearMateTaskAndReset(mate);
        }else if(managed.Add(Id(mate)))stay.Add(Id(mate));
        return true;
    }
}
public sealed partial class CompanionControl {
    private static int WorkCost(string skill)=>Together.Shared.CompanionLabor.Cost(skill);
    private static int LaborUsed(NPC npc) {
        if(!npc.modData.TryGetValue("stardewagent.together/labor",out var raw))return 0;
        var parts=raw.Split(':');return parts.Length==2&&int.TryParse(parts[0],out int day)&&day==Game1.Date.TotalDays&&int.TryParse(parts[1],out int used)?Math.Max(0,used):0;
    }
    private static object Labor(NPC npc)=>new{used=LaborUsed(npc),limit=180,remaining=Math.Max(0,180-LaborUsed(npc)),unit="custom daily labor points, NOT Farmer stamina",reset="native next game day"};
    private static void ValidateLabor(NPC npc,string skill) {
        if(npc.modData.ContainsKey("stardewagent.together/custom-partner")&&LaborUsed(npc)+WorkCost(skill)>180)throw new InvalidOperationException("partner_daily_labor_budget_exhausted");
    }
    private static void ChargeLabor(NPC npc,string skill) {
        if(npc.modData.ContainsKey("stardewagent.together/custom-partner")&&WorkCost(skill)>0)npc.modData["stardewagent.together/labor"]=$"{Game1.Date.TotalDays}:{LaborUsed(npc)+WorkCost(skill)}";
    }
}
