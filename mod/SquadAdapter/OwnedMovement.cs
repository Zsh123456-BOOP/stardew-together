using Microsoft.Xna.Framework;
using StardewValley;
using TheStardewSquad.Framework;
using TheStardewSquad.Framework.Squad;
using TheStardewSquad.Framework.Wrappers;
using TheStardewSquad.Pathfinding;

namespace TheStardewSquad;

public sealed partial class CompanionControl {
    private readonly HashSet<string> stay=new();
    // Map warps, unlocked doors, constructed building doors and actual mine stairs.
    private static Warp? NextExit(GameLocation start,string destination,string? home=null) {
        var queue=new Queue<(GameLocation Location,Warp? First)>();
        var visited=new HashSet<string>{start.NameOrUniqueName};queue.Enqueue((start,null));
        while(queue.Count>0 && visited.Count<100) {
            var node=queue.Dequeue();
            foreach(var edge in Exits(node.Location,destination,home)) {
                var next=LoadedLocation(edge.TargetName);
                if(next==null || !visited.Add(next.NameOrUniqueName))continue;
                var first=node.First??edge;
                if(next.NameOrUniqueName==destination)return first;
                queue.Enqueue((next,first));
            }
        }
        return null;
    }
    private bool Travel(ISquadMate mate,string destination,bool slow,Farmer player) {
        var location=mate.Npc.currentLocation;
        if(location.NameOrUniqueName==destination)return true;
        var edge=NextExit(location,destination,destination==mate.Npc.DefaultMap?destination:null);
        if(edge==null){mate.Halt();return false;}
        var tile=new Point(edge.X,edge.Y);
        // A map warp is activated at its boundary, never at the player's arbitrary tile.
        if(Vector2.Distance(mate.Npc.Tile,tile.ToVector2())<=1.15f) {
            var target=LoadedLocation(edge.TargetName);
            if(target==null)return false;
            Game1.warpCharacter(mate.Npc,target,new Vector2(edge.TargetX,edge.TargetY));
            mate.Path.Clear();mate.CurrentMoveDirection=-1;mate.IsCatchingUp=false;mate.Halt();
        } else {
            var point=AStarPathfinder.IsTilePassableForFollower(location,tile,mate.Npc)?tile:StandingSpot(mate,tile);
            if(point.HasValue)mod.FollowerManager.WalkAgent(mate,point.Value,slow,player);else mate.Halt();
        }
        return false;
    }
    public static bool DriveOwned(ISquadMate mate,Farmer player,bool fast,bool slow) {
        if(instance==null || !IsManaged(mate))return false;
        long before=System.Diagnostics.Stopwatch.GetTimestamp();
        try{return DriveOwnedCore(mate,player,fast,slow);}
        finally {
            movementSamples.Enqueue((System.Diagnostics.Stopwatch.GetTimestamp()-before)*1000.0/System.Diagnostics.Stopwatch.Frequency);
            if(movementSamples.Count>3600)movementSamples.Dequeue();
        }
    }
    private static readonly Queue<double> movementSamples=new();
    private static DateTime performanceUntil;
    private static object? movementPerformance;
    private static object MovementPerformance() {
        if(movementPerformance!=null && DateTime.UtcNow<performanceUntil)return movementPerformance;
        performanceUntil=DateTime.UtcNow.AddSeconds(5);
        var samples=movementSamples.OrderBy(v=>v).ToArray();
        return movementPerformance=new{samples=samples.Length,p95_ms=samples.Length==0?0:samples[(int)((samples.Length-1)*.95)],max_ms=samples.Length==0?0:samples[^1],scope="one managed companion body update"};
    }
    private static bool DriveOwnedCore(ISquadMate mate,Farmer player,bool fast,bool slow) {
        if(instance==null || !IsManaged(mate))return false;
        var self=instance;var npc=mate.Npc;
        if(self.labCollectWalk.TryGetValue(npc,out var pickup)) {
            var standing=new Vector2((int)npc.Position.X+pickup.Offset.X,(int)npc.Position.Y+pickup.Offset.Y);
            if(Math.Abs(pickup.Center.X-standing.X)<=64 && Math.Abs(pickup.Center.Y-standing.Y)<=64)mate.Halt();
            else {
                // Convert the Farmer pickup center to the NPC navigation's standing coordinate.
                var destination=pickup.Center-pickup.Offset.ToVector2()+(npc.getStandingPosition()-npc.Position);
                self.mod.FollowerManager.WalkAgent(mate,(destination/64f).ToPoint(),slow,player);
            }
            return true;
        }
        if(self.labBodyWalk.TryGetValue(npc,out var labTarget)) {
            if(npc.TilePoint!=labTarget)self.mod.FollowerManager.WalkAgent(mate,labTarget,slow,player);else mate.Halt();
            return true;
        }
        npc.speed=Math.Clamp((int)player.getMovementSpeed(),2,5);mate.IsCatchingUp=false;
        var active=self.records.Values.LastOrDefault(r=>r.Actor==Id(mate) && r.Status=="running");
        if(active?.Skill=="dismiss"){self.DriveHome(active,slow,player);return true;}
        if(fast && active?.Skill!="travel") {
            var info=new LocationInfoWrapper(npc.currentLocation,npc);
            // Anchor defense to this NPC, including when the player works elsewhere.
            var task=self.mod.UnifiedTaskManager.FindAttackingTask(mate,info,npc.TilePoint,npc.TilePoint,new HashSet<Vector2>());
            if(task!=null && !(mate.Task?.Type==TaskType.Attacking && mate.Task.TargetCharacter==task.TargetCharacter))
                self.mod.FollowerManager.AssignAgentTask(mate,task);
        }
        if(mate.Task!=null) {
            if(mate.Task.Type==TaskType.Fishing && npc.TilePoint==mate.Task.InteractionTile) {
                if(fast)TaskManager.AnimateFishing(npc,mate.Task.Tile);
            } else {using var output=OwnOutput(mate);self.mod.FollowerManager.DriveAgentTask(mate,slow,player);}
            return true;
        }
        if(active?.Skill is "buy" or "ship" && !active.EffectByActor) {self.DriveEconomy(active,slow,player);return true;}
        if(active?.Skill is "clear" or "till" or "plant" or "feed" or "tend" or "forage" && !active.EffectByActor) {self.DriveProduction(active,slow,player);return true;}
        if(active?.Skill is "gift" or "refill" or "deposit" && !active.EffectByActor) {self.DriveResources(active,slow,player);return true;}
        if(active?.Skill=="collect" && !active.EffectByActor) {
            if(npc.TilePoint!=active.Stand)self.mod.FollowerManager.WalkAgent(mate,active.Stand,slow,player);
            else {
                mate.Halt();npc.faceGeneralDirection(active.Target.ToVector2()*64);
                active.WorkSeconds+=Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
                if(active.WorkSeconds>=.5 && self.PendingEffect(active)) {
                    var machine=(StardewValley.Object)active.Source!;
                    string output=machine.heldObject.Value.QualifiedItemId;
                    using(var outputScope=OwnOutput(mate))machine.checkForAction(player,false);
                    if(!self.PendingEffect(active)){active.EffectByActor=true;active.Catches.Add(output);mate.ActionCooldown=24;}
                    else self.Finish(active,"failed","inventory_full_or_machine_rejected");
                }
            }
            return true;
        }
        if(active?.Skill=="travel") {self.Travel(mate,active.Destination!,slow,player);return true;}
        if(self.stay.Contains(Id(mate))){mate.Halt();return true;}
        if(npc.currentLocation!=player.currentLocation){self.Travel(mate,player.currentLocation.NameOrUniqueName,slow,player);return true;}
        if(Vector2.Distance(npc.Tile,player.Tile)>3) self.mod.FollowerManager.WalkAgent(mate,player.TilePoint,slow,player);
        else mate.Halt();
        return true;
    }
}
