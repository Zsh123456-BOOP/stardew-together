using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    internal object ReadAutonomyDiagnostics()=>new {
        save_epoch=agentSaveEpoch,save_id=Game1.uniqueIDForThisGame.ToString(),day=Game1.Date.TotalDays,time=Game1.timeOfDay,
        status=Data.Autoplay.Status,detail=Data.Autoplay.Detail,model_pending=agentPending!=null&&!agentPending.IsCompleted,last_model_ms=agentLastLatency,
        native_wait=new{menu=Game1.activeClickableMenu?.GetType().Name,event_active=Game1.eventUp,minigame=Game1.currentMinigame?.GetType().Name,transition=Game1.fadeToBlack||Game1.locationRequest!=null,can_move=Game1.player.CanMove},
        actors=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).GroupBy(t=>t.spec.actor).Select(g=>new{actor=g.Key,tasks=g.Select(t=>new{t.spec.id,t.state,t.spec.purpose,t.error,t.command_id})}),
        work=semanticJobs.Values.Where(j=>j.status=="running").Select(j=>new{j.actor,j.goal,j.phase,j.location,j.completed,j.gained,j.deposited,j.requested,j.child_id}),
        business=ReadBusinessLedger(),campaign=Data.Autoplay.Campaign,farm_investment=Data.FarmInvestment,
        memory=new{timeline=agentSaveEpoch,visible_checkpoints=Data.Autoplay.Memory.Cursors.Count,day_summaries=Data.Autoplay.Memory.Days.Count,season_summaries=Data.Autoplay.Memory.Seasons.Count,pending_archive_writes=Data.Autoplay.Memory.Pending.Count,last_error=Data.Autoplay.Memory.LastError,active_promises=Data.SharedGoals.Where(g=>g.Status is "active" or "paused").Select(g=>new{g.Id,g.Title,g.Status,g.AutoBlockedReason})},
        budget=ModelRequestBudget.Status(),verification=new{Data.Autoplay.VerifiedActions,Data.Autoplay.SleepDays,meaning="这些是动作/睡觉证据计数，不代表已通过完整通关验收"}
    };
    internal string[] AutonomyPanelLines(bool memory) {
        var a=Data.Autoplay;var lines=new List<string>();
        if(memory) {
            lines.Add("记忆时间线："+agentSaveEpoch[..8]+" · 存档 "+Game1.uniqueIDForThisGame);
            lines.Add($"归档：{a.Memory.Days.Count} 日摘要 · {a.Memory.Seasons.Count} 季摘要 · {a.Memory.Pending.Count} 条待写");
            if(a.Memory.LastError.Length>0)lines.Add("归档阻碍："+a.Memory.LastError);
            lines.Add(ModelRequestBudget.Display());
            lines.Add("当前请求："+(agentPending!=null&&!agentPending.IsCompleted?"等待模型回答":"没有等待中的模型请求")+$" · 上次耗时 {agentLastLatency/1000:0.0} 秒");
            lines.Add("未完成共同目标：");
            foreach(var goal in Data.SharedGoals.Where(g=>g.Status is "active" or "paused").Take(3))lines.Add(goal.Title+" · "+goal.Status+(goal.AutoBlockedReason.Length>0?" · "+goal.AutoBlockedReason:""));
            lines.Add("预算是 Token 额度；尚未设置人民币价格，不把预留额度当实际账单。");
        } else {
            lines.Add($"原生时间 {Game1.timeOfDay/100:00}:{Game1.timeOfDay%100:00} · 已核验动作 {a.VerifiedActions} · 正常过夜 {a.SleepDays}");
            lines.Add("原生流程："+(Game1.currentMinigame?.GetType().Name??(Game1.eventUp?"剧情进行中":Game1.fadeToBlack||Game1.locationRequest!=null?"地图切换中":"可观察游戏世界")));
            foreach(var group in a.Schedule.Tasks.Where(t=>!t.Terminal).GroupBy(t=>t.spec.actor).Take(3)) {
                var task=group.FirstOrDefault(t=>t.state=="running")??group.First();
                lines.Add(group.Key+" · "+task.state+" · "+task.spec.purpose);
                var work=semanticJobs.Values.FirstOrDefault(j=>j.actor==group.Key&&j.status=="running");if(work!=null)lines.Add($"  {work.phase} · 完成 {work.completed} · 获得 {work.gained} · 已存 {work.deposited}");
            }
            var targets=a.Campaign.Targets;lines.Add($"长期目标：{targets.Count(t=>t.State=="complete")}/{targets.Count} 已核验 · {targets.Count(t=>t.State=="waiting")} 等待 · {targets.Count(t=>t.State=="blocked")} 需重排");
            foreach(var target in targets.Where(t=>t.State is "blocked" or "waiting").Take(3))lines.Add(target.Target+"："+target.Reason);
            lines.Add("F10 暂停；以上运行计数不能替代完整功能验收。");
        }
        return lines.ToArray();
    }
}
