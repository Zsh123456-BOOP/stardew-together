using StardewValley;
using StardewValley.GameData.Crops;
namespace Together;
internal static class NativeSeedPlan {
    internal static Dictionary<string,CropData> Options(string seed,GameLocation location) {
        var crops=DataLoader.Crops(Game1.content);
        return SeedPossibilities.Resolve(seed,(int)location.GetSeason(),location is StardewValley.Locations.IslandLocation).Where(crops.ContainsKey).ToDictionary(id=>id,id=>crops[id]);
    }
    internal static bool Fits(CropData crop,GameLocation location)=>location.SeedsIgnoreSeasonsHere()||crop.Seasons.Contains(location.GetSeason())&&Game1.dayOfMonth+crop.DaysInPhase.Sum()<=CropGrowth.SeasonEnd((int)location.GetSeason(),Game1.dayOfMonth,crop.Seasons.Select(s=>(int)s).ToHashSet(),false);
}
