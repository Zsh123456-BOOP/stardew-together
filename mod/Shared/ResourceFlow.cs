namespace Together.Shared;

// Same deterministic physical-unit allocation in both runtime assemblies.
public static class ResourceFlow {
    public sealed record Stock(string Item,int Category,int Quality,int Count);
    public sealed record Demand(string Item,int Quality,int Count);
    public sealed record Allocation(int[] StockUsed,int[] DemandFilled);
    private sealed class Edge {
        public int To,Reverse,Capacity;
        public Edge(int to,int reverse,int capacity){To=to;Reverse=reverse;Capacity=capacity;}
    }
    private static bool Matches(Stock stock,Demand demand) {
        bool category=int.TryParse(demand.Item.Replace("(O)",""),out int id)&&id<0;
        return stock.Quality>=demand.Quality&&(stock.Item==demand.Item||category&&stock.Category==id);
    }
    public static Allocation Allocate(IReadOnlyList<Stock> stock,IReadOnlyList<Demand> demands) {
        int sink=1+stock.Count+demands.Count;var graph=Enumerable.Range(0,sink+1).Select(_=>new List<Edge>()).ToArray();
        void Add(int a,int b,int cap){var edge=new Edge(b,graph[b].Count,Math.Max(0,cap));var reverse=new Edge(a,graph[a].Count,0);graph[a].Add(edge);graph[b].Add(reverse);}
        for(int i=0;i<stock.Count;i++) {
            Add(0,i+1,stock[i].Count);
            for(int j=0;j<demands.Count;j++)if(Matches(stock[i],demands[j]))Add(i+1,1+stock.Count+j,Math.Min(stock[i].Count,demands[j].Count));
        }
        var terminal=new Edge[demands.Count];
        for(int j=0;j<demands.Count;j++){int at=1+stock.Count+j;Add(at,sink,demands[j].Count);terminal[j]=graph[at][^1];}
        while(true) {
            var parents=Enumerable.Repeat(-1,graph.Length).ToArray();var edges=new int[graph.Length];var queue=new Queue<int>();queue.Enqueue(0);parents[0]=0;
            while(queue.Count>0&&parents[sink]<0) {
                int at=queue.Dequeue();
                for(int e=0;e<graph[at].Count;e++){var edge=graph[at][e];if(edge.Capacity<=0||parents[edge.To]>=0)continue;parents[edge.To]=at;edges[edge.To]=e;queue.Enqueue(edge.To);}
            }
            if(parents[sink]<0)break;
            int flow=int.MaxValue;
            for(int at=sink;at!=0;at=parents[at])flow=Math.Min(flow,graph[parents[at]][edges[at]].Capacity);
            for(int at=sink;at!=0;at=parents[at]){var edge=graph[parents[at]][edges[at]];edge.Capacity-=flow;graph[at][edge.Reverse].Capacity+=flow;}
        }
        return new(stock.Select((s,i)=>Math.Max(0,s.Count)-graph[0][i].Capacity).ToArray(),demands.Select((d,i)=>Math.Max(0,d.Count)-terminal[i].Capacity).ToArray());
    }
}
