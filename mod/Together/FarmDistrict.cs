namespace Together;
// A persisted footprint survives harvest, season changes, and the Farmer walking
// elsewhere. Only approved planting grows it; planning a rejected quote does not.
public sealed class FarmDistrict {
    public List<FarmCell> Field {get;set;}=new();
    public FarmCell? Warehouse {get;set;}
    public int Revision {get;set;}
    public void Commit(IEnumerable<FarmCell> cells) {Field=Field.Concat(cells).Distinct().ToList();Revision++;}
    public static int Distance(FarmCell a,FarmCell b)=>Math.Abs(a.X-b.X)+Math.Abs(a.Y-b.Y);
    public List<LayoutCell> Constrain(IReadOnlyList<LayoutCell> grid,FarmCell home) {
        return grid.Select(c=> {
            int distance=Field.Count==0?Distance(c.Tile,home):Field.Min(t=>Distance(t,c.Tile));
            return c with {Plantable=c.Plantable&&(Field.Count==0||distance<=3)&&c.Tile!=Warehouse,PlanningPenalty=distance*(Field.Count==0?4:30)};
        }).ToList();
    }
    public static List<FarmCell> LargestCluster(IEnumerable<FarmCell> source,FarmCell home) {
        var remaining=source.ToHashSet();var groups=new List<List<FarmCell>>();
        while(remaining.Count>0) {
            var group=new List<FarmCell>();var queue=new Queue<FarmCell>();var first=remaining.OrderBy(t=>Distance(t,home)).First();queue.Enqueue(first);remaining.Remove(first);
            while(queue.TryDequeue(out var at)) {group.Add(at);foreach(var next in remaining.Where(t=>Distance(t,at)<=2).ToArray()){remaining.Remove(next);queue.Enqueue(next);}}
            groups.Add(group);
        }
        return groups.OrderByDescending(g=>g.Count).ThenBy(g=>g.Min(t=>Distance(t,home))).FirstOrDefault()??new();
    }
}
