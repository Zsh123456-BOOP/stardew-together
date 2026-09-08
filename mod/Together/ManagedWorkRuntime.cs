using System.Text.Json;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private int TeamStock(string item)=>AccessibleStock(item)+World().GetProperty("actors").EnumerateArray().Sum(a=>CargoItem(a,item));
    // The model requests a shared stock floor. Native quest collection and internal
    // production helpers retain their explicit incremental-count contracts.
    internal object StartManagedWork(JsonElement args) {
        string goal=AgentToolRegistry.Text(args,"goal");
        string actor=AgentToolRegistry.Text(args,"actor_id","player");
        if(Data.Business.Enabled&&goal=="store"&&actor=="player"&&Game1.player.freeSpotsInInventory()>=Math.Max(1,AgentToolRegistry.Number(args,"required_free_slots",0))&&Game1.timeOfDay<1800) {
            var skipped=(SemanticJob)StartSemanticWork(args);StopSemanticWork(skipped,"storage_not_required_capacity_available",true);return skipped;
        }
        string item=goal switch{"wood"=>"(O)388","stone"=>"(O)390","fiber"=>"(O)771","hardwood"=>"(O)709","resource"=>AgentToolRegistry.Text(args,"item"),_=>""};
        if(item.Length==0||!Data.Business.Enabled&&!args.TryGetProperty("stock_target",out _)||AgentToolRegistry.Text(args,"quest_id").Length>0||AgentToolRegistry.Text(args,"order_id").Length>0)return StartSemanticWork(args);
        UpdateOperatingTargets();
        int stock=TeamStock(item);
        int approved=Math.Max(Data.Operating.MaterialTargets.GetValueOrDefault(item),Data.Autoplay.Agenda.Resources.Where(r=>r.Item==item).Select(r=>r.Count).DefaultIfEmpty(0).Max());
        int? explicitTarget=args.TryGetProperty("stock_target",out _)?AgentToolRegistry.Number(args,"stock_target",0):null;
        int count=AgentToolRegistry.Number(args,"count",0);
        var quantity=WorkQuantity.Resolve(stock,approved,count,explicitTarget,Data.Business.Enabled);
        if(quantity.Error is {} error)throw new InvalidOperationException(error);
        int target=quantity.Target,missing=quantity.Missing;
        int actionLimit=999;
        if(actor!="player"&&missing>0) {
            RefreshFacts(true);var companion=WorkActor(actor);
            int remaining=companion.TryGetProperty("labor",out var labor)?labor.GetProperty("remaining").GetInt32():0;
            actionLimit=Together.Shared.CompanionLabor.OptionalAllowance(remaining,Facts.DryCrops,Facts.RipeCrops,Game1.timeOfDay)/4;
            if(actionLimit==0)throw new InvalidOperationException("partner_labor_reserved_for_farm_or_exhausted");
        }
        var values=args.Deserialize<Dictionary<string,JsonElement>>()!;
        values["count"]=JsonSerializer.SerializeToElement(Math.Clamp(Math.Min(actionLimit,missing),1,999));
        var job=(SemanticJob)StartSemanticWork(JsonSerializer.SerializeToElement(values));job.StockTarget=target;job.ActionLimit=actionLimit;
        job.evidence.Add(new{kind="shared_stock_target",item,target,owned_including_cargo=stock,missing});
        if(missing==0)StopSemanticWork(job,"shared_stock_target_already_met",true);
        return job;
    }
}
