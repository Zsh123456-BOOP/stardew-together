namespace Together;

// Storage is a prerequisite or a delivery, never a reward for finishing a batch.
public static class StorageTiming {
    public static bool NeedsRoom(int freeSlots,bool expectsOutput,bool stackHasRoom)=>expectsOutput&&!stackHasRoom&&!CapacityPlan.FreeFits(freeSlots,1);
    public static string DeliveryReason(int storable,int freeSlots,bool closesMaterialGap,bool endOfDay) {
        if(storable<=0)return "";
        if(closesMaterialGap)return "经营材料已凑齐，交接后可继续制作或建设";
        if(freeSlots<=0)return "货袋已满，交货后继续劳动";
        if(endOfDay)return "收工交接当日产物，供出售和次日生产";
        return "";
    }
}
