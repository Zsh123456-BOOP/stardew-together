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
        var relevant=Data.SharedGoals.Where(g=>g.Status is "active" or "paused").Select(g=>g.Entity).ToHashSet();
        foreach(var opportunity in lastOpportunities) {
            var a=JsonSerializer.SerializeToElement(opportunity.Args);
            foreach(string field in new[]{"entity","recipe"})if(a.TryGetProperty(field,out var value)&&value.ValueKind==JsonValueKind.String)relevant.Add(value.GetString()!);
        }
        var recipes=goalRecipes.Values.Where(r=>r.Kind=="craft"&&(relevant.Contains(r.Id)||relevant.Contains(r.Item)))
            .Concat(goalRecipes.Values.Where(r=>r.Kind=="craft"&&r.Known).OrderBy(r=>r.Inputs.Count(i=>AccessibleStock(i.Item)<i.Count)).Take(3)).DistinctBy(r=>r.Id).ToArray();
        return new {
            observed_day=Game1.Date.TotalDays,observed_time=Game1.timeOfDay,knowledge_ready=Knowledge.Ready,Knowledge.Revision,
            development=recipes.Select(r=>new{id=r.Id,r.Name,known=r.Known,made=Game1.player.craftingRecipes.GetValueOrDefault(r.Id[6..]),r.Supported,r.UnsupportedReason,r.Unlock,materials=r.Inputs.Select(i=>new{item=i.Item,required=i.Count,owned=AccessibleStock(i.Item),missing=Math.Max(0,i.Count-AccessibleStock(i.Item))}),dependencies=new{tool="progress.dependencies",args=new{id=r.Id}}}),
            discovery=new{tool="progress.catalog",note="此处是当前目标/可行机会与部分已知配方；未列出的主线、建筑、设备通过catalog和dependencies探索，不代表不可用。"},
            quests=native.Where(n=>n.kind.Contains("quest",StringComparison.OrdinalIgnoreCase)&&n.completed!=true).Take(6).Select(n=>new{n.id,n.title,n.requirements,n.evidence}),
            achievements=native.Where(n=>n.kind=="achievement"&&n.completed!=true).Take(3).Select(n=>new{n.id,n.title,n.requirements}),
            buildings=native.Where(n=>n.kind=="building"&&n.completed!=true).OrderBy(n=>System.Text.Json.JsonSerializer.SerializeToElement(n.requirements).TryGetProperty("cost",out var cost)?cost.GetInt32():int.MaxValue).Take(2).Select(n=>new{n.id,n.title,n.requirements}),
            instruction="每日检查主线、任务、成就、工具升级与生产设施；选择一项发展前置或说明暂缓原因。需要完整依赖用progress.dependencies。百科读取不等于解锁。"
        };
    }
}
