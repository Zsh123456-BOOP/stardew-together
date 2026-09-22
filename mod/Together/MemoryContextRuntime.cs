using System.Text.Json;
using StardewValley;
namespace Together;

public sealed partial class ModEntry {
    private object TaskCard()=>new {
        waiting_query_drafts=Data.Autoplay.Memory.QueryDrafts.Select(d=>new{call=d.GetProperty("call").GetProperty("tool"),read_via="plan.read"}),schema=1,Data.Autoplay.Goal,Data.Autoplay.StartDay,Data.Autoplay.SleepDays,Data.Autoplay.TrialTargetDay,Data.Autoplay.TrialTargetSleeps,
        plan_read_via="context.read section=plan",Data.Autoplay.ProfessionChoices,
        active=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).Select(t=>new{t.spec.id,t.spec.tool,t.state,t.wait_reason,t.spec.after}),
        goals=Data.SharedGoals.Where(g=>g.Status is "active" or "paused").Select(g=>new{g.Id,g.Title,g.Status,g.Summary}),
        source="native_save_checkpoint; planned work is not verified completion"
    };
    private object PlanningKnowledge(string id)=>Knowledge.Query("",id);
    private object TaskPrerequisites() {
        ReadGoalRecipes();
        var relevant=Data.SharedGoals.Where(g=>g.Status is "active" or "paused").Select(g=>g.Entity).ToHashSet();
        foreach(var pursuit in Data.Autoplay.Campaign.Targets.Where(t=>!t.CompletionObserved))relevant.Add(pursuit.Target);
        var recipes=goalRecipes.Values.Where(r=>r.Kind=="craft"&&(relevant.Contains(r.Id)||relevant.Contains(r.Item)))
            .DistinctBy(r=>r.Id).ToArray();
        return new {
            observed_day=Game1.Date.TotalDays,observed_time=Game1.timeOfDay,knowledge_ready=Knowledge.Ready,Knowledge.Revision,
            development=recipes.Select(r=>new{id=r.Id,r.Name,known=r.Known,made=Game1.player.craftingRecipes.GetValueOrDefault(r.Id[6..]),r.Supported,r.UnsupportedReason,r.Unlock,materials=r.Inputs.Select(i=>new{item=i.Item,required=i.Count,owned=AccessibleStock(i.Item),missing=Math.Max(0,i.Count-AccessibleStock(i.Item))}),dependencies=new{tool="progress.dependencies",args=new{id=r.Id}}}),
            discovery=new{tool="progress.catalog",note="其他配方/建筑/成就按需查询；成就next_only按同指标各线取下一档。详情用progress.dependencies id。"},
            selected_targets=Data.Autoplay.Campaign.Targets.Where(t=>!t.CompletionObserved).Select(t=>new{t.Target,t.State,t.Reason,t.RecoveryCondition}),
            quests_field="progression.active_quests",achievements_field="progression.selected_achievements"
        };
    }
}
