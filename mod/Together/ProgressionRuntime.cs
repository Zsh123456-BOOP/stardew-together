using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    internal object AgentProgression() {
        var p=Game1.player;
        return new {
            source="原生存档与 Data/Achievements；next_steps 是经营建议，不是另造的成就完成标记",
            policy="成就不按列表串行刷。优先今日农务、限时任务/季节窗口、当前可解锁能力，再积累长期目标材料；未解锁高级配方不能独占每天计划。",
            native=new{earned=p.achievements.ToArray(),lifetime_earnings=p.totalMoneyEarned,mine_depth=p.deepestMineLevel,skills=new{farming=p.FarmingLevel,mining=p.MiningLevel,foraging=p.ForagingLevel,fishing=p.FishingLevel,combat=p.CombatLevel},shipping_variety=p.basicShipped.Count(),crafting_variety=p.craftingRecipes.Pairs.Count(x=>x.Value>0)},
            missing_achievements=Game1.achievements.Where(a=>!p.achievements.Contains(a.Key)).Select(a=>new{id=a.Key,definition=a.Value}).ToArray(),
            active_quests=p.questLog.Where(q=>!q.completed.Value).Select(q=>new{id=q.id.Value,title=q.questTitle,description=q.questDescription}).Take(12).ToArray(),
            next_steps=ProgressOpportunities(),
            note="伙伴劳动不会自动计入所有玩家技能或成就；只读观察原生结果，不改完成标记。"
        };
    }
}
