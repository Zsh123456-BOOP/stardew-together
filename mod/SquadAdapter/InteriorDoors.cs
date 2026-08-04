using Microsoft.Xna.Framework;
using StardewValley;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    public static bool CanUseInteriorDoor(GameLocation location,Point tile,Character character) {
        if(character is not NPC npc || !IsManagedNpc(npc))return false;
        var action=location.GetTilePropertySplitBySpaces("Action","Buildings",tile.X,tile.Y);
        // Off-screen maps have not populated interiorDoors yet. Read the real Action tile.
        // NPCs can leave their own room and use shared hall doors; this never unlocks a player's access flags.
        return action.Length>0 && action[0]=="Door" && (action.Length==1 || action.Skip(1).Contains(npc.Name));
    }
    public static void OpenInteriorDoorIfNeeded(NPC npc,Point next) {
        if(CanUseInteriorDoor(npc.currentLocation,next,npc))
            npc.currentLocation.openDoor(new xTile.Dimensions.Location(next.X,next.Y),npc.currentLocation==Game1.currentLocation);
    }
}
