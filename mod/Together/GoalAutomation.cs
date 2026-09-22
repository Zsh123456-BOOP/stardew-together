using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private DateTime goalAutomationAt;
    internal object AgentGoalRun(JsonElement args) {
        var goal=Data.SharedGoals.FirstOrDefault(g=>g.Id==AgentToolRegistry.Text(args,"id"))??throw new InvalidOperationException("shared_goal_not_found");
        string mode=AgentToolRegistry.Text(args,"mode","run");
        if(mode is not ("run" or "pause"))throw new InvalidOperationException("invalid_goal_run_mode");
        if(mode=="run"&&goal.PlanVersion!=1)throw new InvalidOperationException("goal_schema_requires_review");
        if(mode=="run"&&goal.Status!="active")throw new InvalidOperationException("goal_not_active");
        goal.AutoExecute=mode=="run";goal.AutoBlockedReason="";goal.AutoBlockedConditions="";goal.AutoReviewDay=-1;
        if(mode=="pause")Data.Autoplay.Schedule.CancelPending(Data.Autoplay.Schedule.Tasks.Where(t=>t.spec.goal_id==goal.Id&&!t.Terminal&&t.state!="running").Select(t=>t.spec.id).ToArray());
        goalAutomationAt=DateTime.MinValue;
        return new{goal,executor_active=AutoplayRunning,note="只运行已支持且具备原生条件的依赖；开启目标不等于完成。暂停不会中断已经开始的原生消耗。"};
    }
    private string GoalCondition(SharedGoal goal) {
        if(goal.CapacityBlockedTool.Length>0) {
            try {
                var args=JsonSerializer.Deserialize<JsonElement>(goal.CapacityBlockedArgs);
                if(CapacityAdmission(goal.CapacityBlockedTool,args) is {Feasible:false} blocked)return "capacity:"+goal.CapacityBlockedTool+":"+blocked.Reason;
            }catch(InvalidOperationException){}
            goal.CapacityBlockedTool="";goal.CapacityBlockedArgs="{}";
        }
        return FailureKnowledge.Hash(AgentJson.Encode(new{
        goal.Fingerprint,money=Game1.player.Money,capacity=Data.Autoplay.Capacity.Version,
        recipes=Game1.player.craftingRecipes.Keys.OrderBy(k=>k).ToArray(),
        location=Game1.currentLocation.NameOrUniqueName,energy=(int)Game1.player.Stamina,health=Game1.player.health,
        sources=Game1.currentLocation.objects.Values.Where(o=>o.IsTwig()||o.IsBreakableStone()||o.IsWeeds()||ResourceRules.Nodes.ContainsKey(o.ItemId)).GroupBy(o=>o.ItemId).Select(g=>new{item=g.Key,count=g.Count()}),
        stock=Facts.Stock.Select(s=>new{s.Item,s.Count,s.Quality}),
        machines=GoalMachines().Select(m=>new{location=m.Location.NameOrUniqueName,tile=m.Object.TileLocation,item=m.Object.QualifiedItemId,ready=m.Object.readyForHarvest.Value,held=m.Object.heldObject.Value?.QualifiedItemId}),
        crops=Game1.getFarm().terrainFeatures.Pairs.Where(t=>t.Value is StardewValley.TerrainFeatures.HoeDirt {crop:not null}).Select(t=>{var d=(StardewValley.TerrainFeatures.HoeDirt)t.Value;return new{tile=t.Key,item=d.crop.indexOfHarvest.Value,ready=d.readyForHarvest(),dead=d.crop.dead.Value,water=d.state.Value};}),
        tools=Game1.player.Items.OfType<Tool>().Select(t=>new{t.QualifiedItemId,t.UpgradeLevel})
    }));
    }
    private void TickGoalAutomation() {
        if(!AutoplayRunning||DateTime.UtcNow<goalAutomationAt)return;goalAutomationAt=DateTime.UtcNow.AddSeconds(2);
        if(agentLabProbe&&labEarlyStorage)EnsureStorageGoal();
        if(playerExecutor.Busy||Game1.activeClickableMenu!=null||Game1.eventUp||Game1.fadeToBlack||Game1.locationRequest!=null||!Game1.player.CanMove||Game1.player.UsingTool||Game1.timeOfDay>=2200||Game1.player.health<35)return;
        if(AgentActorHasWork("player"))return;
        foreach(var goal in Data.SharedGoals.Where(g=>g.AutoExecute&&g.Status=="active").OrderBy(g=>g.AutoReviewDay).ThenBy(g=>g.AutoReviewMinute).ToArray()) {
            try {
                if(goal.PlanVersion!=1){goal.AutoExecute=false;goal.AutoBlockedReason="goal_schema_requires_review";continue;}
                RefreshFacts(true);if(goal.Status!="active")continue;
                string condition=GoalCondition(goal);
                int minute=DailyBudget.Minutes(Game1.timeOfDay);
                if(goal.AutoBlockedConditions==condition)continue;
                var batch=AgentGoalPrepare(JsonSerializer.SerializeToElement(new{id=goal.Id}));
                goal.AutoReviewDay=Game1.Date.TotalDays;goal.AutoReviewMinute=minute;
                if(batch.tasks.Count==0) {
                    string reason=batch.gaps.Count==0?"waiting_for_native_conditions_or_processing":AgentJson.Encode(batch.gaps);
                    bool changed=goal.AutoBlockedReason!=reason||goal.AutoBlockedConditions!=condition;
                    goal.AutoBlockedReason=reason;goal.AutoBlockedConditions=condition;
                    if(changed){Data.Autoplay.Record("goal_waiting",AgentJson.Encode(new{goal=goal.Id,reason}));WakeAgent("goal_waiting:"+goal.Id);}
                    continue;
                }
                goal.AutoBlockedReason="";goal.AutoBlockedConditions="";
                if(Data.Autoplay.Schedule.Tasks.Count+batch.tasks.Count>180)Data.Autoplay.Schedule.Archive();
                Data.Autoplay.Schedule.Submit("goal-auto-"+Guid.NewGuid().ToString("N"),Data.Autoplay.Schedule.Revision,batch.tasks,Game1.Date.TotalDays,ordered:true);
                Data.Autoplay.Record("goal_batch",AgentJson.Encode(new{goal=goal.Id,tasks=batch.tasks,source="deterministic_dependency_execution"}));
                TickAgentSchedule();break;
            }catch(Exception e){goal.AutoBlockedConditions=GoalCondition(goal);goal.AutoBlockedReason=e is InvalidOperationException?e.Message:e.GetType().Name;Data.Autoplay.Record("goal_blocked",AgentJson.Encode(new{goal=goal.Id,goal.AutoBlockedReason}));WakeAgent("goal_blocked:"+goal.Id);}
        }
    }
}
