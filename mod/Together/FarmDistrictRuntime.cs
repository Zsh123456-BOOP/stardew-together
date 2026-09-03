using StardewValley;
using StardewValley.TerrainFeatures;
namespace Together;
public sealed partial class ModEntry {
    private FarmCell FarmHome(GameLocation l) {
        var home=l.buildings.FirstOrDefault(b=>b.buildingType.Value=="Farmhouse");
        return home==null?new(Game1.player.TilePoint.X,Game1.player.TilePoint.Y):new(home.tileX.Value+home.humanDoor.Value.X,home.tileY.Value+home.humanDoor.Value.Y+1);
    }
    private FarmDistrict District(GameLocation l) {
        if(!Data.Operating.Districts.TryGetValue(l.NameOrUniqueName,out var d))Data.Operating.Districts[l.NameOrUniqueName]=d=new();
        if(d.Field.Count==0)d.Field=FarmDistrict.LargestCluster(l.terrainFeatures.Pairs.Where(p=>p.Value is HoeDirt {crop:not null}).Select(p=>new FarmCell((int)p.Key.X,(int)p.Key.Y)),FarmHome(l));
        return d;
    }
    private List<LayoutCell> DistrictGrid(GameLocation l,List<LayoutCell> grid)=>l.IsGreenhouse?grid:District(l).Constrain(grid,FarmHome(l));
}
