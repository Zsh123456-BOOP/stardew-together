using System.Text.Json;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    // Native read-only wiring check; does not inject resources, goals, time or outcomes.
    private object OnDemandProbe() {
        var source=JsonSerializer.SerializeToElement(new{decision_review=DecisionReviewContext(OperatingOpportunities()),now=AgentSnapshot(),active_actors=ActiveActors.ToArray(),inventory=AgentToolRegistry.Inventory(),schedule=AgentPlanRead(true),operating_candidates=OperatingOpportunities(),prerequisites=TaskPrerequisites(),assets=FacilityAssets(),labor_budget=FarmLaborBudget(),planting_execution=PlantingExecutionFacts(),purchase_status=SeedPurchaseStatus(),progression=DailyProgressDigest(true),memory=AgentMemoryContext(),service_hours=KnownServiceHours()},AgentJson.Options);
        var selected=AgentToolDiscovery.Select(AgentToolRegistry.Catalog,source);var specs=ToolSpecs.Select(selected,source);var definitions=NativeToolProtocol.Definitions(specs);
        int reserved=ContextBudget.Estimate(AgentJson.Encode(definitions))+2200;var packed=ContextBudget.Pack(source,Math.Max(256,30000-reserved),Math.Max(256,16000-reserved));
        var view=JsonSerializer.Deserialize<JsonElement>(packed.Json);var next=AchievementView.Next(AchievementSummaries());
        var batch=JsonSerializer.SerializeToElement(ReadContextSection(JsonSerializer.SerializeToElement(new{sections=new[]{"assets","labor_budget"}})),AgentJson.Options);
        var catalog=JsonSerializer.SerializeToElement(AgentProgressCatalog(JsonSerializer.SerializeToElement(new{kind="achievement",next_only=true,limit=80})),AgentJson.Options);
        var checks=new Dictionary<string,bool>{
            ["native_next_achievement_series_unique"]=next.Select(r=>r.GetProperty("series").GetString()).Distinct().Count()==next.Length,
            ["native_achievement_summary_has_no_full_definition"]=!AgentJson.Encode(catalog).Contains("native_definition"),
            ["native_catalog_matches_selector"]=catalog.GetProperty("total").GetInt32()==next.Length,
            ["unselected_achievement_details_absent"]=!view.GetProperty("prerequisites").TryGetProperty("achievements",out _),
            ["batch_context_returns_both_current_facts"]=batch.TryGetProperty("assets",out _)&&batch.TryGetProperty("labor_budget",out _),
            ["current_inventory_preserved"]=view.GetProperty("inventory").GetRawText()==source.GetProperty("inventory").GetRawText(),
            ["current_care_budget_preserved"]=view.GetProperty("labor_budget").GetRawText()==source.GetProperty("labor_budget").GetRawText(),
            ["decision_review_visible"]=view.GetProperty("decision_review").TryGetProperty("sleep_alternatives",out _),
            ["sleep_and_pause_review_schema_equal"]=specs.Single(s=>s.Name=="player.sleep").Parameters.GetRawText()==specs.Single(s=>s.Name=="agent.pause").Parameters.GetRawText(),
            ["native_zero_energy_visit_cannot_be_rejected_for_energy"]=SleepAlternatives(OperatingOpportunities()).Where(o=>o.energy==0).All(o=>DecisionReviewPolicy.Sleep(JsonSerializer.SerializeToElement(new{reason="体力不足选择收工",alternatives=new[]{new{id=o.id,because="energy",detail="认为没有体力进行该活动"}}}),Game1.player.Stamina,DailyBudget.WorkMinutes(Game1.timeOfDay,ReturnReserve()),new[]{o})?.StartsWith("decision_review:energy_contradiction:")==true),
            ["shop_windows_retained_before_opening"]=view.GetProperty("service_hours").EnumerateArray().Any(s=>s.GetProperty("location").GetString()=="SeedShop"&&s.TryGetProperty("recheck_at",out _)),
            ["purchase_manifest_visible"]=view.GetProperty("purchase_status").TryGetProperty("items",out _),
            ["sleep_future_value_contract"]=specs.Single(s=>s.Name=="player.sleep").Parameters.GetProperty("properties").GetProperty("alternatives").GetProperty("items").GetProperty("properties").TryGetProperty("value",out _),
            ["purchase_additional_reason_contract"]=new[]{"player.buy","player.procure","farm.select_seeds"}.All(name=>NativeToolProtocol.LegacySpec(name,AgentToolRegistry.Catalog[name]).Parameters.GetProperty("properties").TryGetProperty("additional_reason",out _)),
            ["work_parameters_complete"]=specs.Single(s=>s.Name=="work.run").Parameters.GetProperty("properties").TryGetProperty("stock_target",out _)
        };
        return new{checks,source,definitions,packed_context=view,field_revisions=ContextRevisions.Fields(source),scope="read-only native wiring, no paid model or injected game progress"};
    }
}
