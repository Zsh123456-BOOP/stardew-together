namespace Together;

public sealed record SeedRequest(string Item,int Count,int Price,int Stock);
public static class AutonomyPolicy {
    // -1 is absence of a model-imposed daily ceiling, not negative cash.
    public static int Cash(int cash,int keep,int daily,int spent,int pending,int development=0)=>
        Math.Max(0,Math.Min(Math.Max(0,cash-Math.Max(0,keep)-Math.Max(0,pending)-Math.Max(0,development)),daily<0?int.MaxValue:Math.Max(0,daily-spent-pending)));
    // Preserve the model's item order; shrinking is explicit in the receipt.
    public static Dictionary<string,int> Seeds(IEnumerable<SeedRequest> requests,int allowance) {
        var result=new Dictionary<string,int>();
        foreach(var row in requests) {
            if(row.Count<1||row.Price<0||row.Stock<0||result.ContainsKey(row.Item))throw new InvalidOperationException("invalid_seed_request");
            int n=Math.Min(row.Count,row.Stock);if(row.Price>0)n=Math.Min(n,allowance/row.Price);
            result[row.Item]=n;allowance-=n*row.Price;
        }
        return result;
    }
    public static string InvestmentState(string phase,string error)=>phase switch {
        "done" when error.Length>0=>"deferred",
        "done"=>"completed",
        "blocked"=>"blocked",
        "awaiting_selection"=>"awaiting_model_choice",
        "idle"=>"idle",
        _=>"executing"
    };
    public static List<FarmCell>? Path(FarmCell start,FarmCell end,Func<FarmCell,bool> passable,int limit=10000) {
        if(start==end)return new(){start};if(!passable(end))return null;
        var open=new PriorityQueue<FarmCell,int>();var costs=new Dictionary<FarmCell,int>{{start,0}};var parent=new Dictionary<FarmCell,FarmCell>();var closed=new HashSet<FarmCell>();
        open.Enqueue(start,FarmDistrict.Distance(start,end));
        while(open.TryDequeue(out var at,out _)&&closed.Count<limit) {
            if(!closed.Add(at))continue;
            if(at==end){var path=new List<FarmCell>{end};while(parent.TryGetValue(at,out var prev)){path.Add(prev);at=prev;}path.Reverse();return path;}
            foreach(var next in new[]{new FarmCell(at.X+1,at.Y),new(at.X-1,at.Y),new(at.X,at.Y+1),new(at.X,at.Y-1)}) {
                if(closed.Contains(next)||!passable(next))continue;int cost=costs[at]+1;
                if(costs.TryGetValue(next,out int old)&&old<=cost)continue;costs[next]=cost;parent[next]=at;open.Enqueue(next,cost+FarmDistrict.Distance(next,end));
            }
        }
        return null;
    }
    public static Dictionary<FarmCell,int> Distances(FarmCell start,Func<FarmCell,bool> passable,int limit=10000) {
        var found=new Dictionary<FarmCell,int>{{start,0}};var seen=new HashSet<FarmCell>{start};var queue=new Queue<FarmCell>();queue.Enqueue(start);
        while(queue.TryDequeue(out var at)&&found.Count<limit)foreach(var next in new[]{new FarmCell(at.X+1,at.Y),new(at.X-1,at.Y),new(at.X,at.Y+1),new(at.X,at.Y-1)})
            if(seen.Add(next)&&passable(next)){found[next]=found[at]+1;queue.Enqueue(next);}
        return found;
    }
    public static double ResourceCost(int walk,float energy,int hits,int yield,int needed)=>
        (walk+Math.Max(0,energy)+Math.Max(0,hits)+2.0)/Math.Max(1,Math.Min(yield,needed));
}
