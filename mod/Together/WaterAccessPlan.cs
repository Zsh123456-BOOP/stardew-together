namespace Together;
public sealed record WaterAccessCell(FarmCell Tile,bool Passable,int ClearEnergy);
public static class WaterAccessPlan {
    public static List<FarmCell> Find(IEnumerable<WaterAccessCell> cells,FarmCell start,ISet<FarmCell> banks,int energyBudget) {
        var grid=cells.ToDictionary(c=>c.Tile);var queue=new PriorityQueue<FarmCell,int>();
        var costs=new Dictionary<FarmCell,int>{{start,0}};var energy=new Dictionary<FarmCell,int>{{start,0}};var previous=new Dictionary<FarmCell,FarmCell>();
        queue.Enqueue(start,0);
        while(queue.TryDequeue(out var at,out int priority)) {
            if(costs[at]!=priority)continue;
            if(banks.Contains(at)) {
                var path=new List<FarmCell>();while(at!=start){path.Add(at);at=previous[at];}path.Reverse();return path;
            }
            foreach(var next in new[]{new FarmCell(at.X+1,at.Y),new FarmCell(at.X-1,at.Y),new FarmCell(at.X,at.Y+1),new FarmCell(at.X,at.Y-1)}) {
                if(!grid.TryGetValue(next,out var cell)||!cell.Passable&&cell.ClearEnergy<=0)continue;
                int clear=cell.Passable?0:cell.ClearEnergy,used=energy[at]+clear,cost=priority+1+clear*10;
                if(used>energyBudget||cost>=costs.GetValueOrDefault(next,int.MaxValue))continue;
                costs[next]=cost;energy[next]=used;previous[next]=at;queue.Enqueue(next,cost);
            }
        }
        return new();
    }
}
