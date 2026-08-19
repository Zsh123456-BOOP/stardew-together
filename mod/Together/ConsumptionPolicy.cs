using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private void ValidatePlayerConsumption(IReadOnlyDictionary<Item,int> consumed,string goalId,string output) {
        RefreshFacts(true);
        var owner=Data.SharedGoals.FirstOrDefault(g=>g.Id==goalId&&g.Status=="active");
        if(goalId.Length>0&&(owner==null||!owner.Nodes.Any(n=>n.Item==output)))throw new InvalidOperationException("consumption_goal_does_not_own_output");
        var requirements=Data.Projects.Where(p=>p.Status=="active").SelectMany(p=>p.Needs)
            .Concat(Data.SharedGoals.Where(g=>g.Status=="active"&&g!=owner).SelectMany(g=>g.Reserved));
        var available=Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category});
        var spending=consumed.Select(p=>new GoalStock{Item=p.Key.QualifiedItemId,Count=p.Value,Quality=p.Key.Quality,Category=p.Key.Category});
        if(!ReservationAllocation.Preserves(available,requirements,spending))throw new InvalidOperationException("material_reserved_for_other_goal_or_quality_allocation");
        // Missing bundle items and live delivery requests are preserved for food
        // and sale even before the player has explicitly created a shared goal.
        if(output.Length==0&&consumed.Keys.Any(i=>Facts.Bundles.Where(b=>!b.Complete).SelectMany(b=>b.Missing).Any(n=>n.Item==i.QualifiedItemId)||Facts.Goals.Where(q=>!q.Complete&&q.Kind!="craft").SelectMany(q=>q.Needs).Any(n=>n.Item==i.QualifiedItemId)))
            throw new InvalidOperationException("progress_item_not_available_for_consumption");
    }
}
