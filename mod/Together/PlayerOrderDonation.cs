using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.SpecialOrders;
using StardewValley.SpecialOrders.Objectives;

namespace Together;
public sealed partial class PlayerExecutor {
    private SpecialOrder? donatingOrder;
    private string orderDropBox="";
    private Point? orderDropTile;
    private readonly HashSet<int> orderDonationSkipped=new();
    internal static object ReadOrderDonations()=>Game1.player.team.specialOrders.Select(o=>new{id=o.questKey.Value,name=o.GetName(),state=o.questState.Value.ToString(),deadline=o.dueDate.Value,dropboxes=o.objectives.OfType<DonateObjective>().Select(d=>new{id=d.dropBox.Value,location=d.GetDropboxLocationName(),current=d.GetCount(),required=d.GetMaxCount(),confirmed=d.confirmed.Value,tags=d.acceptableContextTagSets.ToArray()}),accepted_inventory=Game1.player.Items.Where(i=>i!=null&&o.GetAcceptCount(i)>0).Select(i=>new{item=i.QualifiedItemId,quality=i.Quality,count=Math.Min(i.Stack,o.GetAcceptCount(i))})}).ToArray();
    private void StartOrderDonation(JsonElement args) {
        string key=AgentToolRegistry.Text(args,"order");orderDropBox=AgentToolRegistry.Text(args,"dropbox");
        donatingOrder=Game1.player.team.specialOrders.FirstOrDefault(o=>o.questKey.Value==key&&o.UsesDropBox(orderDropBox))??throw new InvalidOperationException("active_order_dropbox_required");
        var objective=donatingOrder.objectives.OfType<DonateObjective>().FirstOrDefault(d=>d.dropBox.Value==orderDropBox)??throw new InvalidOperationException("donation_objective_missing");
        destination=objective.GetDropboxLocationName();if(Game1.getLocationFromName(destination)==null)throw new InvalidOperationException("dropbox_location_not_available");
        orderDropTile=null;orderDonationSkipped.Clear();Current!.phase="order_donation_travel";
    }
    private void TickOrderDonation() {
        var order=donatingOrder!;
        if(Game1.locationRequest!=null||Game1.fadeToBlack)return;
        if(Game1.activeClickableMenu is QuestContainerMenu menu) {
            if(!ReferenceEquals(menu.stackCapacityCheck?.Target,order))throw new InvalidOperationException("different_order_donation_menu");
            if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(180);
            int slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>!orderDonationSkipped.Contains(i)&&Game1.player.Items[i] is {} item&&menu.GetDonatableAmount(item)>0,-1);
            if(slot<0) {
                if(!menu.readyToClose())return;menu.exitThisMenu();
                Current!.effects.Add(new{kind="native_order_donation_confirmed",order=order.questKey.Value,state=order.questState.Value.ToString(),objectives=order.objectives.Select(o=>new{type=o.GetType().Name,current=o.GetCount(),required=o.GetMaxCount()})});
                Finish(Current.completed>0?"succeeded":"failed",Current.completed>0?null:"no_eligible_unreserved_order_items");return;
            }
            var item=Game1.player.Items[slot];string id=item.QualifiedItemId;int quality=item.Quality,amount=menu.GetDonatableAmount(item);
            try{ValidateConsumption?.Invoke(new Dictionary<Item,int>{{item,amount}},"","order:"+order.questKey.Value);}catch(InvalidOperationException){orderDonationSkipped.Add(slot);return;}
            int before=Game1.player.Items.Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack),stored=order.donatedItems.Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack);
            var bounds=menu.inventory.inventory[slot].bounds;menu.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
            int removed=before-Game1.player.Items.Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack),added=order.donatedItems.Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack)-stored;
            if(removed<=0||removed!=added||removed>amount)throw new InvalidOperationException("native_order_donation_conservation_failed");
            Current!.completed+=removed;Current.effects.Add(new{kind="native_order_items_deposited",order=order.questKey.Value,item=id,quality,count=removed});return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("order_donation_menu_interrupted");
        if(!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        var location=Game1.currentLocation;
        if(orderDropTile==null) {
            var candidates=new List<Point>();
            for(int y=0;y<location.Map.Layers[0].LayerHeight;y++)for(int x=0;x<location.Map.Layers[0].LayerWidth;x++) {
                var action=location.GetTilePropertySplitBySpaces("Action","Buildings",x,y);if(action.Length>1&&action[0]=="DropBox"&&action[1]==orderDropBox)candidates.Add(new(x,y));
            }
            foreach(var tile in candidates.OrderBy(t=>Vector2.DistanceSquared(t.ToVector2(),Game1.player.Tile)))try{Walk(Approach(tile,true));orderDropTile=tile;break;}catch(InvalidOperationException){}
            if(orderDropTile==null)throw new InvalidOperationException("native_order_dropbox_unreachable");
        }
        if(Current!.phase=="order_donation_opening")return;
        if(Game1.player.TilePoint!=target){MonitorWalk();return;}
        StopWalk();Face(orderDropTile.Value);Adjacent(orderDropTile.Value);
        if(!Game1.tryToCheckAt(orderDropTile.Value.ToVector2(),Game1.player))throw new InvalidOperationException("native_order_dropbox_rejected");Current.phase="order_donation_opening";
    }
}
