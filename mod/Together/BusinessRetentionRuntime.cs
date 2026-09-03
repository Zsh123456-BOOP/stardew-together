using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private bool ApprovedMaterialSale(string item)=>!BusinessRetention.Materials.Contains(item)||Data.Operating.Production.SurplusDay==Game1.Date.TotalDays&&Data.Operating.Production.SaleItems.Contains(item);
    private MaterialAllocation AllocateMaterial(string item,Dictionary<string,int>? processing=null) {
        int owned=AccessibleStock(item),committed=Math.Max(0,owned-DisposableStock(item));
        int operations=(processing??BusinessRawReserves()).GetValueOrDefault(item);
        if(item=="(O)178")operations=Math.Max(operations,Math.Max(0,Game1.getFarm().getAllFarmAnimals().Count()*Data.Business.FeedDays-Facts.HayInSilo));
        int target=Math.Max(Data.Operating.MaterialTargets.GetValueOrDefault(item),Data.Autoplay.Agenda.Resources.Where(r=>r.Item==item).Select(r=>r.Count).DefaultIfEmpty(0).Max());
        return ProductionAllocation.Split(owned,committed,operations,target);
    }
    private int SaleFloor(string item){var a=AllocateMaterial(item);return a.Committed+a.Operations;}
    private void CheckSaleReserves(IReadOnlyDictionary<Item,int> consumed) {
        if(!Data.Business.Enabled)return;
        UpdateOperatingTargets();var processing=BusinessRawReserves();
        foreach(var group in consumed.GroupBy(p=>p.Key.QualifiedItemId)) {
            var a=AllocateMaterial(group.Key,processing);
            if(!ApprovedMaterialSale(group.Key))throw new InvalidOperationException($"sale_requires_surplus_decision:{group.Key}:free={a.Free}:use_farm.production_surplus_after_reviewing_uses");
            if(group.Sum(p=>p.Value)>a.Free)throw new InvalidOperationException($"sale_would_consume_operating_reserve:{group.Key}:stock={a.Owned}:committed={a.Committed}:operations={a.Operations}:free={a.Free}");
        }
    }
}
