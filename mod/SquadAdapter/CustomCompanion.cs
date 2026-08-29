using StardewValley;
using StardewModdingAPI;
namespace TheStardewSquad;
public sealed partial class CompanionControl {
    public bool AttachCustomCompanion(string name) {
        if(Context.IsMultiplayer)return false;
        var npc=Game1.getCharacterFromName(name);
        if(npc?.currentLocation==null||!npc.modData.ContainsKey("stardewagent.together/custom-partner"))return false;
        var mate=Members.FirstOrDefault(m=>m.Npc==npc);
        if(mate==null) {
            mate=mod.SquadMateFactory.Create(npc);mod.RecruitmentManager.Recruit(mate,Game1.player,isSilent:true);
            if(!Members.Contains(mate))return false;
            managed.Add(Id(mate));stay.Add(Id(mate));mod.FollowerManager.ClearMateTaskAndReset(mate);
        }else managed.Add(Id(mate));
        return true;
    }
}
public sealed partial class CompanionControl {
    private static int WorkCost(string skill)=>skill switch {"water" or "pet"=>1,"harvest" or "forage"=>2,"mine" or "clear"=>4,"fish"=>12,"plant" or "till" or "feed" or "tend" or "collect" or "refill"=>2,_=>0};
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
