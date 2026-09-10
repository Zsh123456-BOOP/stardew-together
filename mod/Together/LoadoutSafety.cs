namespace Together;

// Phase A is deliberately single-storage. This declaration is independent of
// capacity feasibility: more free slots cannot implement an unsupported shape.
public static class LoadoutSafety {
    public static string? Unsupported(string tool,string mode="")=>tool switch {
        "player.build" or "player.upgrade_house" or "player.repair_boat" or "player.island_upgrade"=>"construction_materials",
        "player.beach" when mode=="bridge"=>"construction_materials",
        "player.order_donate" or "player.bundle" or "player.donate_museum"=>"partial_delivery",
        "player.social" when mode is "deliver" or "gift" or "relationship"=>"partial_delivery",
        _=>null
    };
    public static bool MultipleStorage(bool bagSufficient,IEnumerable<bool> singleStorageSufficient,bool combinedSufficient)=>!bagSufficient&&!singleStorageSufficient.Any(x=>x)&&combinedSufficient;
    public static bool MustStop(string error,bool? conserved)=>error=="loadout_conservation_failed"||conserved!=true;
    public static string Normalize(string error)=>error switch {"storage_busy"=>"loadout_storage_busy","native_capacity_changed"=>"loadout_native_capacity_changed",_=>error};
}
