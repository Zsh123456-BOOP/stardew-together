using System.Text.Json;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    // Native read-only wiring check; does not inject resources, goals, time or outcomes.
    private object OnDemandProbe() {
        var source=JsonSerializer.SerializeToElement(new{now=AgentSnapshot(),active_actors=ActiveActors.ToArray(),inventory=AgentToolRegistry.Inventory(),schedule=AgentPlanRead(true),operating_candidates=OperatingOpportunities(),prerequisites=TaskPrerequisites(),assets=FacilityAssets(),labor_budget=FarmLaborBudget(),planting_execution=PlantingExecutionFacts(),progression=DailyProgressDigest(true),memory=AgentMemoryContext(),service_hours=KnownServiceHours()},AgentJson.Options);
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
            ["work_parameters_complete"]=specs.Single(s=>s.Name=="work.run").Parameters.GetProperty("properties").TryGetProperty("stock_target",out _)
        };
        return new{checks,source,definitions,packed_context=view,field_revisions=ContextRevisions.Fields(source),scope="read-only native wiring, no paid model or injected game progress"};
    }
}
