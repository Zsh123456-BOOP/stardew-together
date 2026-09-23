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
        if(goal=="cleanup"&&!Data.Maintenance.Orders.Any(o=>o.Id==AgentToolRegistry.Text(args,"cleanup_id")&&o.Status=="active"))
            return new{status="blocked",stop_reason="cleanup_order_not_active",executed=false,next="先用farm.cleanup创建或恢复明确范围的清理单；播种清地用farm.plan后work.run plant",orders=Data.Maintenance.Orders.Select(o=>new{o.Id,o.Status})};
        if(goal=="storage_expand"&&actor=="player"&&OperatingDecisionPolicy.ReuseStorage(SharedStorage().Count(),ExistingStorageAcceptsCargo(),args.TryGetProperty("additional",out var extra)&&extra.ValueKind==JsonValueKind.True))
            return new{status="succeeded",completed=0,requested=0,stop_reason="existing_storage_available",native_progress_evidence=false,assets=FacilityAssets(),next="work.run goal=store；未新增设施，未移动物品"};
        string dependency=AgentToolRegistry.Text(args,"goal_id");
        if(dependency.Length>0) {
            RefreshFacts(true);
            var owner=Data.SharedGoals.FirstOrDefault(g=>g.Id==dependency&&g.Status=="active")??throw new InvalidOperationException("active_dependency_goal_required");
            string material=AgentToolRegistry.Text(args,"item");int requested=AgentToolRegistry.Number(args,"count",0);
            int dependencyMissing=owner.Nodes.Where(n=>n.Kind=="gather"&&n.Item==material).Sum(n=>n.ToPrepare);
            if(requested<1||requested>dependencyMissing)throw new InvalidOperationException("dependency_quantity_changed_replan");
            return StartSemanticWork(args);
        }
        // Explicit storage requests belong to the model; no free-slot policy veto.
        string item=goal switch{"wood"=>"(O)388","stone"=>"(O)390","fiber"=>"(O)771","hardwood"=>"(O)709","resource"=>AgentToolRegistry.Text(args,"item"),_=>""};
        if(item.Length==0||AgentToolRegistry.Text(args,"quest_id").Length>0||AgentToolRegistry.Text(args,"order_id").Length>0)return StartSemanticWork(args);
        UpdateOperatingTargets();
        int stock=TeamStock(item);
        int approved=Math.Max(Data.Operating.MaterialTargets.GetValueOrDefault(item),Data.Autoplay.Agenda.Resources.Where(r=>r.Item==item).Select(r=>r.Count).DefaultIfEmpty(0).Max());
        int? explicitTarget=args.TryGetProperty("stock_target",out _)?AgentToolRegistry.Number(args,"stock_target",0):null;
        int count=AgentToolRegistry.Number(args,"count",0);
        var quantity=WorkQuantity.Resolve(stock,approved,count,explicitTarget,Data.Business.Enabled);
        if(quantity.Error is {} error)throw new InvalidOperationException(error);
        int target=quantity.Target,missing=quantity.Missing;
        CheckResourceReview(args,item,stock,approved,missing);
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
