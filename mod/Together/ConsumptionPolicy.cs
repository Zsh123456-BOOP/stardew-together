using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private List<Requirement> ConsumptionRequirements(SharedGoal? owner=null,bool protectProgress=false,string progressOwner="") {
        var projects=Data.Projects.Where(p=>p.Status=="active"&&p.Kind!=progressOwner).ToArray();
        var needs=projects.SelectMany(p=>p.Needs).Concat(Data.SharedGoals.Where(g=>g.Status=="active"&&g!=owner).SelectMany(g=>g.Reserved)).ToList();
        if(protectProgress) {
            // Encyclopedia visibility is not a resource commitment. In farm
            // management mode, unselected bundles must not reserve every log
            // before the first chest. Explicit projects are protected above;
            // enabling the progression campaign retains its wider protection.
            if(Data.Autoplay.Campaign.Enabled)
                needs.AddRange(Facts.Bundles.Where(b=>!b.Complete&&"bundle:"+b.Id!=progressOwner&&!projects.Any(p=>p.Kind=="bundle:"+b.Id)).SelectMany(b=>b.Missing));
            needs.AddRange(Facts.Goals.Where(g=>!g.Complete&&g.Id!=progressOwner&&g.Kind!="craft"&&!projects.Any(p=>p.Kind==g.Id)).SelectMany(g=>g.Needs));
        }
        return needs;
    }
    private int DisposableStock(string item,int minimumQuality=0,bool protectProgress=true) {
        var stock=Facts.Stock.Select(s=>new Together.Shared.ResourceFlow.Stock(s.Item,s.Category,s.Quality,s.Count)).ToArray();
        var demands=ConsumptionRequirements(protectProgress:protectProgress).Select(r=>new Together.Shared.ResourceFlow.Demand(r.Item,r.Quality,r.Count)).ToArray();
        var allocation=Together.Shared.ResourceFlow.Allocate(stock,demands);
        return stock.Select((s,i)=>s.Item==item&&s.Quality>=minimumQuality?Math.Max(0,s.Count-allocation.StockUsed[i]):0).Sum();
    }
    private bool CanConsumeOne(Item item)=>ReservationAllocation.Preserves(Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category}),ConsumptionRequirements(protectProgress:true),new[]{new GoalStock{Item=item.QualifiedItemId,Count=1,Quality=item.Quality,Category=item.Category}});
    private void ValidatePlayerConsumption(IReadOnlyDictionary<Item,int> consumed,string goalId,string output) {
        RefreshFacts(true);
        var owner=Data.SharedGoals.FirstOrDefault(g=>g.Id==goalId&&g.Status=="active");
        if(goalId.Length>0&&(owner==null||!owner.Nodes.Any(n=>n.Item==output)))throw new InvalidOperationException("consumption_goal_does_not_own_output");
        var requirements=ConsumptionRequirements(owner,true,output);
        var available=Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category});
        var spending=consumed.Select(p=>new GoalStock{Item=p.Key.QualifiedItemId,Count=p.Value,Quality=p.Key.Quality,Category=p.Key.Category});
        if(!ReservationAllocation.Preserves(available,requirements,spending))throw new InvalidOperationException("material_reserved_for_other_goal_or_quality_allocation");
        // Exact quantity/quality demands protect native progress without locking
        // every surplus unit of the same crop. Already missing demands remain visible.
    }
}
