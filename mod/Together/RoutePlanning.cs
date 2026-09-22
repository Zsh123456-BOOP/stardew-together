using Microsoft.Xna.Framework;
using StardewValley;
namespace Together;

internal sealed record RouteLeg(GameLocation Location,Point Entry,Warp Exit);
internal sealed record RouteResolution(Warp? First,Point Entry,int Transitions,string Reason,object[] Evidence) {public bool Reachable=>Reason=="reachable";}
public sealed partial class PlayerExecutor {
    internal static Func<GameLocation,Warp,string?>? DoorAccess;
    internal static bool CanReachInteraction(GameLocation location,Point tile) {
        var route=ResolveRoute(Game1.currentLocation,location.NameOrUniqueName);
        if(!route.Reachable)return false;
        var reachable=ReachableTiles(location,route.Entry,out bool exhausted);
        return !exhausted&&new[]{new Point(tile.X,tile.Y+1),new(tile.X-1,tile.Y),new(tile.X+1,tile.Y),new(tile.X,tile.Y-1)}.Any(p=>Passable(location,p)&&reachable.ContainsKey(p));
    }
    // Bounded by actual map cells; a search budget is never a physical obstruction.
    private static readonly Dictionary<string,(DateTime At,Dictionary<Point,int> Cells,bool Exhausted)> routeCells=new();
    private static Dictionary<Point,int> ReachableTiles(GameLocation l,Point start,out bool exhausted) {
        string key=FailureKnowledge.Hash(AgentJson.Encode(new{save=Game1.uniqueIDForThisGame,day=Game1.Date.TotalDays,location=l.NameOrUniqueName,start=new[]{start.X,start.Y},size=Game1.player.GetBoundingBox().Size.ToString(),objects=l.objects.Pairs.Select(p=>new{p.Key,p.Value.QualifiedItemId}),terrain=l.terrainFeatures.Pairs.Select(p=>new{p.Key,kind=p.Value.GetType().Name}),people=l.characters.Select(n=>new{n.Name,box=n.GetBoundingBox().ToString(),n.IsInvisible}),mail=Game1.player.mailReceived}));
        if(routeCells.TryGetValue(key,out var cached)&&(DateTime.UtcNow-cached.At).TotalMilliseconds<750){exhausted=cached.Exhausted;return cached.Cells;}
        int cells=l.Map.Layers[0].LayerWidth*l.Map.Layers[0].LayerHeight,limit=Math.Min(cells+1,50000);
        var queue=new Queue<Point>();var distances=new Dictionary<Point,int>{{start,0}};var seen=new HashSet<Point>{start};queue.Enqueue(start);
        while(queue.TryDequeue(out var at)&&distances.Count<limit)foreach(var d in new[]{new Point(1,0),new Point(-1,0),new Point(0,1),new Point(0,-1)}) {
            var p=new Point(at.X+d.X,at.Y+d.Y);if(!seen.Add(p)||!Passable(l,p))continue;distances[p]=distances[at]+1;queue.Enqueue(p);
        }
        if(distances.Count==1&&l==Game1.currentLocation&&start==Game1.player.TilePoint&&PlayerRouteController.EscapeTile() is {} escape) {
            distances[escape]=1;queue.Enqueue(escape);
            while(queue.TryDequeue(out var at)&&distances.Count<limit)foreach(var d in new[]{new Point(1,0),new Point(-1,0),new Point(0,1),new Point(0,-1)}) {
                var p=new Point(at.X+d.X,at.Y+d.Y);if(distances.ContainsKey(p)||!Passable(l,p))continue;distances[p]=distances[at]+1;queue.Enqueue(p);
            }
        }
        exhausted=queue.Count>0;
        if(routeCells.Count>=24)routeCells.Clear();routeCells[key]=(DateTime.UtcNow,distances,exhausted);return distances;
    }
    internal static RouteResolution ResolveRoute(GameLocation from,string destination,Point? initialEntry=null) {
        var start=initialEntry??(from==Game1.currentLocation?Game1.player.TilePoint:from.warps.Select(w=>new Point(w.X,w.Y)).FirstOrDefault());
        if(from==Game1.currentLocation&&!StaticMapPassable(from,start))return new(null,start,0,"route_origin_inside_static_collision",new object[]{new{location=from.NameOrUniqueName,entry=new[]{start.X,start.Y},bounds=Game1.player.GetBoundingBox().ToString(),position=Game1.player.Position.ToString(),Game1.player.ignoreCollisions,event_up=Game1.eventUp,temporary_passable=!Game1.player.TemporaryPassableTiles.IsEmpty(),note="当前位置是原生静态阻挡格；不能通过更换工具或瞬移伪造可达"}});
        if(from.NameOrUniqueName==destination)return new(null,start,0,"reachable",Array.Empty<object>());
        var excluded=new HashSet<string>();var evidence=new List<object>();
        string EdgeKey(RouteLeg leg)=>$"{leg.Location.NameOrUniqueName}:{leg.Entry.X},{leg.Entry.Y}:{leg.Exit.X},{leg.Exit.Y}:{leg.Exit.TargetName}";
        var flood=new Dictionary<(string,Point),Dictionary<Point,int>>();
        for(int attempt=0;attempt<64;attempt++) {
            var q=new PriorityQueue<(GameLocation Location,Point Entry,List<RouteLeg> Legs),int>();q.Enqueue((from,start,new()),0);
            var best=new Dictionary<(string,Point),int>{{(from.NameOrUniqueName,start),0}};List<RouteLeg>? chain=null;Point arrival=start;int searched=0;
            while(q.TryDequeue(out var node,out int cost)) {
                if(++searched>1000)return new(null,start,0,"route_search_budget_exhausted",evidence.ToArray());
                if(cost!=best[(node.Location.NameOrUniqueName,node.Entry)])continue;
                if(node.Location.NameOrUniqueName==destination){chain=node.Legs;arrival=node.Entry;break;}
                foreach(var edge in Exits(node.Location)) {
                    var leg=new RouteLeg(node.Location,node.Entry,edge);if(excluded.Contains(EdgeKey(leg)))continue;
                    var next=LoadedLocation(edge.TargetName);if(next==null)continue;
                    var entry=new Point(edge.TargetX,edge.TargetY);int nextCost=cost+1+(next.IsFarm&&node.Location.NameOrUniqueName!="BusStop"?8:0);
                    if(best.TryGetValue((next.NameOrUniqueName,entry),out int old)&&old<=nextCost)continue;
                    best[(next.NameOrUniqueName,entry)]=nextCost;q.Enqueue((next,entry,node.Legs.Append(leg).ToList()),nextCost);
                }
            }
            if(chain==null)return new(null,start,0,evidence.Count==0?"route_map_chain_missing":evidence.Any(e=>AgentJson.Encode(e).Contains("exit_stand"))?"route_exit_unreachable":"route_door_unavailable",evidence.ToArray());
            bool valid=true;
            foreach(var leg in chain) {
                string? gate=DoorAccess?.Invoke(leg.Location,leg.Exit);
                if(gate!=null){excluded.Add(EdgeKey(leg));evidence.Add(new{layer="door",reason=gate,location=leg.Location.NameOrUniqueName,exit=new[]{leg.Exit.X,leg.Exit.Y},target=leg.Exit.TargetName});valid=false;break;}
                if(!flood.TryGetValue((leg.Location.NameOrUniqueName,leg.Entry),out var distances)) {
                    distances=ReachableTiles(leg.Location,leg.Entry,out bool exhausted);flood[(leg.Location.NameOrUniqueName,leg.Entry)]=distances;
                    if(exhausted)return new(null,start,0,"route_search_budget_exhausted",evidence.ToArray());
                }
                var at=new Point(leg.Exit.X,leg.Exit.Y);var stands=new[]{at,new Point(at.X,at.Y+1),new Point(at.X-1,at.Y),new Point(at.X+1,at.Y),new Point(at.X,at.Y-1)};
                if(stands.Any(p=>distances.ContainsKey(p)))continue;
                excluded.Add(EdgeKey(leg));valid=false;
                evidence.Add(new{layer="exit_stand",location=leg.Location.NameOrUniqueName,entry=new[]{leg.Entry.X,leg.Entry.Y},exit=new[]{at.X,at.Y},target=leg.Exit.TargetName,reachable_cells=distances.Count,component=distances.Keys.OrderBy(p=>p.X).ThenBy(p=>p.Y).First().ToString(),stands=stands.Select(p=>new{tile=new[]{p.X,p.Y},passable=Passable(leg.Location,p)}),near_entry=Enumerable.Range(-1,3).SelectMany(x=>Enumerable.Range(-1,3).Select(y=>new Point(leg.Entry.X+x,leg.Entry.Y+y))).Select(p=>new{tile=new[]{p.X,p.Y},passable=Passable(leg.Location,p)}),npcs=leg.Location.characters.Where(n=>Vector2.DistanceSquared(n.Tile,leg.Entry.ToVector2())<16).Select(n=>new{n.Name,tile=new[]{n.TilePoint.X,n.TilePoint.Y},bounds=n.GetBoundingBox().ToString()}),player_bounds=Game1.player.GetBoundingBox().ToString()});break;
            }
            if(valid)return new(chain[0].Exit,arrival,chain.Count,"reachable",evidence.ToArray());
        }
        return new(null,start,0,"route_search_budget_exhausted",evidence.ToArray());
    }
}
