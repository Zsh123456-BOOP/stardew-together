using System.Reflection;
using System.Text.Json;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private ShopMenu? purchaseMenu;
    private string purchaseId="";
    private int purchaseRemaining,purchasePriceLimit,purchaseKeepGold,purchaseSpent,purchaseBudget;
    private void StartPurchase(JsonElement args) {
        purchaseMenu=Game1.activeClickableMenu as ShopMenu??throw new InvalidOperationException("open_native_shop_first");
        if(purchaseMenu.ShopId!=AgentToolRegistry.Text(args,"shop"))throw new InvalidOperationException("shop_changed_read_again");
        purchaseId=AgentToolRegistry.Text(args,"item");purchaseRemaining=AgentToolRegistry.Number(args,"count",1);
        purchasePriceLimit=AgentToolRegistry.Number(args,"max_unit_price",-1);purchaseKeepGold=AgentToolRegistry.Number(args,"keep_gold",500);
        purchaseBudget=AgentToolRegistry.Number(args,"budget",-1);purchaseSpent=0;
        if(purchaseRemaining is <1 or >999||purchasePriceLimit<0||purchaseKeepGold<0||purchaseBudget<0)throw new InvalidOperationException("explicit_purchase_limits_required");
        if(purchaseMenu.currency!=0)throw new InvalidOperationException("non_gold_shop_requires_currency_adapter");
        Current!.phase="shopping";
    }
    private void TickPurchase() {
        if(purchaseMenu==null||Game1.activeClickableMenu!=purchaseMenu)throw new InvalidOperationException("shop_menu_changed");
        if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(180);
        if(purchaseMenu.heldItem is Item held) {
            purchaseMenu.heldItem=Game1.player.addItemToInventory(held);
            if(purchaseMenu.heldItem!=null)throw new InvalidOperationException("purchased_output_needs_inventory_space");
        }else if(purchaseMenu.heldItem!=null)throw new InvalidOperationException("unknown_shop_held_item");
        if(purchaseRemaining==0){purchaseMenu.exitThisMenu();purchaseMenu=null;Finish("succeeded");return;}
        var item=purchaseMenu.forSale.FirstOrDefault(i=>i.QualifiedItemId==purchaseId)??throw new InvalidOperationException("shop_item_unavailable");
        if(!purchaseMenu.itemPriceAndStock.TryGetValue(item,out var offer)||offer.Stock<1||!item.CanBuyItem(Game1.player))throw new InvalidOperationException("shop_stock_or_condition_changed");
        if(offer.Price<0||offer.Price>purchasePriceLimit||offer.Price>purchaseBudget-purchaseSpent||offer.Price>Game1.player.Money-purchaseKeepGold)throw new InvalidOperationException("purchase_budget_or_price_changed");
        // Exchanging goods also consumes reservations; implement it explicitly rather
        // than treating a zero-gold trade as free. Recipe offers have different proof.
        if(offer.TradeItem!=null||item.IsRecipe||offer.ActionsOnPurchase?.Count>0)throw new InvalidOperationException("purchase_requires_trade_or_unlock_adapter");
        if(item is not Item prototype||!Game1.player.couldInventoryAcceptThisItem(prototype))throw new InvalidOperationException("purchase_inventory_space_required");
        int beforeMoney=Game1.player.Money,before=Game1.player.Items.Where(i=>i?.QualifiedItemId==purchaseId).Sum(i=>i.Stack);
        var method=typeof(ShopMenu).GetMethod("tryToPurchaseItem",BindingFlags.Instance|BindingFlags.NonPublic)??throw new InvalidOperationException("native_purchase_handler_changed");
        bool remove=(bool)method.Invoke(purchaseMenu,new object?[]{item,null,1,purchaseMenu.xPositionOnScreen,purchaseMenu.yPositionOnScreen})!;
        if(remove){purchaseMenu.forSale.Remove(item);purchaseMenu.itemPriceAndStock.Remove(item);}
        int inHand=purchaseMenu.heldItem?.QualifiedItemId==purchaseId?purchaseMenu.heldItem.Stack:0;
        int inBag=Game1.player.Items.Where(i=>i?.QualifiedItemId==purchaseId).Sum(i=>i.Stack);
        if(beforeMoney-Game1.player.Money!=offer.Price||inHand+inBag-before<1)throw new InvalidOperationException("native_purchase_result_not_verified");
        Current!.effects.Add(new{kind="native_purchase",shop=purchaseMenu.ShopId,item=purchaseId,units=inHand+inBag-before,cost=beforeMoney-Game1.player.Money});
        purchaseSpent+=beforeMoney-Game1.player.Money;purchaseRemaining--;Current.completed++;
    }
    public static object ReadShop() {
        if(Game1.activeClickableMenu is not ShopMenu menu)throw new InvalidOperationException("native_shop_not_open");
        return new{shop=menu.ShopId,currency=menu.currency,money=Game1.player.Money,held=menu.heldItem is Item item?AgentToolRegistry.ItemInfo(item):null,
            stock=menu.forSale.Take(100).Select(i=>new{item=i.QualifiedItemId,name=i.DisplayName,units_per_purchase=i.Stack,recipe=i.IsRecipe,price=menu.itemPriceAndStock.GetValueOrDefault(i)?.Price,stock=menu.itemPriceAndStock.GetValueOrDefault(i)?.Stock,trade_item=menu.itemPriceAndStock.GetValueOrDefault(i)?.TradeItem,can_buy=i.CanBuyItem(Game1.player)}),truncated=menu.forSale.Count>100,
            note="实际已打开的原生商店；player.buy支持金币普通货品，需数量/单价上限/总预算/保留金。"};
    }
}
