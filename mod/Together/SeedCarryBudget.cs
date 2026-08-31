namespace Together;

// Capacity remaining for additions, after accounting for actual compatible stacks.
// Evaluate the whole purchase manifest, not each seed against the same empty slot.
public sealed record SeedCarryBudget(int FreeSlots,Dictionary<string,int> StackRoom,Dictionary<string,int> StackSize) {
    public bool Fits(IReadOnlyDictionary<string,int> additions) {
        long slots=0;
        foreach(var p in additions) {
            int extra=Math.Max(0,p.Value-StackRoom.GetValueOrDefault(p.Key));
            int size=Math.Max(1,StackSize.GetValueOrDefault(p.Key,999));
            slots+=(extra+(long)size-1)/size;
        }
        return slots<=FreeSlots;
    }
}
