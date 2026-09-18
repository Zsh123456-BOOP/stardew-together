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
        string basis=FailureKnowledge.Hash(AgentJson.Encode(new{agentGeneration,day=Game1.Date.TotalDays,time=Game1.timeOfDay,location=Game1.currentLocation.NameOrUniqueName,Game1.player.TilePoint,Game1.player.Stamina,revision=Data.Autoplay.Schedule.Revision,menu=Game1.activeClickableMenu?.GetType().Name,event_up=Game1.eventUp,bag=Game1.player.Items.Select(i=>new{id=i?.QualifiedItemId,count=i?.Stack,quality=i?.Quality}),operating=OperatingDecisionBasis()}));
        if(basis!=decisionSnapshotBasis){decisionSnapshotBasis=basis;decisionSnapshotPart=0;decisionSnapshot.Clear();}
        void Add(object section){var copy=JsonSerializer.SerializeToElement(section,AgentJson.Options);foreach(var field in copy.EnumerateObject())decisionSnapshot[field.Name]=field.Value.Clone();}
        var started=System.Diagnostics.Stopwatch.GetTimestamp();
        switch(decisionSnapshotPart) {
            case 0:Add(new{inventory_plan=InventoryPlanning()});break;
            case 1:Add(new{farm_cleanup=FarmMaintenanceSummary()});break;
            case 2:Add(new{day=AgentDay(true),progression=DailyProgressDigest()});break;
            case 3:decisionSnapshot["production_temporary"]=JsonSerializer.SerializeToElement(ProductionSummary(),AgentJson.Options);break;
            case 4:
                var opportunities=OperatingOpportunities();WriteBusinessLog("operating_candidates",AgentJson.Encode(opportunities));
                object ui=playerExecutor.OwnsFishing?new{type="executor_owned_fishing"}:agentTools.Execute("menu.read",JsonSerializer.SerializeToElement(new{}));
                Add(new{operating_candidates=opportunities,ui});break;
            case 5:Add(new{sleep_review=sleepReview,night=NightStatus(),protection_reasons=ProtectionReasons(),planting_execution=PlantingExecutionFacts(),commitments=OperationCommitments(),run_id=Data.Autoplay.RunId,start_day=Data.Autoplay.StartDay,verified_actions=Data.Autoplay.VerifiedActions,verified_normal_sleeps=Data.Autoplay.SleepDays,goal=Data.Autoplay.Goal,plan=Data.Autoplay.Plan,now=AgentSnapshot(),inventory=AgentToolRegistry.Inventory(),schedule=AgentPlanRead(true)});break;
            case 6:
                Add(new{business=new{production=decisionSnapshot["production_temporary"],policy=Data.Business,routine=Data.Autoplay.Routine,investment=InvestmentObservation(),planting_commitment=new{energy=PendingFarmEnergy(),recovery_nodes=Data.Autoplay.Schedule.Tasks.Where(t=>Data.FarmInvestment.Tasks.Contains(t.spec.id)&&t.state is "failed" or "blocked").Select(t=>new{t.spec.id,t.spec.tool,t.spec.args,t.error,t.spec.after})},pending_shipping_count=Game1.getFarm().getShippingBin(Game1.player).Count,note="farm.business_status查看产能与投资依据，算法已排任务不要重复提交"}});decisionSnapshot.Remove("production_temporary");break;
            case 7:Add(new{companions=AgentCompanions(true),deliberation=new{queries_without_progress=decisionPacing.QueriesWithoutProgress},decision_reasons=agentWakeReasons.ToArray(),recent=RecentAgentContext(),active_actors=ActiveActors.ToArray(),goals=GoalContext(),memory=AgentMemoryContext(),stamp=SnapshotStamp()});break;
        }
        double ms=(System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency;
        if(ms>=20)WriteBusinessLog("snapshot_section_slow",AgentJson.Encode(new{part=decisionSnapshotPart,ms}));
        if(++decisionSnapshotPart<8)return false;
        frozen=JsonSerializer.SerializeToElement(decisionSnapshot,AgentJson.Options);decisionSnapshotPart=0;decisionSnapshot.Clear();decisionSnapshotBasis="";return true;
    }
}
