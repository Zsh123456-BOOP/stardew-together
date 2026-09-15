using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class ModEntry {
    private static int DryFarmEnergy()=>Game1.getFarm().terrainFeatures.Values.OfType<HoeDirt>().Count(d=>d.crop!=null&&!d.crop.dead.Value&&d.state.Value!=1&&!d.readyForHarvest())*4;
    private static int AvailablePlantingEnergy()=>Math.Max(0,(int)Game1.player.Stamina-DailyBudget.EnergyReserve-DryFarmEnergy());
    // Optional gathering may use only energy left after existing care and queued
    // planting. Before today's shop quote, hold a bounded expansion allowance;
    // it is released when investment finishes or becomes unavailable.
    private FarmPlantPlan[] ApprovedPlantingPlans() {
        var investment=Data.FarmInvestment;
        return Data.Autoplay.Schedule.Tasks.Where(t=>t.spec.tool=="work.run"&&AgentToolRegistry.Text(t.spec.args,"goal")=="plant"&&(!t.Terminal||investment.Enabled&&investment.Phase=="blocked"&&investment.Tasks.Contains(t.spec.id)&&farmPlantPlans.TryGetValue(AgentToolRegistry.Text(t.spec.args,"plan_id"),out var heldPlan)&&Game1.player.Items.Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer())).Any(i=>i?.QualifiedItemId==heldPlan.Seed)))
            .Select(t=>farmPlantPlans.GetValueOrDefault(AgentToolRegistry.Text(t.spec.args,"plan_id")))
            .Where(p=>p!=null&&p.Day==Game1.Date.TotalDays&&p.Epoch==agentSaveEpoch&&p.Location=="Farm")
            .DistinctBy(p=>p!.Id).Select(p=>p!).ToArray();
    }
    private int PendingFarmEnergy() {
        int care=DryFarmEnergy();var plans=ApprovedPlantingPlans();
        var farm=Game1.getFarm();
        var preparation=plans.SelectMany(p=>p!.PreparationTiles.Count>0?p.PreparationTiles:p.Tiles).Distinct();
        int pending=preparation.Sum(t=>PlotClearCost(farm,new(t.X,t.Y)));
        foreach(var t in plans.SelectMany(p=>p!.Tiles).Distinct()) {
            var dirt=farm.terrainFeatures.GetValueOrDefault(new(t.X,t.Y)) as HoeDirt;
            if(dirt==null)pending+=4;
            if(dirt?.crop==null&&dirt?.state.Value!=1)pending+=4;
        }
        return care+pending;
    }
}
