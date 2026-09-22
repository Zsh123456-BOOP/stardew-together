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
        // Small, fresh overview ensures the model sees development outside today's routine.
        var native=ReadNativeGoalRows();ReadGoalRecipes();
        return new {
            observed_day=Game1.Date.TotalDays,observed_time=Game1.timeOfDay,knowledge_ready=Knowledge.Ready,Knowledge.Revision,
            development=goalRecipes.Values.Where(r=>r.Kind=="craft"&&(DataLoader.Machines(Game1.content).ContainsKey(r.Item)||ItemRegistry.Create(r.Item) is StardewValley.Object o&&(o.IsSprinkler()||o.IsScarecrow()))).OrderByDescending(r=>Game1.player.craftingRecipes.ContainsKey(r.Id[6..])).ThenBy(r=>r.Inputs.Count(i=>AccessibleStock(i.Item)<i.Count)).Take(6).Select(r=>new{id=r.Id,known=Game1.player.craftingRecipes.ContainsKey(r.Id[6..]),made=Game1.player.craftingRecipes.GetValueOrDefault(r.Id[6..]),knowledge=PlanningKnowledge(r.Id),dependencies=new{tool="progress.dependencies",args=new{id=r.Id}},note="未知解锁需查依赖，未授予配方"}),
            quests=native.Where(n=>n.kind.Contains("quest",StringComparison.OrdinalIgnoreCase)&&n.completed!=true).Take(6).Select(n=>new{n.id,n.title,n.requirements,n.evidence}),
            achievements=native.Where(n=>n.kind=="achievement"&&n.completed!=true).Take(3).Select(n=>new{n.id,n.title,n.requirements}),
            buildings=native.Where(n=>n.kind=="building"&&n.completed!=true).OrderBy(n=>System.Text.Json.JsonSerializer.SerializeToElement(n.requirements).TryGetProperty("cost",out var cost)?cost.GetInt32():int.MaxValue).Take(2).Select(n=>new{n.id,n.title,n.requirements}),
            instruction="每日检查主线、任务、成就、工具升级与生产设施；选择一项发展前置或说明暂缓原因。需要完整依赖用progress.dependencies。百科读取不等于解锁。"
        };
    }
}
