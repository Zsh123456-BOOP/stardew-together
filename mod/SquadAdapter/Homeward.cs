using Microsoft.Xna.Framework;
using StardewValley;
using TheStardewSquad.Framework.Behaviors;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    private static readonly HashSet<NPC> keepDismissalPosition=new();
    public static bool KeepDismissalPosition(NPC npc)=>keepDismissalPosition.Contains(npc);
    private void DriveHome(Record r,bool slow,Farmer player) {
        r.ActiveSeconds+=(float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
        if(r.ActiveSeconds>300){Finish(r,"failed","home_route_unavailable");return;}
        var mate=r.Mate;var npc=mate.Npc;
        if(npc.currentLocation.NameOrUniqueName!=r.Destination){Travel(mate,r.Destination!,slow,player);return;}
        if(Vector2.Distance(npc.Tile,r.Target.ToVector2())>1.5f) {mod.FollowerManager.WalkAgent(mate,r.Target,slow,player);return;}
        mate.Halt();keepDismissalPosition.Add(npc);
        try {mod.RecruitmentManager.Dismiss(mate,isSilent:true,warpBehavior:DismissalWarpBehavior.RoamHere);}
        finally {keepDismissalPosition.Remove(npc);}
        Finish(r,"succeeded");managed.Remove(r.Actor);stay.Remove(r.Actor);
    }
}
