namespace Together;

public sealed record PackingMove(string Id,bool IntoBag,StackKey Key,int Count,int MaxStack);
public sealed record PackingResult(bool Feasible,IReadOnlyList<PackingMove> Moves,string Reason);
public static class LoadoutPlan {
    // Both containers evolve in one ordered simulation. A swap is not atomic:
    // at least one real transfer must fit first, otherwise report the cycle.
    public static PackingResult Arrange(CapacitySnapshot bag,CapacitySnapshot chest,IReadOnlyList<PackingMove> requested) {
        var a=bag.Clone();var b=chest.Clone();var left=requested.ToList();var done=new List<PackingMove>();
        while(left.Count>0) {
            bool advanced=false;
            foreach(var move in left.ToArray()) {
                var source=(move.IntoBag?b:a).Clone();var target=(move.IntoBag?a:b).Clone();
                if(!source.TryTake(move.Key,move.Count,out _)||!target.TryPut(move.Key,move.Count,move.MaxStack,out _))continue;
                if(move.IntoBag){b=source;a=target;}else{a=source;b=target;}
                done.Add(move);left.Remove(move);advanced=true;break;
            }
            if(!advanced)return new(false,done,"loadout_intermediate_capacity_or_stock_blocked");
        }
        return new(true,done,"");
    }
}
