namespace Together;
// Native Crop.ResolveSeedId/getRandomLowGradeCropForThisSeason, SDV 1.6.15.
// Enumerate outcomes without constructing Crop or advancing Game1.random.
public static class SeedPossibilities {
    public static string[] Resolve(string seed,int season,bool island=false) {
        seed=seed.StartsWith("(O)")?seed[3..]:seed;
        if(seed is not ("770" or "MixedFlowerSeeds"))return new[]{seed};
        if(seed=="770"&&island)return new[]{"479","833","481","478"};
        if(season==3)return Enumerable.Range(0,3).SelectMany(s=>Resolve(seed,s)).Distinct().ToArray();
        return (seed,season) switch {
            ("770",0)=>new[]{"472","474","475"},("770",1)=>new[]{"487","483","482","484"},("770",2)=>new[]{"487","488","489","490"},
            ("MixedFlowerSeeds",0)=>new[]{"427","429"},("MixedFlowerSeeds",1)=>new[]{"455","453","431"},("MixedFlowerSeeds",2)=>new[]{"431","425"},_=>Array.Empty<string>()
        };
    }
}
