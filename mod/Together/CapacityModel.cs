namespace Together;

public readonly record struct StackKey(string Id,int Quality,string Variant);
public sealed class CapacitySnapshot {
    private sealed record Slot(StackKey Key,int Stack,int MaxStack,bool Protected);
    private List<Slot> slots;
    public int SlotCount {get;}
    public int Occupied=>slots.Count;
    public int FreeSlots=>SlotCount-Occupied;
    public CapacitySnapshot(int slotCount,IEnumerable<(StackKey Key,int Stack,int MaxStack,bool Protected)> items) {
        if(slotCount<0)throw new ArgumentOutOfRangeException(nameof(slotCount));
        SlotCount=slotCount;slots=items.Where(i=>i.Stack>0).Select(i=>new Slot(i.Key,i.Stack,Math.Max(i.Stack,i.MaxStack),i.Protected)).ToList();
        if(slots.Count>slotCount)throw new ArgumentException("capacity_snapshot_overflow");
    }
    public CapacitySnapshot Clone()=>new(SlotCount,slots.Select(s=>(s.Key,s.Stack,s.MaxStack,s.Protected)));
    public bool TryTake(StackKey key,int count,out string reason) {
        if(count<0)throw new ArgumentOutOfRangeException(nameof(count));
        reason="";if(slots.Where(s=>s.Key==key&&!s.Protected).Sum(s=>(long)s.Stack)<count){reason="capacity_material_unavailable_or_protected";return false;}
        // Native recipe consumption walks inventory backwards. Preserve that order.
        for(int i=slots.Count-1;i>=0&&count>0;i--)if(slots[i].Key==key&&!slots[i].Protected) {
            var s=slots[i];int take=Math.Min(count,s.Stack);count-=take;
            if(take==s.Stack)slots.RemoveAt(i);else slots[i]=s with{Stack=s.Stack-take};
        }
        return true;
    }
    public bool TryPut(StackKey key,int count,int maxStack,out string reason) {
        if(count<0||maxStack<1)throw new ArgumentOutOfRangeException(nameof(count));
        var copy=new List<Slot>(slots);int left=count;reason="";
        for(int i=0;i<copy.Count&&left>0;i++)if(copy[i].Key==key) {
            var s=copy[i];int add=Math.Min(left,Math.Max(0,Math.Min(s.MaxStack,maxStack)-s.Stack));
            copy[i]=s with{Stack=s.Stack+add};left-=add;
        }
        while(left>0&&copy.Count<SlotCount){int add=Math.Min(left,maxStack);copy.Add(new(key,add,maxStack,false));left-=add;}
        if(left>0){reason=slots.Any(s=>s.Key==key)?"capacity_no_stackable_room":"capacity_no_free_slot";return false;}
        slots=copy;return true;
    }
}
public abstract record CapacityOp;
public sealed record TakeOp(StackKey Key,int Count):CapacityOp;
public sealed record PutOp(StackKey Key,int Count,int MaxStack):CapacityOp;
public sealed record RequireFreeOp(int Slots):CapacityOp;
public sealed record CapacityVerdict(bool Feasible,int FailedStep,string Reason,int PeakOccupied,int FinalOccupied,int FreeSlotsAtEnd);
public static class CapacityPlan {
    public static bool FreeFits(int free,int required)=>Simulate(new(Math.Max(0,free),Array.Empty<(StackKey,int,int,bool)>()),new CapacityOp[]{new RequireFreeOp(Math.Max(0,required))}).Feasible;
    public static int RequiredSlots(string goal,int requested=0)=>Math.Max(0,requested);
    public static CapacityVerdict Simulate(CapacitySnapshot start,IReadOnlyList<CapacityOp> ops) {
        var state=start.Clone();int peak=state.Occupied;
        for(int n=0;n<ops.Count;n++) {
            string reason="";bool ok=ops[n] switch {
                TakeOp take=>state.TryTake(take.Key,take.Count,out reason),
                PutOp put=>state.TryPut(put.Key,put.Count,put.MaxStack,out reason),
                RequireFreeOp free when free.Slots>=0=>state.FreeSlots>=free.Slots,
                _=>throw new ArgumentException("invalid_capacity_operation")
            };
            peak=Math.Max(peak,state.Occupied);
            if(!ok)return new(false,n,reason.Length>0?reason:"capacity_no_free_slot",peak,state.Occupied,state.FreeSlots);
        }
        return new(true,-1,"",peak,state.Occupied,state.FreeSlots);
    }
}
