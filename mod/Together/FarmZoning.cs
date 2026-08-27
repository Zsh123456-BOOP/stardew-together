namespace Together;

public sealed record FarmFootprint(int X,int Y,int Width,int Height,bool Home);
public static class FarmZoning {
    // Spatial constraints, not a score penalty: a tempting nearby empty courtyard
    // must never beat farmland simply because it is closer to the Farmer.
    public static Dictionary<FarmCell,string> Reserve(IReadOnlyList<LayoutCell> grid,IReadOnlyList<FarmFootprint> buildings,FarmCell home,IEnumerable<FarmCell> anchors) {
        var cells=grid.ToDictionary(c=>c.Tile);var reserved=new Dictionary<FarmCell,string>();
        foreach(var b in buildings) {
            for(int y=b.Y-1;y<b.Y+b.Height+1;y++)for(int x=b.X-1;x<b.X+b.Width+1;x++)
                if(cells.ContainsKey(new(x,y)))reserved[new(x,y)]="building_access";
            if(b.Home)for(int y=b.Y+b.Height;y<b.Y+b.Height+4;y++)for(int x=b.X-1;x<b.X+b.Width+1;x++)
                if(cells.ContainsKey(new(x,y)))reserved[new(x,y)]="home_courtyard";
        }
        if(!cells.TryGetValue(home,out var origin)||!origin.Passable)return reserved;
        var costs=new Dictionary<FarmCell,int>{{home,0}};var parent=new Dictionary<FarmCell,FarmCell>();var queue=new PriorityQueue<FarmCell,int>();queue.Enqueue(home,0);
        while(queue.TryDequeue(out var p,out int cost)) {
            if(cost!=costs[p])continue;
            foreach(var next in Neighbours(p))if(cells.TryGetValue(next,out var cell)&&cell.Passable) {
                int candidate=cost+1+cell.ClearCost*4;
                if(candidate>=costs.GetValueOrDefault(next,int.MaxValue))continue;
                costs[next]=candidate;parent[next]=p;queue.Enqueue(next,candidate);
            }
        }
        foreach(var anchor in anchors.Distinct()) {
            // Native exits can lie one tile outside the map. Reach their inside stand.
            var target=Neighbours(anchor).Append(anchor).Where(costs.ContainsKey).OrderBy(p=>costs[p]).Select(p=>(FarmCell?)p).FirstOrDefault();
            if(target==null)continue;var at=target.Value;
            while(true) {
                foreach(var t in new[]{at,new(at.X+1,at.Y),new(at.X,at.Y+1),new(at.X+1,at.Y+1)})
                    if(cells.TryGetValue(t,out var cell)&&cell.Passable&&!reserved.ContainsKey(t))reserved[t]="service_road";
                if(at==home||!parent.TryGetValue(at,out at))break;
            }
        }
        return reserved;
    }
    private static IEnumerable<FarmCell> Neighbours(FarmCell p){yield return new(p.X-1,p.Y);yield return new(p.X+1,p.Y);yield return new(p.X,p.Y-1);yield return new(p.X,p.Y+1);}
}
