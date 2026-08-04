using Microsoft.Xna.Framework;
using StardewValley;
using TheStardewSquad.Framework.Wrappers;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    public static Stack<Point>? FindOwnedPath(GameLocation location,Point start,Point end,Character character) {
        var map=new MapInfoWrapper(location,character);
        int width=location.Map.Layers[0].LayerWidth,height=location.Map.Layers[0].LayerHeight;
        if(end.X<0 || end.Y<0 || end.X>=width || end.Y>=height)return null;
        var passable=new Dictionary<Point,bool>();
        bool Pass(Point p) {
            if(p.X<0 || p.Y<0 || p.X>=width || p.Y>=height)return false;
            if(!passable.TryGetValue(p,out bool value))passable[p]=value=map.IsTilePassable(p);
            return value;
        }
        int Distance(Point p) {int x=Math.Abs(p.X-end.X),y=Math.Abs(p.Y-end.Y);return 14*Math.Min(x,y)+10*Math.Abs(x-y);}
        if(!Pass(end))return null;
        var open=new PriorityQueue<Point,(int Cost,int Remaining)>();
        var cost=new Dictionary<Point,int>{{start,0}};var parent=new Dictionary<Point,Point>();var closed=new HashSet<Point>();
        open.Enqueue(start,(Distance(start),Distance(start)));
        // Full core maps can require >500 expansions even for a reachable doorway.
        // Bound work by actual map size, with a hard cap for unsupported huge maps.
        int limit=Math.Min(16384,width*height);
        while(open.Count>0 && closed.Count<limit) {
            var current=open.Dequeue();if(!closed.Add(current))continue;
            if(current==end) {
                var path=new Stack<Point>();path.Push(current);
                while(parent.TryGetValue(current,out var previous)){current=previous;path.Push(current);}
                return path;
            }
            for(int x=-1;x<=1;x++)for(int y=-1;y<=1;y++) {
                if(x==0 && y==0)continue;var next=new Point(current.X+x,current.Y+y);
                if(closed.Contains(next) || !Pass(next))continue;
                if(x!=0 && y!=0 && (!Pass(new(current.X+x,current.Y)) || !Pass(new(current.X,current.Y+y))))continue;
                int distance=cost[current]+(x!=0 && y!=0?14:10);
                if(cost.TryGetValue(next,out int known) && known<=distance)continue;
                cost[next]=distance;parent[next]=current;int remaining=Distance(next);open.Enqueue(next,(distance+remaining,remaining));
            }
        }
        return null;
    }
}
