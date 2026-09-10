using System.Reflection;
using System.Text.Json;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private ShopMenu? purchaseMenu;
    private bool? purchaseRecipe;
    private string purchaseId="";
    private string purchaseTrade="";
    private int purchaseTradeBudget,purchaseTradeSpent,purchaseCurrency;
    private int purchaseRemaining,purchasePriceLimit,purchaseKeepGold,purchaseSpent,purchaseBudget;
    private void StartPurchase(JsonElement args) {
        purchaseMenu=Game1.activeClickableMenu as ShopMenu??throw new InvalidOperationException("open_native_shop_first");
        if(purchaseMenu.ShopId!=AgentToolRegistry.Text(args,"shop"))throw new InvalidOperationException("shop_changed_read_again");
        purchaseRecipe=args.TryGetProperty("recipe",out var recipeFlag)?recipeFlag.GetBoolean():null;
        purchaseId=AgentToolRegistry.Text(args,"item");purchaseRemaining=AgentToolRegistry.Number(args,"count",1);
        purchasePriceLimit=AgentToolRegistry.Number(args,"max_unit_price",-1);purchaseKeepGold=AgentToolRegistry.Number(args,"keep_gold",500);
        purchaseBudget=AgentToolRegistry.Number(args,"budget",-1);purchaseSpent=0;
        purchaseTrade=AgentToolRegistry.Text(args,"trade_item","");purchaseTradeBudget=AgentToolRegistry.Number(args,"trade_budget",0);purchaseTradeSpent=0;
        purchaseCurrency=AgentToolRegistry.Number(args,"currency",0);
        if(purchaseCurrency!=0)purchaseKeepGold=AgentToolRegistry.Number(args,"keep_currency",0);
        if(purchaseRemaining is <1 or >999||purchasePriceLimit<0||purchaseKeepGold<0||purchaseBudget<0)throw new InvalidOperationException("explicit_purchase_limits_required");
        if(purchaseMenu.currency!=purchaseCurrency||purchaseCurrency is not (0 or 1 or 2 or 4)||purchaseTradeBudget<0)throw new InvalidOperationException("explicit_matching_purchase_currency_required");
        Current!.phase="shopping";
    }
    private void TickPurchase() {
        if(Current!.phase=="upgrade_confirmation") {
            if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} message) {
                if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(400);message.finishTyping();message.receiveLeftClick(message.xPositionOnScreen+16,message.yPositionOnScreen+16);return;
            }
            if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("upgrade_confirmation_requires_choice");
            Finish("succeeded");return;
        }
        if(purchaseMenu==null||Game1.activeClickableMenu!=purchaseMenu)throw new InvalidOperationException("shop_menu_changed");
        if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(180);
        if(purchaseMenu.heldItem is Item held) {
            purchaseMenu.heldItem=Game1.player.addItemToInventory(held);
            if(purchaseMenu.heldItem!=null)throw new InvalidOperationException("capacity_no_stackable_room");
        }else if(purchaseMenu.heldItem!=null)throw new InvalidOperationException("unknown_shop_held_item");
        if(purchaseRemaining==0){purchaseMenu.exitThisMenu();purchaseMenu=null;Finish("succeeded");return;}
        var item=purchaseMenu.itemPriceAndStock.Keys.FirstOrDefault(i=>i.QualifiedItemId==purchaseId&&(!purchaseRecipe.HasValue||i.IsRecipe==purchaseRecipe.Value))??throw new InvalidOperationException("shop_item_unavailable");
        if(!purchaseMenu.itemPriceAndStock.TryGetValue(item,out var offer)||offer.Stock<1)throw new InvalidOperationException("shop_stock_or_condition_changed");
        // CanBuyItem also rejects a full bag; expose that recoverable cause first.
        if(!item.IsRecipe&&item is Item product&&purchaseMenu.ShopId!="ClintUpgrade"&&!CapacityAdapter.CanReceive(Game1.player,product))throw new InvalidOperationException("capacity_no_stackable_room");
        if(!item.IsRecipe&&!item.CanBuyItem(Game1.player))throw new InvalidOperationException("shop_purchase_condition_changed");
        int currency=ShopMenu.getPlayerCurrencyAmount(Game1.player,purchaseCurrency);
        if(offer.Price<0||offer.Price>purchasePriceLimit||offer.Price>purchaseBudget-purchaseSpent||offer.Price>currency-purchaseKeepGold)throw new InvalidOperationException("purchase_budget_or_price_changed");
        if(offer.ActionsOnPurchase?.Count>0)throw new InvalidOperationException("custom_purchase_actions_require_specific_verifier");
        if(item is not Item prototype)throw new InvalidOperationException("unsupported_salable_type");
        bool upgrade=purchaseMenu.ShopId=="ClintUpgrade"&&item is Tool;
        if((item.IsRecipe||upgrade)&&purchaseRemaining!=1)throw new InvalidOperationException("unlock_or_upgrade_requires_single_purchase");
        if(upgrade&&Game1.player.toolBeingUpgraded.Value!=null)throw new InvalidOperationException("another_tool_upgrade_active");
        var recipeBook=prototype.Category==-7?Game1.player.cookingRecipes:Game1.player.craftingRecipes;
        if(item.IsRecipe&&recipeBook.ContainsKey(prototype.BaseName))throw new InvalidOperationException("recipe_already_known");
        if(!item.IsRecipe&&!upgrade&&!CapacityAdapter.CanReceive(Game1.player,prototype))throw new InvalidOperationException("capacity_no_stackable_room");
        string trade=offer.TradeItem==null?"":ItemRegistry.QualifyItemId(offer.TradeItem)??offer.TradeItem;
        int tradeCount=trade.Length==0?0:offer.TradeItemCount??5;
        if(trade.Length>0&&(trade!=purchaseTrade||tradeCount>purchaseTradeBudget-purchaseTradeSpent||!purchaseMenu.HasTradeItem(trade,tradeCount)))throw new InvalidOperationException("explicit_trade_budget_or_stock_missing");
        if(trade.Length>0&&trade is not ("(O)858" or "(O)73")) {
            // Native ReduceId can span quality stacks. Until allocation is unified,
            // conservatively protect every matching stack from reserved use.
            var spent=Game1.player.Items.Where(i=>i?.QualifiedItemId==trade).ToDictionary(i=>i,i=>Math.Min(i.Stack,tradeCount));
            ValidateConsumption?.Invoke(spent,"","");
        }
        int TradeBalance()=>trade=="(O)858"?Game1.player.QiGems:trade=="(O)73"?Game1.netWorldState.Value.GoldenWalnuts:Game1.player.Items.Where(i=>i?.QualifiedItemId==trade).Sum(i=>i.Stack);
        bool sharedCurrency=trade=="(O)858"&&purchaseCurrency==4;
        if(sharedCurrency&&currency-offer.Price-tradeCount<purchaseKeepGold)throw new InvalidOperationException("combined_currency_and_trade_budget_insufficient");
        int beforeTrade=TradeBalance(),beforeMoney=currency,before=Game1.player.Items.Where(i=>i?.QualifiedItemId==purchaseId).Sum(i=>i.Stack);
        var method=typeof(ShopMenu).GetMethod("tryToPurchaseItem",BindingFlags.Instance|BindingFlags.NonPublic)??throw new InvalidOperationException("native_purchase_handler_changed");
        bool remove=(bool)method.Invoke(purchaseMenu,new object?[]{item,null,1,purchaseMenu.xPositionOnScreen,purchaseMenu.yPositionOnScreen})!;
        if(remove){purchaseMenu.forSale.Remove(item);purchaseMenu.itemPriceAndStock.Remove(item);}
        int inHand=purchaseMenu.heldItem?.QualifiedItemId==purchaseId?purchaseMenu.heldItem.Stack:0;
        int inBag=Game1.player.Items.Where(i=>i?.QualifiedItemId==purchaseId).Sum(i=>i.Stack);
        int paid=beforeMoney-ShopMenu.getPlayerCurrencyAmount(Game1.player,purchaseCurrency);
        bool received=item.IsRecipe?recipeBook.ContainsKey(prototype.BaseName):upgrade?Game1.player.toolBeingUpgraded.Value?.QualifiedItemId==purchaseId&&Game1.player.daysLeftForToolUpgrade.Value>0:inHand+inBag-before>=item.Stack;
        if(paid!=offer.Price+(sharedCurrency?tradeCount:0)||trade.Length>0&&beforeTrade-TradeBalance()!=tradeCount+(sharedCurrency?offer.Price:0)||!received)throw new InvalidOperationException("native_purchase_result_not_verified");
        Current.effects.Add(new{kind=upgrade?"native_tool_upgrade_started":item.IsRecipe?"native_recipe_learned":"native_purchase",shop=purchaseMenu.ShopId,item=purchaseId,recipe=item.IsRecipe?prototype.BaseName:null,units=inHand+inBag-before,currency=purchaseCurrency,cost=offer.Price,trade_item=trade,trade_count=tradeCount,days_remaining=upgrade?Game1.player.daysLeftForToolUpgrade.Value:0});
        purchaseSpent+=offer.Price;purchaseTradeSpent+=tradeCount;purchaseRemaining--;Current.completed++;
        if(upgrade)Current.phase="upgrade_confirmation";
    }
    public static object ReadShop(JsonElement args) {
        if(Game1.activeClickableMenu is not ShopMenu menu)throw new InvalidOperationException("native_shop_not_open");
        int offset=AgentToolRegistry.Number(args,"offset",0),limit=Math.Clamp(AgentToolRegistry.Number(args,"limit",60),1,100);if(offset<0)throw new InvalidOperationException("invalid_shop_offset");
        var all=menu.itemPriceAndStock.Keys.ToArray();
        return new{shop=menu.ShopId,currency=menu.currency,money=Game1.player.Money,held=menu.heldItem is Item item?AgentToolRegistry.ItemInfo(item):null,
            balance=ShopMenu.getPlayerCurrencyAmount(Game1.player,menu.currency),stock=all.Skip(offset).Take(limit).Select(i=>new{item=i.QualifiedItemId,name=i.DisplayName,units_per_purchase=i.Stack,recipe=i.IsRecipe,recipe_name=i.IsRecipe?(i as Item)?.BaseName:null,price=menu.itemPriceAndStock[i].Price,stock=menu.itemPriceAndStock[i].Stock,trade_item=menu.itemPriceAndStock[i].TradeItem is {} t?ItemRegistry.QualifyItemId(t):null,trade_count=menu.itemPriceAndStock[i].TradeItemCount,custom_actions=menu.itemPriceAndStock[i].ActionsOnPurchase,can_buy=i.CanBuyItem(Game1.player)}),offset,total=all.Length,next_offset=offset+limit<all.Length?(int?)(offset+limit):null,
            note="完整原生货品分页，含其它分页标签；count是购买次数，每次可能获得units_per_purchase个。货币/兑换预算必须与报价匹配。"};
    }
}
