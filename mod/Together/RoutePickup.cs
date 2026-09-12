using Microsoft.Xna.Framework;
using StardewValley;

namespace Together;
public sealed partial class PlayerExecutor {
    public Func<Item,bool>? OpportunisticItemAllowed {get;set;}
    private string pickupRoute="";
    private int routePickups;
    private DateTime nextRoutePickup;
    private void TryRoutePickup() {
        if(Current?.skill is not ("player.travel" or "player.move")||ownedController?.pathToEndPoint is not {Count:>0} path||Game1.activeClickableMenu!=null||Game1.eventUp||!Game1.player.CanMove||Game1.player.UsingTool||DateTime.UtcNow<nextRoutePickup)return;
        if(pickupRoute!=Current.command_id){pickupRoute=Current.command_id;routePickups=0;}
        if(routePickups>=3)return;
        nextRoutePickup=DateTime.UtcNow.AddMilliseconds(500);
        var p=Game1.player;var l=Game1.currentLocation;var at=p.TilePoint;
        foreach(var tile in new[]{new Point(at.X+1,at.Y),new Point(at.X-1,at.Y),new Point(at.X,at.Y+1),new Point(at.X,at.Y-1)}) {
            if(!l.objects.TryGetValue(tile.ToVector2(),out var item)||!item.isForage()||item.bigCraftable.Value||item.questItem.Value||OpportunisticItemAllowed?.Invoke(item)!=true)continue;
            if(!path.Take(3).Any(t=>Math.Abs(t.X-tile.X)+Math.Abs(t.Y-tile.Y)<=1))continue;
            // Native forage can roll quality/double yield. Simulate each possible
            // outcome conservatively rather than assuming the map object's stack.
            bool fits=new[]{0,1,2,4}.All(quality=>{var output=item.getOne();output.Quality=quality;output.Stack=2;return CapacityAdapter.Receive(p,output).Feasible;});
            if(!fits)continue;
            int before=p.Items.Where(i=>i?.QualifiedItemId==item.QualifiedItemId).Sum(i=>i.Stack);
            // No route mutation, teleport or direct inventory writes. The native
            // interaction alone decides whether a nearby object can be gathered.
            bool accepted=Game1.tryToCheckAt(tile.ToVector2(),p);routePickups++;
            int after=p.Items.Where(i=>i?.QualifiedItemId==item.QualifiedItemId).Sum(i=>i.Stack);
            Current.effects.Add(new{kind="native_route_pickup",location=l.NameOrUniqueName,tile=new[]{tile.X,tile.Y},actor_tile=new[]{at.X,at.Y},path_head=path.Take(3).Select(t=>new[]{t.X,t.Y}).ToArray(),item=item.QualifiedItemId,accepted,gained=after-before,removed=!l.objects.ContainsKey(tile.ToVector2()),limit=3});
            break;
        }
    }
}
