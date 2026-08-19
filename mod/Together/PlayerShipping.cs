using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;

namespace Together;
public sealed class ShipmentLine {
    public string item {get;set;}="";
    public int count {get;set;}
    public int quality {get;set;}
}
public sealed partial class PlayerExecutor {
    private List<ShipmentLine> shipment=new();
    private ShippingBin? shipmentBin;
    private Point shipmentTile;
    private void StartShipping(JsonElement args) {
        if(!args.TryGetProperty("items",out var raw))throw new InvalidOperationException("shipment_items_required");
        shipment=JsonSerializer.Deserialize<List<ShipmentLine>>(raw.GetRawText())??new();
        if(shipment.Count is <1 or >24||shipment.Any(i=>i.count is <1 or >999||i.quality is not (0 or 1 or 2 or 4)||ItemRegistry.GetData(i.item)==null))throw new InvalidOperationException("invalid_shipment_manifest");
        shipmentBin=null;destination="Farm";Current!.phase="shipping_travel";
    }
    private void TickShipping() {
        if(Game1.locationRequest!=null||Game1.fadeToBlack)return;
        if(Game1.activeClickableMenu is ItemGrabMenu {shippingBin:true} menu) {
            if(shipmentBin==null||menu.context!=shipmentBin||menu.heldItem!=null)throw new InvalidOperationException("shipping_menu_changed_or_cursor_occupied");
            if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(60);
            var line=shipment.FirstOrDefault(i=>i.count>0);
            if(line==null) {if(menu.readyToClose()){menu.exitThisMenu();Finish("succeeded");}return;}
            int slot=Enumerable.Range(0,Game1.player.Items.Count).Where(i=>Game1.player.Items[i] is StardewValley.Object o&&o.QualifiedItemId==line.item&&o.Quality>=line.quality&&o.canBeShipped()&&!o.questItem.Value).OrderBy(i=>Game1.player.Items[i].Quality).FirstOrDefault(-1);
            if(slot<0)throw new InvalidOperationException("shipment_missing_carried_unreserved_item");
            var item=Game1.player.Items[slot];int count=item.Stack<=line.count?item.Stack:1,quality=item.Quality;string id=item.QualifiedItemId;
            ValidateConsumption?.Invoke(new Dictionary<Item,int>{{item,count}},"","");
            var farm=Game1.getFarm();int beforeBag=Game1.player.Items.Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack),beforeBin=farm.getShippingBin(Game1.player).Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack);
            var bounds=menu.inventory.inventory[slot].bounds;var keyboard=Game1.oldKBState;
            try{Game1.oldKBState=default;if(count==item.Stack)menu.receiveLeftClick(bounds.Center.X,bounds.Center.Y);else menu.receiveRightClick(bounds.Center.X,bounds.Center.Y);}finally{Game1.oldKBState=keyboard;}
            int removed=beforeBag-Game1.player.Items.Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack),added=farm.getShippingBin(Game1.player).Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack)-beforeBin;
            if(removed!=count||added!=count||menu.heldItem!=null)throw new InvalidOperationException("native_shipment_conservation_failed");
            line.count-=count;Current!.completed+=count;
            if(Current.effects.Count<128)Current.effects.Add(new{kind="native_shipment",item=id,quality,count,income="pending_native_overnight"});return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("shipping_menu_interrupted");
        if(!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(shipmentBin==null) {
            foreach(var bin in Game1.getFarm().buildings.OfType<ShippingBin>().Where(b=>b.daysOfConstructionLeft.Value<=0).OrderBy(b=>Vector2.DistanceSquared(new(b.tileX.Value,b.tileY.Value),Game1.player.Tile))) {
                bool found=false;
                foreach(int dx in new[]{0,1}) {
                    var tile=new Point(bin.tileX.Value+dx,bin.tileY.Value);
                    try{Walk(Approach(tile,true));shipmentTile=tile;shipmentBin=bin;found=true;break;}catch(InvalidOperationException){}
                }
                if(found)break;
            }
            if(shipmentBin==null)throw new InvalidOperationException("native_shipping_bin_unreachable");
        }
        if(Game1.player.TilePoint!=target){MonitorWalk();return;}StopWalk();Adjacent(shipmentTile);Face(shipmentTile);
        if(!NativeMenuInput.InteractWorld(shipmentTile)||Game1.activeClickableMenu is not ItemGrabMenu {shippingBin:true})throw new InvalidOperationException("native_shipping_menu_did_not_open");Current!.phase="shipping_manifest";
    }
}
