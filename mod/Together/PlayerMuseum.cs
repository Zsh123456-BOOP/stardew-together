using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private string museumItem="",museumHeld="";
    private int museumLimit,museumBeforeCount,museumBagCount;
    private readonly HashSet<string> museumProtected=new();
    private void StartMuseumDonation(JsonElement args) {
        if(Game1.activeClickableMenu is not MuseumMenu {reOrganizing:false,heldItem:null})throw new InvalidOperationException("native_museum_donation_menu_required");
        museumItem=AgentToolRegistry.Text(args,"item");museumLimit=AgentToolRegistry.Number(args,"count");
        if(museumLimit is <0 or >100)throw new InvalidOperationException("invalid_donation_count");
        museumProtected.Clear();museumHeld="";Current!.phase="museum_select";
    }
    private void TickMuseumDonation() {
        if(Current!.phase=="museum_close") {
            if(Game1.activeClickableMenu==null){Finish("succeeded");return;}
            if(Game1.activeClickableMenu is not MuseumMenu)throw new InvalidOperationException("museum_close_menu_changed");
            return;
        }
        if(Game1.activeClickableMenu is not MuseumMenu menu)throw new InvalidOperationException("museum_menu_changed");
        if(menu.fadeTimer>0||menu.state!=1||DateTime.UtcNow<nextInteraction)return;
        nextInteraction=DateTime.UtcNow.AddMilliseconds(120);
        if(museumHeld.Length>0) {
            if(menu.heldItem==null||menu.heldItem.QualifiedItemId!=museumHeld)throw new InvalidOperationException("museum_held_item_changed");
            Vector2 at=menu.Museum.getFreeDonationSpot();
            if(!menu.Museum.isTileSuitableForMuseumPiece((int)at.X,(int)at.Y))throw new InvalidOperationException("museum_no_free_exhibit");
            int screenX=(int)Utility.ModifyCoordinateForUIScale(at.X*64+32-Game1.viewport.X);
            int screenY=(int)Utility.ModifyCoordinateForUIScale(at.Y*64+32-Game1.viewport.Y);
            if(screenX<64||screenX>Game1.uiViewport.Width-64||screenY<64||screenY>menu.yPositionOnScreen-32) {
                Game1.panScreen(screenX<64?-16:screenX>Game1.uiViewport.Width-64?16:0,screenY<64?-16:screenY>menu.yPositionOnScreen-32?16:0);return;
            }
            menu.receiveLeftClick(screenX,screenY);
            int bag=Game1.player.Items.Where(i=>i?.QualifiedItemId==museumHeld).Sum(i=>i.Stack);
            if(menu.heldItem!=null||menu.Museum.museumPieces.Length!=museumBeforeCount+1||!menu.Museum.museumPieces.TryGetValue(at,out string? donated)||"(O)"+donated!=museumHeld||museumBagCount-bag!=1)throw new InvalidOperationException("native_museum_donation_not_verified");
            Current.effects.Add(new{kind="native_museum_donation",item=museumHeld,tile=at,consumed=1,museum_count=menu.Museum.museumPieces.Length});
            Current.completed++;museumHeld="";return;
        }
        if(menu.menuMovingDown||menu.menuPositionOffset>0)return;
        int slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is {} item&&(museumItem.Length==0||item.QualifiedItemId==museumItem)&&!museumProtected.Contains(item.QualifiedItemId)&&menu.Museum.isItemSuitableForDonation(item),-1);
        if(slot<0||museumLimit>0&&Current.completed>=museumLimit) {
            if(Current.completed==0&&museumItem.Length>0)throw new InvalidOperationException("requested_museum_item_absent_already_donated_or_reserved");
            if(!menu.readyToClose())return;
            var close=menu.okButton.bounds;menu.receiveLeftClick(close.Center.X,close.Center.Y);Current.phase="museum_close";return;
        }
        var selected=Game1.player.Items[slot];
        try{ValidateConsumption?.Invoke(new Dictionary<Item,int>{{selected,1}},"","");}
        catch(InvalidOperationException e){museumProtected.Add(selected.QualifiedItemId);Current.effects.Add(new{kind="museum_item_reserved",item=selected.QualifiedItemId,reason=e.Message});return;}
        museumBeforeCount=menu.Museum.museumPieces.Length;museumBagCount=Game1.player.Items.Where(i=>i?.QualifiedItemId==selected.QualifiedItemId).Sum(i=>i.Stack);
        if(slot>=menu.inventory.inventory.Count)throw new InvalidOperationException("museum_inventory_slot_not_visible");
        string itemId=selected.QualifiedItemId;var bounds=menu.inventory.inventory[slot].bounds;menu.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
        if(menu.heldItem?.QualifiedItemId!=itemId)throw new InvalidOperationException("museum_native_pickup_rejected");
        museumHeld=itemId;
    }
}
