namespace Together;

// Material demands overlap (e.g. any vegetable and a particular gold vegetable).
// A flow allocation counts each physical unit once and can reroute substitutes.
public static class ReservationAllocation {
    public static int[] Allocate(IReadOnlyList<GoalStock> stock,IReadOnlyList<Requirement> demands) =>
        Together.Shared.ResourceFlow.Allocate(stock.Select(s=>new Together.Shared.ResourceFlow.Stock(s.Item,s.Category,s.Quality,s.Count)).ToArray(),
            demands.Select(d=>new Together.Shared.ResourceFlow.Demand(d.Item,d.Quality,d.Count)).ToArray()).DemandFilled;
    public static bool Preserves(IEnumerable<GoalStock> available,IEnumerable<Requirement> reservations,IEnumerable<GoalStock> consumed) {
        var stock=available.Where(s=>s.Count>0).GroupBy(s=>new{s.Item,s.Quality,s.Category}).OrderBy(g=>g.Key.Quality).ThenBy(g=>g.Key.Item,StringComparer.Ordinal).Select(g=>new GoalStock{Item=g.Key.Item,Quality=g.Key.Quality,Category=g.Key.Category,Count=g.Sum(s=>s.Count)}).ToList();
        var demands=reservations.Where(r=>r.Count>0).GroupBy(r=>new{r.Item,r.Quality}).OrderByDescending(g=>g.Key.Quality).ThenBy(g=>g.Key.Item,StringComparer.Ordinal).Select(g=>new Requirement{Item=g.Key.Item,Quality=g.Key.Quality,Count=g.Sum(r=>r.Count)}).ToList();
        if(demands.Count==0)return true;
        int[] baseline=Allocate(stock,demands);
        foreach(var spent in consumed) {
            int left=spent.Count;
            foreach(var item in stock.Where(s=>s.Item==spent.Item&&s.Quality==spent.Quality&&s.Category==spent.Category)){int take=Math.Min(item.Count,left);item.Count-=take;left-=take;}
            if(left>0||spent.Count<0)return false;
        }
        // Preserve what each commitment could actually receive before the action.
        // Already missing materials don't make all unrelated actions impossible.
        for(int i=0;i<demands.Count;i++)demands[i].Count=baseline[i];
        int[] after=Allocate(stock,demands);return baseline.SequenceEqual(after);
    }
}
