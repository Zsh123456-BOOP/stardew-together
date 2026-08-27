namespace Together.Shared;

public static class ForageCropRules {
    public static bool CanHarvest(bool forage,string kind)=>!forage||kind=="1";
    // Vanilla Crop.harvest handles spring onions separately from indexOfHarvest.
    public static string HarvestId(bool forage,string kind,string ordinary)=>forage&&kind=="1"?"(O)399":ordinary;
}
