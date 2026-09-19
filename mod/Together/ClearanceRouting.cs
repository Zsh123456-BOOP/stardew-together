namespace Together;

// Pure bounded search. A blocked tile is traversable only through an explicit,
// feasible native clearing operation; it never becomes a walkable tile by fiat.
public static class ClearanceRouting {
    public sealed record Obstacle(double Cost,float Energy,bool Useful);
    public sealed record Plan(List<FarmCell> Path,List<FarmCell> Clear,double Cost,float Energy);
    private sealed record Node(FarmCell At,List<FarmCell> Path,List<FarmCell> Clear,double Cost,float Energy);
    public static Plan? Find(FarmCell start,FarmCell end,Func<FarmCell,bool> passable,Func<FarmCell,Obstacle?> obstacle,Func<IReadOnlyList<FarmCell>,bool> capacity,float energy,int maxClear=3,double maxCost=double.PositiveInfinity,int limit=6000) {
        var open=new PriorityQueue<Node,double>();var seen=new Dictionary<string,double>();
        open.Enqueue(new(start,new(){start},new(),0,0),FarmDistrict.Distance(start,end));int expanded=0;
        while(open.TryDequeue(out var n,out _)&&expanded++<limit) {
            string key=$"{n.At.X},{n.At.Y}:"+string.Join(";",n.Clear.OrderBy(t=>t.X).ThenBy(t=>t.Y).Select(t=>$"{t.X},{t.Y}"));
            if(seen.TryGetValue(key,out var old)&&old<=n.Cost)continue;seen[key]=n.Cost;
            if(n.At==end)return new(n.Path,n.Clear,n.Cost,n.Energy);
            foreach(var next in new[]{new FarmCell(n.At.X+1,n.At.Y),new(n.At.X-1,n.At.Y),new(n.At.X,n.At.Y+1),new(n.At.X,n.At.Y-1)}) {
                if(n.Path.Contains(next))continue;
                var clear=n.Clear;double cost=n.Cost+1;float spent=n.Energy;
                if(!passable(next)) {
                    var o=obstacle(next);if(o==null||clear.Count>=maxClear)continue;
                    spent+=o.Energy;if(spent>energy)continue;
                    clear=new(n.Clear){next};if(!capacity(clear))continue;
                    cost+=o.Cost+(o.Useful?0:.01); // tie-break only, never hides labour cost
                }
                if(cost+FarmDistrict.Distance(next,end)>maxCost)continue;
                open.Enqueue(new(next,new(n.Path){next},clear,cost,spent),cost+FarmDistrict.Distance(next,end));
            }
        }
        return null;
    }
}
