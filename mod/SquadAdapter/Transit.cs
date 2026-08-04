using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    private static readonly Dictionary<string,(int Minute,int Buildings,string[] Names)> reachableCache=new();
    private static string[] Reachable(GameLocation start) {
        int minute=Game1.Date.TotalDays*3000+Game1.timeOfDay;
        int buildings=Game1.getFarm().buildings.Count;
        if(reachableCache.TryGetValue(start.NameOrUniqueName,out var known) && known.Minute==minute && known.Buildings==buildings)return known.Names;
        var visited=new HashSet<string>{start.NameOrUniqueName};var queue=new Queue<GameLocation>();queue.Enqueue(start);
        while(queue.Count>0 && visited.Count<100) {
            var location=queue.Dequeue();
            foreach(var warp in Exits(location,Game1.player.currentLocation.NameOrUniqueName)) {
                if(visited.Contains(warp.TargetName))continue;
                var target=Game1.getLocationFromName(warp.TargetName);if(target==null)continue;
                if(visited.Add(target.NameOrUniqueName))queue.Enqueue(target);
            }
        }
        var names=visited.ToArray();reachableCache[start.NameOrUniqueName]=(minute,buildings,names);return names;
    }
    private static IEnumerable<Warp> Exits(GameLocation location,string destination,string? home=null) {
        foreach(var warp in location.warps)yield return warp;
        foreach(var door in location.doors.Pairs) {
            var a=location.GetTilePropertySplitBySpaces("Action","Buildings",door.Key.X,door.Key.Y);
            if(a.Length<4 || a[0] is not ("Warp" or "LockedDoorWarp"))continue;
            if(!int.TryParse(a[1],out int x) || !int.TryParse(a[2],out int y))continue;
            if(a[0]=="LockedDoorWarp" && a[3]!=home && !DoorOpen(location,a))continue;
            yield return new Warp(door.Key.X,door.Key.Y,a[3],x,y,false);
        }
        foreach(var building in location.buildings) {
            var indoor=building.GetIndoors();if(indoor==null || building.daysOfConstructionLeft.Value>0 || building.humanDoor.Value.X<0)continue;
            var exit=indoor.warps.FirstOrDefault(w=>w.TargetName==location.NameOrUniqueName || w.TargetName==location.Name);
            if(exit==null)continue;
            yield return new Warp(building.tileX.Value+building.humanDoor.Value.X,building.tileY.Value+building.humanDoor.Value.Y,
                indoor.NameOrUniqueName,exit.X,Math.Max(0,exit.Y-1),false);
        }
        if(location.Name=="Town" && !Game1.MasterPlayer.mailReceived.Contains("JojaMember")) {
            foreach(var door in location.doors.Pairs)
                if(location.doesTileHaveProperty(door.Key.X,door.Key.Y,"Action","Buildings")=="WarpCommunityCenter" && Game1.player.eventsSeen.Contains("611439"))
                    yield return new Warp(door.Key.X,door.Key.Y,"CommunityCenter",32,23,false);
        }
        int targetLevel=-1;
        if(destination.StartsWith("UndergroundMine"))int.TryParse(destination[15..],out targetLevel);
        if(location is MineShaft mine) {
            var layer=mine.Map.GetLayer("Buildings");
            for(int y=0;y<layer.LayerHeight;y++)for(int x=0;x<layer.LayerWidth;x++) {
                int tile=layer.Tiles[x,y]?.TileIndex??-1;
                if(tile==115)yield return new Warp(x,y,"Mine",18,5,false);
                if(tile==173 && targetLevel==mine.mineLevel+1 && targetLevel<=120) {
                    var next=MineShaft.GetMine(MineShaft.GetLevelName(targetLevel));
                    yield return new Warp(x,y,next.NameOrUniqueName,(int)next.tileBeneathLadder.X,(int)next.tileBeneathLadder.Y,false);
                }
                if(tile==112 && targetLevel>0 && targetLevel<=120 && targetLevel%5==0 && targetLevel<=Game1.player.deepestMineLevel) {
                    var next=MineShaft.GetMine(MineShaft.GetLevelName(targetLevel));
                    yield return new Warp(x,y,next.NameOrUniqueName,(int)next.tileBeneathElevator.X,(int)next.tileBeneathElevator.Y,false);
                }
            }
        } else if(location.Name=="Mine" && targetLevel>=1 && targetLevel<=120) {
            // Read the actual interaction tiles rather than hard-coding a player's position.
            var layer=location.Map.GetLayer("Buildings");
            for(int y=0;y<layer.LayerHeight;y++)for(int x=0;x<layer.LayerWidth;x++) {
                string action=location.doesTileHaveProperty(x,y,"Action","Buildings")??"";
                if((action=="Mine" && targetLevel==1) || (action=="MineElevator" && targetLevel%5==0 && targetLevel<=Game1.player.deepestMineLevel)) {
                    var next=MineShaft.GetMine(MineShaft.GetLevelName(targetLevel));var spot=targetLevel==1?next.tileBeneathLadder:next.tileBeneathElevator;
                    yield return new Warp(x,y,next.NameOrUniqueName,(int)spot.X,(int)spot.Y,false);
                }
            }
        }
    }
    private static bool DoorOpen(GameLocation location,string[] action) {
        if(action.Length<6 || !int.TryParse(action[4],out int open) || !int.TryParse(action[5],out int close))return false;
        if(location.AreStoresClosedForFestival() && location.InValleyContext())return false;
        bool key=Game1.player.HasTownKey && location.InValleyContext();
        if(action[3]=="SeedShop" && Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)=="Wed" && !Utility.HasAnyPlayerSeenEvent("191393") && !key)return false;
        if(action[3]=="FishShop" && Game1.player.mailReceived.Contains("willyHours"))open=800;
        if(!key && (Game1.timeOfDay<open || Game1.timeOfDay>=close))return false;
        return action.Length<8 || !int.TryParse(action[7],out int points) || points<=0 || location.IsWinterHere()
            || (Game1.player.friendshipData.TryGetValue(action[6],out var friendship) && friendship.Points>=points);
    }
}
