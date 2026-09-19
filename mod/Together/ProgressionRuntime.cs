using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private Dictionary<string,string> progressDigestPrevious=new();
    private object DailyProgressDigest() {
        var p=Game1.player;
        var quests=p.questLog.Select(q=>new{id=NativeQuestIdentity.Id(q),title=q.questTitle,objective=q.currentObjective,complete=q.completed.Value,days_left=q.daysLeft.Value,description=q.questDescription,social=SocialObservation.Quest(q)}).ToArray();
        var current=quests.ToDictionary(q=>"quest:"+q.id,q=>AgentJson.Encode(q));
        foreach(int id in p.achievements)current["achievement:"+id]="earned";
        foreach(string mail in p.mailReceived)current["mail:"+mail]="received";
        var changes=current.Where(kv=>!progressDigestPrevious.TryGetValue(kv.Key,out var old)||old!=kv.Value).Select(kv=>new{id=kv.Key,value=kv.Value}).ToArray();
        progressDigestPrevious=current;
        if(changes.Length>0)Data.Autoplay.Record("native_progress_delta",AgentJson.Encode(new{day=Game1.Date.TotalDays,changes}));
        var achievements=Game1.achievements.Where(a=>!p.achievements.Contains(a.Key)).Select(a=>new{id=a.Key,title=a.Value.Split('^')[0],rule=System.Text.Json.JsonSerializer.SerializeToElement(AchievementRules.Native(a.Key))}).ToArray();
        double Ratio(System.Text.Json.JsonElement rule)=>rule.TryGetProperty("current",out var c)&&rule.TryGetProperty("target",out var t)?c.GetDouble()/Math.Max(1,t.GetDouble()):-1;
        return new{day=Game1.Date.TotalDays,date=new{year=Game1.year,season=Game1.currentSeason,day=Game1.dayOfMonth},route=Facts.Route,
            social=SocialObservation.Read(p),active_quests=quests.Take(12),quest_total=quests.Length,unread_mail=Game1.mailbox.ToArray(),
            achievements_earned=p.achievements.Count,achievements_missing=achievements.Length,
            nearby_achievements=achievements.OrderByDescending(a=>Ratio(a.rule)).Take(4),
            unlocks=new{mine_depth=p.deepestMineLevel,fishing_tool=AvailableTool<StardewValley.Tools.FishingRod>(),crafting_recipes=p.craftingRecipes.Count(),cooking_recipes=p.cookingRecipes.Count()},
            collection=new{shipped=p.basicShipped.Count(),fish=p.fishCaught.Count()},
            changes=changes.TakeLast(6),
            details=new{tasks="progress.read",all_targets="progress.catalog",dependencies="progress.dependencies",boards="quest_board.read"},
            note="原生事实，只提供比较依据，不强制任务/成就优先于经营。摘要未列出不代表不存在；Steam解锁未核验，未适配条件明确unknown。"};
    }
    internal object AgentProgression() {
        var p=Game1.player;
        return new {
            source="原生存档与 Data/Achievements；next_steps 是经营建议，不是另造的成就完成标记",
            policy="成就不按列表串行刷。优先今日农务、限时任务/季节窗口、当前可解锁能力，再积累长期目标材料；未解锁高级配方不能独占每天计划。",
            native=new{earned=p.achievements.ToArray(),lifetime_earnings=p.totalMoneyEarned,mine_depth=p.deepestMineLevel,skills=new{farming=p.FarmingLevel,mining=p.MiningLevel,foraging=p.ForagingLevel,fishing=p.FishingLevel,combat=p.CombatLevel},shipping_variety=p.basicShipped.Count(),crafting_variety=p.craftingRecipes.Pairs.Count(x=>x.Value>0)},
            missing_achievements=Game1.achievements.Where(a=>!p.achievements.Contains(a.Key)).Select(a=>new{id=a.Key,definition=a.Value}).ToArray(),
            active_quests=p.questLog.Where(q=>!q.completed.Value).Select(q=>new{id=NativeQuestIdentity.Id(q),title=q.questTitle,description=q.questDescription,social=SocialObservation.Quest(q)}).Take(12).ToArray(),
            social=SocialObservation.Read(p),campaign=Data.Autoplay.Campaign,
            next_steps=ProgressOpportunities(),
            note="伙伴劳动不会自动计入所有玩家技能或成就；只读观察原生结果，不改完成标记。"
        };
    }
}
