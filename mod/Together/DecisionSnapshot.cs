using System.Text.Json;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private readonly Dictionary<string,JsonElement> decisionSnapshot=new();
    private string decisionSnapshotBasis="";
    private int decisionSnapshotPart;
    // Read/freeze each section on the game thread. Only detached JSON crosses to the model worker.
    // Invalidate the whole assembly when its actor/stock/queue basis changes between frames.
    private bool TryDecisionSnapshot(out JsonElement frozen) {
        frozen=default;
        string basis=HasPendingQueries?agentGeneration+":"+Game1.Date.TotalDays+":"+Data.Autoplay.Schedule.Revision:FailureKnowledge.Hash(AgentJson.Encode(new{agentGeneration,day=Game1.Date.TotalDays,time=Game1.timeOfDay,location=Game1.currentLocation.NameOrUniqueName,Game1.player.TilePoint,Game1.player.Stamina,revision=Data.Autoplay.Schedule.Revision,menu=Game1.activeClickableMenu?.GetType().Name,event_up=Game1.eventUp,bag=Game1.player.Items.Select(i=>new{id=i?.QualifiedItemId,count=i?.Stack,quality=i?.Quality}),operating=OperatingDecisionBasis()}));
        if(basis!=decisionSnapshotBasis){decisionSnapshotBasis=basis;decisionSnapshotPart=0;decisionSnapshot.Clear();}
        void Add(object section){var copy=JsonSerializer.SerializeToElement(section,AgentJson.Options);foreach(var field in copy.EnumerateObject())decisionSnapshot[field.Name]=field.Value.Clone();}
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        switch(decisionSnapshotPart) {
            case 0:Add(new{inventory_plan=InventoryPlanning()});break;
            case 1:Add(new{farm_cleanup=FarmMaintenanceSummary()});break;
            case 2:Add(new{day=AgentDay(true),progression=DailyProgressDigest(),service_hours=KnownServiceHours()});break;
            case 3:decisionSnapshot["production_temporary"]=JsonSerializer.SerializeToElement(ProductionSummary(),AgentJson.Options);break;
            case 4:
                var opportunities=OperatingOpportunities();WriteBusinessLog("operating_candidates",AgentJson.Encode(opportunities));
                object ui=playerExecutor.OwnsFishing?new{type="executor_owned_fishing"}:agentTools.Execute("menu.read",JsonSerializer.SerializeToElement(new{}));
                Add(new{operating_candidates=opportunities,decision_review=DecisionReviewContext(opportunities),ui});break;
            case 5:Add(new{work_profiles=Data.Autoplay.Memory.WorkProfileDay==Game1.Date.TotalDays?Data.Autoplay.Memory.WorkProfilesUntilDecision.Where(p=>p.Value>=Data.Autoplay.Decisions).Select(p=>p.Key).ToArray():Array.Empty<string>(),active_work=semanticJobs.Values.Where(j=>j.status=="running").Select(j=>new{j.command_id,j.actor,j.goal,j.location,j.phase}),native_tool_exchange=Data.Autoplay.ToolExchange.Messages(),assets=FacilityAssets(),labor_budget=FarmLaborBudget(),sleep_review=sleepReview,night=NightStatus(),protection_reasons=ProtectionReasons(),planting_execution=PlantingExecutionFacts(),purchase_status=SeedPurchaseStatus(),deliberation=new{rounds_without_native_progress=decisionPacing.QueriesWithoutProgress,next=decisionPacing.QueriesWithoutProgress>=3?"已多轮没有原生进展：使用现有事实行动；若等待明确营业/队列事件，用有界agent.wait，不反复补读同一事实。":"只查询影响下一步决定的缺失信息"},commitments=OperationCommitments(),run_id=Data.Autoplay.RunId,start_day=Data.Autoplay.StartDay,verified_actions=Data.Autoplay.VerifiedActions,verified_normal_sleeps=Data.Autoplay.SleepDays,goal=Data.Autoplay.Goal,plan=Data.Autoplay.Plan,now=AgentSnapshot(),inventory=AgentToolRegistry.Inventory(),schedule=AgentPlanRead(true)});break;
            case 6:
                Add(new{business=new{production=decisionSnapshot["production_temporary"],policy=Data.Business,routine=Data.Autoplay.Routine,investment=InvestmentObservation(),planting_commitment=new{energy=PendingFarmEnergy(),recovery_nodes=Data.Autoplay.Schedule.Tasks.Where(t=>Data.FarmInvestment.Tasks.Contains(t.spec.id)&&t.state is "failed" or "blocked").Select(t=>new{t.spec.id,t.spec.tool,t.spec.args,t.error,t.spec.after})},pending_shipping_count=Game1.getFarm().getShippingBin(Game1.player).Count,note="farm.business_status查看产能与投资依据，算法已排任务不要重复提交"}});decisionSnapshot.Remove("production_temporary");break;
            case 7:Add(new{equipped_tools=AgentToolDiscovery.LeasedTools(Data.Autoplay.Memory,Game1.Date.TotalDays,Data.Autoplay.Decisions),pending_queries=PendingQueries(),observation_backlog=new{pending=Data.Autoplay.Memory.Queries.Count(q=>!q.Delivered),read_via="query.read",note="有界摘要分批送达；未交付结果仍保存在原生checkpoint"},task_card=TaskCard(),prerequisites=TaskPrerequisites(),companions=AgentCompanions(true),deliberation=new{queries_without_progress=decisionPacing.QueriesWithoutProgress},decision_reasons=agentWakeReasons.ToArray(),recent=RecentAgentContext(),active_actors=ActiveActors.ToArray(),goals=GoalContext(),memory=AgentMemoryContext(),stamp=SnapshotStamp()});break;
        }
        double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        if(ms>=20)WriteBusinessLog("snapshot_section_slow",AgentJson.Encode(new{part=decisionSnapshotPart,ms}));
        if(++decisionSnapshotPart<8)return false;
        frozen=JsonSerializer.SerializeToElement(decisionSnapshot,AgentJson.Options);decisionSnapshotPart=0;decisionSnapshot.Clear();decisionSnapshotBasis="";return true;
    }
}
