using StardewValley;
namespace Together;
public static class PlayerSelection {
    // Validate BEFORE calling the native setter, which itself reads CurrentItem.
    public static void Set(Farmer farmer,int index) {SlotInvariant.Check(index,farmer.Items.Count);farmer.CurrentToolIndex=index;}
    public static void Neutral(Farmer farmer) {
        int slot=Enumerable.Range(0,farmer.Items.Count).FirstOrDefault(i=>farmer.Items[i]==null||farmer.Items[i] is Tool,-1);
        if(slot<0)throw new InvalidOperationException("neutral_inventory_slot_unavailable");
        Set(farmer,slot);farmer.netItemStowed.Value=true;
    }
    public static void Assert(Farmer farmer)=>SlotInvariant.Check(farmer.CurrentToolIndex,farmer.Items.Count);
}
