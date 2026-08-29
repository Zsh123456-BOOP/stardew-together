using StardewValley;
using StardewValley.Objects;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private bool homeGiftOpening;
    internal static bool HasHomeGift()=>Utility.getHomeOfFarmer(Game1.player).objects.Values
        .OfType<Chest>().Any(c=>c.giftbox.Value&&c.GetItemsForPlayer().Any(i=>i!=null));

    private void StartHomeSupplies() {
        homeGiftOpening=false;
        destination=Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName;
        Current!.phase="home_supplies_travel";
    }

    private void TickHomeSupplies() {
        if(homeGiftOpening) {
            // Starter gifts display a native explanatory letter after delivering
            // their items. Only acknowledge notices owned by this opened gift.
            if(Current!.phase=="treasure_opening"&&treasureChest!=null&&!treasureChest.GetItemsForPlayer().Any(i=>i!=null)
                &&Game1.activeClickableMenu is DialogueBox {isQuestion:false} notice) {
                if(DateTime.UtcNow>treasureDeadline)throw new InvalidOperationException("home_gift_notice_timeout");
                if(DateTime.UtcNow<nextInteraction)return;
                nextInteraction=DateTime.UtcNow.AddMilliseconds(250);
                Current.effects.Add(new{kind="native_home_gift_notice",text=notice.getCurrentString()});
                notice.finishTyping();notice.receiveLeftClick(notice.xPositionOnScreen+16,notice.yPositionOnScreen+16);return;
            }
            TickTreasure();return;
        }
        if(Game1.fadeToBlack||Game1.locationRequest!=null)return;
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("home_supplies_menu_requires_review");
        if(!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(!HasHomeGift()) {
            Current!.effects.Add(new{kind="home_gifts_absent",location=destination});
            Finish("succeeded");return;
        }
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        // Reuse the native treasure controller for adjacency, opening animations,
        // actual inventory deltas and reward verification. No item grants or
        // hard-coded farmhouse coordinates; ordinary storage is never selected.
        origin=destination;
        var gifts=Game1.currentLocation.objects.Pairs
            .Where(p=>p.Value is Chest c&&c.giftbox.Value&&c.GetItemsForPlayer().Any(i=>i!=null))
            .Select(p=>(Tile:p.Key.ToPoint(),Chest:(Chest)p.Value));
        StartTreasure(gifts);
        homeGiftOpening=true;
    }
}
