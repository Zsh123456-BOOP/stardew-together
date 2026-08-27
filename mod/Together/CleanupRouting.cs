namespace Together;

public sealed record CleanupSite(FarmCell Tile,int Energy,int Priority=0);
public sealed record CleanupStep(CleanupSite Site,FarmCell Stand,int Walk);
public static class CleanupRouting {
    private static IEnumerable<FarmCell> Around(FarmCell p) {
        yield return new(p.X,p.Y+1);yield return new(p.X-1,p.Y);yield return new(p.X+1,p.Y);yield return new(p.X,p.Y-1);
    }
    // Frontier BFS stops at the nearest reachable work stand. Tool types do not
    // influence grouping; clearing a target opens that tile for the next step.
    public static List<CleanupStep> Plan(FarmCell start,IEnumerable<CleanupSite> source,Func<FarmCell,bool> passable,int energy,int limit,int radius=8) {
        var remaining=source.GroupBy(s=>s.Tile).ToDictionary(g=>g.Key,g=>g.First());
        var cleared=new HashSet<FarmCell>();var cached=new Dictionary<FarmCell,bool>();var result=new List<CleanupStep>();FarmCell? anchor=null;
        bool Walkable(FarmCell p)=>cleared.Contains(p)||(cached.TryGetValue(p,out bool valid)?valid:cached[p]=passable(p));
        while(result.Count<limit) {
            if(!remaining.Values.Any(s=>s.Energy<=energy&&(!anchor.HasValue||Math.Abs(s.Tile.X-anchor.Value.X)+Math.Abs(s.Tile.Y-anchor.Value.Y)<=radius)))break;
            var queue=new Queue<(FarmCell Tile,int Distance)>();var seen=new HashSet<FarmCell>{start};queue.Enqueue((start,0));CleanupStep? best=null;
            while(queue.TryDequeue(out var at)) {
                if(best!=null&&at.Distance>best.Walk)break;
                foreach(var tile in Around(at.Tile))if(remaining.TryGetValue(tile,out var site)&&site.Energy<=energy&&(!anchor.HasValue||Math.Abs(tile.X-anchor.Value.X)+Math.Abs(tile.Y-anchor.Value.Y)<=radius))
                    if(best==null||site.Priority<best.Site.Priority)best=new(site,at.Tile,at.Distance);
                if(best!=null)continue;
                foreach(var next in Around(at.Tile))if(seen.Add(next)&&Walkable(next))queue.Enqueue((next,at.Distance+1));
            }
            if(best==null)break;
            result.Add(best);remaining.Remove(best.Site.Tile);cleared.Add(best.Site.Tile);energy-=best.Site.Energy;start=best.Stand;anchor??=best.Site.Tile;
        }
        return result;
    }
}
