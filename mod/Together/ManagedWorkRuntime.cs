using System.Text.Json;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private int TeamStock(string item)=>AccessibleStock(item)+World().GetProperty("actors").EnumerateArray().Sum(a=>CargoItem(a,item));
    // The model requests a shared stock floor. Native quest collection and internal
    // production helpers retain their explicit incremental-count contracts.
    internal object StartManagedWork(JsonElement args) {
        string goal=AgentToolRegistry.Text(args,"goal");
        string item=goal switch{"wood"=>"(O)388","stone"=>"(O)390","fiber"=>"(O)771","hardwood"=>"(O)709","resource"=>AgentToolRegistry.Text(args,"item"),_=>""};
        if(item.Length==0||!Data.Business.Enabled&&!args.TryGetProperty("stock_target",out _)||AgentToolRegistry.Text(args,"quest_id").Length>0||AgentToolRegistry.Text(args,"order_id").Length>0)return StartSemanticWork(args);
        int target=AgentToolRegistry.Number(args,"stock_target",AgentToolRegistry.Number(args,"count",20));
        if(target is <1 or >9999)throw new InvalidOperationException("stock_target_requires_1_to_9999");
        UpdateOperatingTargets();target=Math.Max(target,Data.Operating.MaterialTargets.GetValueOrDefault(item));
        int stock=TeamStock(item),missing=Math.Max(0,target-stock);
        var values=args.Deserialize<Dictionary<string,JsonElement>>()!;
        values["count"]=JsonSerializer.SerializeToElement(Math.Clamp(Math.Min(missing,AgentToolRegistry.Number(args,"count",999)),1,999));
        var job=(SemanticJob)StartSemanticWork(JsonSerializer.SerializeToElement(values));job.StockTarget=target;
        job.evidence.Add(new{kind="shared_stock_target",item,target,owned_including_cargo=stock,missing});
        if(missing==0)StopSemanticWork(job,"shared_stock_target_already_met",true);
        return job;
    }
}
