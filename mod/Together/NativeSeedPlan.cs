using StardewValley;
using StardewValley.GameData.Crops;
namespace Together;
internal static class NativeSeedPlan {
    internal static Dictionary<string,CropData> Options(string seed,GameLocation location) {
        var crops=DataLoader.Crops(Game1.content);
        return SeedPossibilities.Resolve(seed,(int)location.GetSeason(),location is StardewValley.Locations.IslandLocation).Where(crops.ContainsKey).ToDictionary(id=>id,id=>crops[id]);
    }
    internal static int Growth(CropData crop,GameLocation location,float fertilizer=0,bool paddy=false)=>CropGrowth.Stages(crop.DaysInPhase,fertilizer,Game1.player.professions.Contains(5),paddy).Sum();
    internal static int EarliestGrowth(CropData crop,GameLocation location) {
        int days=Growth(crop,location);
        foreach(var tile in location.terrainFeatures.Pairs.Where(t=>t.Value is StardewValley.TerrainFeatures.HoeDirt {crop:null})) {
            var soil=(StardewValley.TerrainFeatures.HoeDirt)tile.Value;
            bool paddy=crop.IsPaddyCrop&&Enumerable.Range(-3,7).Any(x=>Enumerable.Range(-3,7).Any(y=>location.CanRefillWateringCanOnTile((int)tile.Key.X+x,(int)tile.Key.Y+y)));
            days=Math.Min(days,Growth(crop,location,soil.GetFertilizerSpeedBoost(),paddy));
        }
        return days;
    }
    internal static bool Fits(CropData crop,GameLocation location)=>location.SeedsIgnoreSeasonsHere()||crop.Seasons.Contains(location.GetSeason())&&Game1.dayOfMonth+EarliestGrowth(crop,location)<=CropGrowth.SeasonEnd((int)location.GetSeason(),Game1.dayOfMonth,crop.Seasons.Select(s=>(int)s).ToHashSet(),false);
}
