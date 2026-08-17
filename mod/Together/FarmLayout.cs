namespace Together;

public readonly record struct FarmCell(int X,int Y);
public sealed record LayoutCell(FarmCell Tile,bool Plantable,bool Passable,bool Watered,bool Irrigated,bool Protected,bool Tilled,int DistanceToWater);
public sealed record LayoutResult(List<FarmCell> Tiles,int ManualWatering,int Unprotected,string StopReason);

public static class FarmLayout {
    public static bool KeepsAccess(IReadOnlyList<LayoutCell> source,FarmCell start,IEnumerable<FarmCell> anchors,IEnumerable<FarmCell> occupied,IEnumerable<FarmCell> newAccess) {
        var cells=source.ToDictionary(c=>c.Tile);var blocked=occupied.ToHashSet();
        if(blocked.Contains(start))return false;
        var before=Distances(cells,start,new());var after=Distances(cells,start,blocked);
        return anchors.Where(before.ContainsKey).All(after.ContainsKey)&&newAccess.All(after.ContainsKey);
    }
    public static Dictionary<FarmCell,int> WaterDistances(IReadOnlyList<LayoutCell> source,IEnumerable<FarmCell> water) {
        var cells=source.ToDictionary(c=>c.Tile);var result=new Dictionary<FarmCell,int>();var queue=new Queue<FarmCell>();
        foreach(var stand in water.SelectMany(Neighbours).Distinct().Where(p=>cells.TryGetValue(p,out var c)&&c.Passable)) {result[stand]=0;queue.Enqueue(stand);}
        while(queue.TryDequeue(out var p))foreach(var n in Neighbours(p))if(!result.ContainsKey(n)&&cells.TryGetValue(n,out var c)&&c.Passable){result[n]=result[p]+1;queue.Enqueue(n);}
        return result;
    }
    private static IEnumerable<FarmCell> Neighbours(FarmCell p) {yield return new(p.X+1,p.Y);yield return new(p.X-1,p.Y);yield return new(p.X,p.Y+1);yield return new(p.X,p.Y-1);}
    private static Dictionary<FarmCell,int> Distances(Dictionary<FarmCell,LayoutCell> cells,FarmCell start,HashSet<FarmCell> blocked) {
        var result=new Dictionary<FarmCell,int>{{start,0}};var queue=new Queue<FarmCell>();queue.Enqueue(start);
        while(queue.TryDequeue(out var tile))foreach(var n in Neighbours(tile))
            if(!result.ContainsKey(n)&&!blocked.Contains(n)&&cells.TryGetValue(n,out var c)&&c.Passable){result[n]=result[tile]+1;queue.Enqueue(n);}
        return result;
    }
    // Input is a detached grid. Every accepted trellis placement is checked against
    // the FUTURE collision map, preserving access to earlier crops and anchor tiles.
    public static LayoutResult Choose(IReadOnlyList<LayoutCell> source,FarmCell start,IReadOnlyList<FarmCell> anchors,int count,bool trellis,int manualLimit,bool requireProtection=false) {
        var cells=source.ToDictionary(c=>c.Tile);var blocked=new HashSet<FarmCell>();var selected=new List<FarmCell>();
        var original=Distances(cells,start,blocked);var required=anchors.Where(original.ContainsKey).ToArray();
        var candidates=source.Where(c=>c.Plantable&&original.ContainsKey(c.Tile)&&(!requireProtection||c.Protected)&&!required.Contains(c.Tile)).ToArray();
        int manual=0;string stop="requested_tiles_selected";
        while(selected.Count<count) {
            var ordered=candidates.Where(c=>!selected.Contains(c.Tile)&&(!trellis||c.Tile!=start)&& (c.Irrigated||manual<manualLimit))
                .OrderBy(c=>original[c.Tile]*2 + c.DistanceToWater*.2 + (c.Irrigated?0:25)+(c.Protected?0:8)+(c.Tilled?0:6)
                    +(selected.Count==0?0:selected.Min(t=>Math.Abs(t.X-c.Tile.X)+Math.Abs(t.Y-c.Tile.Y))*5))
                .ThenBy(c=>c.Tile.Y).ThenBy(c=>c.Tile.X);
            LayoutCell? chosen=null;
            foreach(var candidate in ordered) {
                if(trellis) {
                    blocked.Add(candidate.Tile);var reachable=Distances(cells,start,blocked);
                    bool safe=required.All(reachable.ContainsKey)&&selected.Append(candidate.Tile).All(t=>Neighbours(t).Any(reachable.ContainsKey));
                    blocked.Remove(candidate.Tile);if(!safe)continue;
                }
                chosen=candidate;break;
            }
            if(chosen==null){stop="space_access_or_daily_labor_limit";break;}
            selected.Add(chosen.Tile);if(trellis)blocked.Add(chosen.Tile);if(!chosen.Irrigated)manual++;
        }
        return new(selected,manual,selected.Count(t=>!cells[t].Protected),stop);
    }
}
