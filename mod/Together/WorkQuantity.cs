namespace Together;

public sealed record WorkQuantity(int Target,int Missing,string? Error) {
    // Count always means additional items. Only stock_target names a total.
    public static WorkQuantity Resolve(int owned,int approved,int count,int? stockTarget,bool managed) {
        if(count is <0 or >999||stockTarget is <1 or >9999)return new(0,0,"invalid_material_quantity_count_0_to_999_stock_target_1_to_9999");
        if(stockTarget.HasValue&&stockTarget.Value<=owned)return new(stockTarget.Value,0,null);
        if(managed&&approved<=owned)return new(owned,0,"no_approved_material_demand");
        int target=stockTarget??(count==0?(managed?approved:owned+20):owned+count);
        if(managed)target=Math.Min(target,approved);
        return new(target,Math.Max(0,target-owned),null);
    }
}
