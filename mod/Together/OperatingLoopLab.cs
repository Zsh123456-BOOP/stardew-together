using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;
namespace Together;
public sealed partial class ModEntry {
    // Only invoked by RunLabScenario's EnableLab + world + AgentLab gates.
    private object OperatingLoopFixture() {
        if(playerExecutor.Busy||WorkActorBusy("player"))throw new InvalidOperationException("fixture_requires_idle_player");
        var checks=new Dictionary<string,bool>();var samples=new Dictionary<string,object>();
        checks["unknown_resource_is_local"]=RecoverableExecutionFailure("resource_item_has_no_known_native_node_route");
        checks["invalid_fish_parameters_are_local"]=RecoverableExecutionFailure("fish_trip_invalid_count:expected_1_to_100")&&RecoverableExecutionFailure("fish_trip_invalid_item:use_native_QID");
        foreach(string item in new[]{"(O)388","(O)390","(O)771","(O)709"}) {
            var spec=new AgentTaskSpec{id="resource-probe",tool="work.run",args=JsonSerializer.SerializeToElement(new{goal="resource",item,count=1})};PrepareOperation(spec);
            checks["resource_alias:"+item]=AgentToolRegistry.Text(spec.args,"goal")==ResourceRules.WorkKind(item);
        }
        string policy=AgentJson.Encode(Data.Business);ConfigureBusiness(JsonSerializer.SerializeToElement(new{}));checks["empty_business_query_is_read_only"]=policy==AgentJson.Encode(Data.Business);
        foreach(string section in new[]{"farm_cleanup","service_hours","inventory_plan","companions","progression","business","day","schedule","recent","memory","goals","operating_candidates","sleep_review","plan","task_card"}) {ReadContextSection(JsonSerializer.SerializeToElement(new{section}));checks["readback:"+section]=true;}
        var invalid=JsonSerializer.SerializeToElement(AgentProgressCatalog(JsonSerializer.SerializeToElement(new{kind="recipe"})),AgentJson.Options);checks["invalid_catalog_kind_explained"]=invalid.GetProperty("status").GetString()=="invalid_kind";
        samples["production"]=ProductionSummary();samples["coverage"]=GoalRuleCoverage(JsonSerializer.SerializeToElement(new{}));
        var coverage=JsonSerializer.SerializeToElement(samples["coverage"],AgentJson.Options).GetProperty("coverage");checks["unparsed_machine_rules_visible"]=coverage.GetProperty("unparsed").GetInt32()>0;
        var original=Data.Autoplay.Schedule;int day=Game1.dayOfMonth,time=Game1.timeOfDay;
        try {
            Data.Autoplay.Schedule=new(){Finished=CaptureScheduledOutcome};var s=Data.Autoplay.Schedule;
            s.Submit("fixture-future",0,new(){new(){id="future",tool="player.social",not_before=1000},new(){id="travel",tool="player.travel",sequence_after=new(){"future"}}},Game1.Date.TotalDays);
            Game1.timeOfDay=800;checks["future_sequence_does_not_occupy_actor"]=!OperationActorOccupied("player")&&s.Ready(Game1.Date.TotalDays,800).Count==0;
            s.Finish(s.Tasks[0],"partial","fixture_partial",AgentJson.Encode(new{status="partial",requested=5,completed=2,remaining=3,source="lab_injected_receipt_not_native_work"}));
            var observed=Data.Autoplay.Memory.Queries.Last();checks["partial_terminal_outbox"]=observed.Kind=="outcome"&&observed.Result.GetProperty("remaining").GetInt32()==3;
            Game1.dayOfMonth=3;Game1.timeOfDay=610;Data.Autoplay.Schedule=new(){Finished=CaptureScheduledOutcome};operationWindowStamp="";
            Data.Autoplay.Schedule.Submit("fixture-closed",0,new(){new(){id="closed",tool="player.service",args=JsonSerializer.SerializeToElement(new{location="SeedShop",service="shop"})}},Game1.Date.TotalDays);
            PrepareServiceWindows();var closed=Data.Autoplay.Schedule.Tasks.Single();
            checks["native_wednesday_window_terminal"]=closed.state=="blocked"&&closed.error=="service_window_unavailable_today";
            checks["closed_service_outbox"]=Data.Autoplay.Memory.Queries.Last().Result.GetProperty("reason").GetString()=="weekly_closed";
            checks["closed_service_learned"]=Data.Autoplay.Operations.Constraints.Any(c=>c.Subject=="SeedShop");samples["closed"]=closed;
        }finally{Game1.dayOfMonth=day;Game1.timeOfDay=time;Data.Autoplay.Schedule=original;operationWindowStamp="";}
        var farm=Game1.getFarm();var tile=new Vector2(64,18);var chestTile=new Vector2(65,18);var oldTerrain=farm.terrainFeatures.GetValueOrDefault(tile);var oldObject=farm.objects.GetValueOrDefault(tile);var oldChest=farm.objects.GetValueOrDefault(chestTile);var upgrade=Game1.player.toolBeingUpgraded.Value;
        var field=new FarmPlantPlan{Id="fixture-clear-soil",Epoch=agentSaveEpoch,Day=Game1.Date.TotalDays,Location="Farm",Seed="(O)472",Tiles=new(){new(64,18)},PreparationTiles=new(){new(64,18)}};
        try {
            farm.objects.Remove(tile);farm.terrainFeatures[tile]=new HoeDirt(1,farm);Game1.player.toolBeingUpgraded.Value=new Axe();farmPlantPlans[field.Id]=field;
            var chest=new Chest(true);chest.modData[WorkChestRole]="output";chest.Items.Add(new MilkPail());farm.objects[chestTile]=chest;
            checks["milk_pail_retrievable_from_shared_chest"]=AvailableTools<MilkPail>().Any(t=>chest.Items.Contains(t));
            var spec=new ScheduledAgentTask{spec=new(){id="fixture-plant",tool="work.run",args=JsonSerializer.SerializeToElement(new{goal="plant",plan_id=field.Id})}};
            var kit=DescribeKit(spec);checks["wet_cleared_plot_does_not_require_axe_hoe_or_can"]=!kit.Needs.Any(n=>n.Label is "斧头" or "锄头" or "水壶");
            ((HoeDirt)farm.terrainFeatures[tile]).state.Value=0;checks["dry_cleared_plot_requires_can"]=DescribeKit(spec).Needs.Any(n=>n.Label=="水壶");
        }finally{farmPlantPlans.Remove(field.Id);Game1.player.toolBeingUpgraded.Value=upgrade;if(oldTerrain==null)farm.terrainFeatures.Remove(tile);else farm.terrainFeatures[tile]=oldTerrain;if(oldObject==null)farm.objects.Remove(tile);else farm.objects[tile]=oldObject;if(oldChest==null)farm.objects.Remove(chestTile);else farm.objects[chestTile]=oldChest;}
        string archived=CaptureObservation("context.read",JsonSerializer.SerializeToElement(new{durable="retained beyond live window"}));
        Data.Autoplay.Memory.Queries.RemoveAll(q=>q.Id==archived);var restored=JsonSerializer.SerializeToElement(ReadQueryResult(JsonSerializer.SerializeToElement(new{id=archived})),AgentJson.Options);
        checks["archived_query_id_readback"]=restored.GetProperty("fields")[0].GetProperty("value").GetString()=="retained beyond live window";
        int failures=Data.Autoplay.Survival.ModelFailures;var mode=Data.Autoplay.Survival.Mode;var states=original.Tasks.Select(t=>t.state).ToArray();
        try {
            ModelUnavailable(new InvalidOperationException("context_essential_state_exceeds_budget:fixture"),null,false);
            checks["local_context_failure_does_not_sleep_cancel_or_count_remote"]=Data.Autoplay.Survival.Mode==mode&&Data.Autoplay.Survival.ModelFailures==failures&&states.SequenceEqual(original.Tasks.Select(t=>t.state))&&decisionBlockedReason?.StartsWith("local_context_pack")==true;
        }finally{decisionBlockedReason=null;}
        return new{checks,samples,performance=Performance(),scope="isolated runtime wiring and native state observations; synthetic terminal fixtures are not economic or long-run evidence"};
    }
}
