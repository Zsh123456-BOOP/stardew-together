using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private void ValidatePlayerConsumption(IReadOnlyDictionary<Item,int> consumed,string goalId,string output) {
        RefreshFacts(true);
        var owner=Data.SharedGoals.FirstOrDefault(g=>g.Id==goalId&&g.Status=="active");
        if(goalId.Length>0&&(owner==null||!owner.Nodes.Any(n=>n.Item==output)))throw new InvalidOperationException("consumption_goal_does_not_own_output");
        var requirements=Data.Projects.Where(p=>p.Status=="active").SelectMany(p=>p.Needs)
            .Concat(Data.SharedGoals.Where(g=>g.Status=="active"&&g!=owner).SelectMany(g=>g.Reserved));
        foreach(var group in requirements.GroupBy(r=>new{r.Item,r.Quality})) {
            bool category=int.TryParse(group.Key.Item.Replace("(O)",""),out int cat)&&cat<0;
            int used=consumed.Where(p=>p.Key.Quality>=group.Key.Quality&&(p.Key.QualifiedItemId==group.Key.Item||category&&p.Key.Category==cat)).Sum(p=>p.Value);
            if(used==0)continue;
            int available=Facts.Stock.Where(s=>s.Quality>=group.Key.Quality&&(s.Item==group.Key.Item||category&&s.Category==cat)).Sum(s=>s.Count);
            if(available-used<Math.Min(available,group.Sum(r=>r.Count)))throw new InvalidOperationException("material_reserved_for_other_goal:"+group.Key.Item);
        }
        // Missing bundle items and live delivery requests are preserved for food
        // and sale even before the player has explicitly created a shared goal.
        if(output.Length==0&&consumed.Keys.Any(i=>Facts.Bundles.Where(b=>!b.Complete).SelectMany(b=>b.Missing).Any(n=>n.Item==i.QualifiedItemId)||Facts.Goals.Where(q=>!q.Complete&&q.Kind!="craft").SelectMany(q=>q.Needs).Any(n=>n.Item==i.QualifiedItemId)))
            throw new InvalidOperationException("progress_item_not_available_for_consumption");
    }
}
