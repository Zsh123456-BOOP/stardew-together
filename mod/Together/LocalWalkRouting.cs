namespace Together;
// Pixel offsets, not tile occupancy. Edges must be swept with the actual body.
public static class LocalWalkRouting {
    public static List<FarmCell>? Find(Func<FarmCell,bool> arrived,Func<FarmCell,FarmCell,bool> clear,int step=8,int radius=128,int limit=1100) {
        var start=new FarmCell(0,0);var queue=new Queue<FarmCell>();queue.Enqueue(start);
        var parent=new Dictionary<FarmCell,FarmCell>{{start,start}};
        while(queue.Count>0&&parent.Count<=limit) {
            var at=queue.Dequeue();
            if(at!=start&&arrived(at)){var path=new List<FarmCell>();for(var p=at;p!=start;p=parent[p])path.Add(p);path.Reverse();return path;}
            foreach(var next in new[]{new FarmCell(at.X,at.Y-step),new FarmCell(at.X+step,at.Y),new FarmCell(at.X,at.Y+step),new FarmCell(at.X-step,at.Y)}) {
                if(Math.Abs(next.X)>radius||Math.Abs(next.Y)>radius||parent.ContainsKey(next)||!clear(at,next))continue;
                parent[next]=at;queue.Enqueue(next);
            }
        }
        return null;
    }
}
