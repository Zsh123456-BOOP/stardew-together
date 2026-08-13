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
            next_steps=new[]{
                new{goal="维持农场并增加有效种植和收入",evidence=$"耕作等级 {p.FarmingLevel}；今日待浇 {Facts.DryCrops}、待收 {Facts.RipeCrops}、枯苗 {Facts.DeadCrops}",action="先补水/委派照料，再查当季种子、生长期与预算；只种能照顾的数量"},
                new{goal="原生任务、解锁与矿井检查点",evidence=$"玩家已到矿井 {p.deepestMineLevel} 层，下一建议检查点 {Math.Min(120,(p.deepestMineLevel/5+1)*5)} 层",action="查进度、入口和装备；玩家推进原生记录，伙伴协助采矿战斗；大厅不是矿层"},
                new{goal="资源与制作、出货收藏",evidence=$"已出货品种 {p.basicShipped.Count()}；已实际制作配方 {p.craftingRecipes.Pairs.Count(x=>x.Value>0)}",action="按已解锁配方、农场建设和真实成就缺口备料；预留种子/补给/任务物资，整理库存后继续"}
            },
            note="伙伴劳动不会自动计入所有玩家技能或成就；只读观察原生结果，不改完成标记。"
        };
    }
}
