namespace Together;

public readonly record struct FarmCell(int X,int Y);
public sealed record LayoutCell(FarmCell Tile,bool Plantable,bool Passable,bool Watered,bool Irrigated,bool Protected,bool Tilled,int DistanceToWater,int ClearCost=0,bool Equipment=false);
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
        // Select a whole bed before selecting individual plants. Passable includes
        // removable debris; entry must be reachable WITHOUT clearing unrelated land.
        var cells=source.ToDictionary(c=>c.Tile);
        var actual=cells.ToDictionary(p=>p.Key,p=>p.Value with{Passable=p.Value.Passable&&p.Value.ClearCost==0});
        var reached=Distances(actual,start,new());var reserved=anchors.ToHashSet();
        var legal=source.Where(c=>c.Plantable&&(!requireProtection||c.Protected)&&!reserved.Contains(c.Tile)).ToDictionary(c=>c.Tile);
        if(count<=0||legal.Count==0)return new(new(),0,0,"no_compact_plot");
        int maxArea=count+Math.Min(12,source.Count(c=>c.Equipment));
        List<FarmCell>? best=null;double bestScore=double.MaxValue;int bestManual=0;
        var shapes=(from w in Enumerable.Range(1,Math.Min(maxArea,12)) from h in Enumerable.Range(1,Math.Min(maxArea,12))
                    where w*h<=maxArea&&(!trellis||Math.Min(w,h)<=2) select (W:w,H:h)).GroupBy(s=>s.W*s.H).OrderByDescending(g=>g.Key);
        foreach(var size in shapes) {
            if(best!=null&&size.Key<best.Count)break;
            foreach(var shape in size)foreach(var corner in legal.Keys) {
                var bed=new List<LayoutCell>();bool valid=true;
                for(int y=0;y<shape.H&&valid;y++)for(int x=0;x<shape.W;x++) {
                    var at=new FarmCell(corner.X+x,corner.Y+y);
                    if(legal.TryGetValue(at,out var cell))bed.Add(cell);
                    else if(!cells.TryGetValue(at,out var equipment)||!equipment.Equipment||reserved.Contains(at)){valid=false;break;}
                }
                if(!valid||bed.Count==0||bed.Count>count||best!=null&&bed.Count<best.Count)continue;int manual=bed.Count(c=>!c.Irrigated);if(manual>manualLimit)continue;
                int entry=bed.SelectMany(c=>Neighbours(c.Tile).Append(c.Tile)).Where(reached.ContainsKey).Select(p=>reached[p]).DefaultIfEmpty(int.MaxValue).Min();
                if(entry==int.MaxValue)continue;
                double score=entry*2+Math.Abs(shape.W-shape.H)*3+bed.Sum(c=>c.ClearCost*3+(c.Irrigated?0:25)+(c.Protected?0:8)+(c.Tilled?0:6)+Math.Min(100,c.DistanceToWater)*.2);
                if(best!=null&&bed.Count==best.Count&&score>=bestScore)continue;
                var tiles=bed.Select(c=>c.Tile).ToHashSet();
                if(trellis) {
                    var future=source.Select(c=>c with{Passable=c.Passable&&(c.ClearCost==0||tiles.Contains(c.Tile))}).ToArray();
                    var after=Distances(future.ToDictionary(c=>c.Tile),start,tiles);
                    if(tiles.Contains(start)||anchors.Where(reached.ContainsKey).Any(a=>!after.ContainsKey(a))||tiles.Any(t=>!Neighbours(t).Any(after.ContainsKey)))continue;
                }
                // Serpentine rows avoid repeatedly crossing the entire bed.
                best=bed.OrderBy(c=>c.Tile.Y).ThenBy(c=>(c.Tile.Y-corner.Y)%2==0?c.Tile.X:-c.Tile.X).Select(c=>c.Tile).ToList();bestScore=score;bestManual=manual;
            }
        }
        if(best!=null)return new(best,bestManual,best.Count(t=>!cells[t].Protected),best.Count==count?"compact_plot_selected":"compact_plot_reduced_for_space_access_or_labor");
        return new(new(),0,0,"no_reachable_compact_plot");
    }
    // Used inside an already selected bed by the mixed-crop budget allocator.
    public static LayoutResult ChooseWithinBed(IReadOnlyList<LayoutCell> source,FarmCell start,IReadOnlyList<FarmCell> anchors,int count,bool trellis,int manualLimit,bool requireProtection=false) {
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
