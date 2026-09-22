using Microsoft.Xna.Framework;
using StardewValley;
namespace Together;

internal sealed record SocialReachability(string npc,string location,bool reachable,string reason,int[]? stand,int? approach_steps,int map_transitions,object[] doors);
public sealed partial class PlayerExecutor {
    // Read-only endpoint preview. Never swap Game1.player or instantiate a native
    // controller on an unoccupied map (the latter can teleport a Farmer).
    internal static SocialReachability SocialReach(NPC npc) {
        var location=npc.currentLocation;
        SocialReachability Result(bool ok,string reason,Point? stand=null,int? steps=null,int maps=0,object[]? doors=null)=>new(npc.Name,location?.NameOrUniqueName??"",ok,reason,stand is {} p?new[]{p.X,p.Y}:null,steps,maps,doors??Array.Empty<object>());
        if(location==null||npc.IsInvisible||npc.isSleeping.Value||npc.IsMonster)return Result(false,"npc_unavailable_or_sleeping");
        var start=Game1.player.TilePoint;int transitions=0;
        if(location!=Game1.currentLocation) {
            var route=ResolveRoute(Game1.currentLocation,location.NameOrUniqueName);
            if(!route.Reachable)return Result(false,route.Reason,doors:route.Evidence);
            start=route.Entry;transitions=route.Transitions;
        }
        var at=npc.StandingPixel;var center=new Point(at.X/64,at.Y/64);
        var options=Enumerable.Range(-1,3).SelectMany(x=>Enumerable.Range(-1,3).Select(y=>new Point(center.X+x,center.Y+y))).Where(p=>p!=center&&Passable(location,p)).ToHashSet();
        // One flood fill for all eight endpoints. Actual collision protects rooms,
        // furniture, counters and moving characters; no diagonal corner cutting.
        var q=new Queue<Point>();var distance=new Dictionary<Point,int>{{start,0}};q.Enqueue(start);
        while(q.TryDequeue(out var p)&&distance.Count<10000) {
            if(options.Contains(p))return Result(true,"reachable_native_stand",p,distance[p],transitions);
            foreach(var d in new[]{new Point(0,1),new Point(1,0),new Point(0,-1),new Point(-1,0)}) {
                var next=new Point(p.X+d.X,p.Y+d.Y);if(distance.ContainsKey(next)||!Passable(location,next))continue;
                distance[next]=distance[p]+1;q.Enqueue(next);
            }
        }
        // Explain access requirements from the native map instead of inviting
        // another blind trip. We do not open doors or award unlock flags here.
        var locks=new List<object>();var layer=location.Map.GetLayer("Buildings");
        if(layer!=null)for(int y=0;y<layer.LayerHeight;y++)for(int x=0;x<layer.LayerWidth;x++) {
            var action=location.GetTilePropertySplitBySpaces("Action","Buildings",x,y);
            if(action.Length==0||action[0] is not ("Door" or "ConditionalDoor"))continue;
            locks.Add(new{tile=new[]{x,y},kind=action[0],condition=string.Join(" ",action.Skip(1)),friendship=action[0]=="Door"?action.Skip(1).Select(n=>new{npc=n,hearts=Game1.player.getFriendshipHeartLevelForNPC(n),required_hearts=2,previously_unlocked=Game1.player.mailReceived.Contains("doorUnlock"+n)}).ToArray():null});
        }
        return Result(false,"no_reachable_native_stand_from_entry",maps:transitions,doors:locks.ToArray());
    }
}
