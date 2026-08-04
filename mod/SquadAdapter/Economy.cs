using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Internal;
using StardewValley.Objects;
using TheStardewSquad.Framework.Squad;

namespace TheStardewSquad;
public sealed partial class CompanionControl {
    private sealed class EconomyLedger {
        public int Day {get;set;}=-1;
        public int Spent {get;set;}
        public Dictionary<string,int> Purchased {get;set;}=new();
        public List<string> Entries {get;set;}=new();
    }
    private const string LedgerKey="stardewagent.together/economy";
    private EconomyLedger Ledger() {
        var ledger=Game1.player.modData.TryGetValue(LedgerKey,out var json)?JsonSerializer.Deserialize<EconomyLedger>(json)??new():new EconomyLedger();
        if(ledger.Day!=Game1.Date.TotalDays){ledger.Day=Game1.Date.TotalDays;ledger.Spent=0;}
        return ledger;
    }
    private void StoreLedger(EconomyLedger ledger) {ledger.Entries=ledger.Entries.TakeLast(100).ToList();Game1.player.modData[LedgerKey]=JsonSerializer.Serialize(ledger);}
    private readonly Dictionary<string,ShoppingOrder> orderTargets=new();
    private ShoppingOrder StableOrder(ShoppingOrder order) {
        string key=order.Id+":"+order.Item+":"+order.Shop;
        if(!orderTargets.TryGetValue(key,out var stable))orderTargets[key]=stable=order;
        return stable;
    }
    private static (string Location,string Npc) Merchant(string shop)=>shop switch {
        "SeedShop"=>("SeedShop","Pierre"),"AnimalShop"=>("AnimalShop","Marnie"),"Blacksmith"=>("Blacksmith","Clint"),_=> ("","")
    };
    private Point? ShopCounter(ISquadMate mate,string shop) {
        var merchant=Merchant(shop);var l=mate.Npc.currentLocation;
        if(l.Name!=merchant.Location || l.AreStoresClosedForFestival())return null;
        var owner=Game1.getCharacterFromName(merchant.Npc);if(owner?.currentLocation!=l)return null;
        // The merchant has to be serving at their real map shop action, not merely visiting.
        var layer=l.Map.GetLayer("Buildings");
        for(int y=0;y<layer.LayerHeight;y++)for(int x=0;x<layer.LayerWidth;x++) {
            string action=l.doesTileHaveProperty(x,y,"Action","Buildings")??"";
            bool shopAction=action=="Shop "+shop || (shop=="SeedShop" && action=="SeedShop") || (shop=="AnimalShop" && action=="AnimalShop") || (shop=="Blacksmith" && action=="Blacksmith");
            if(shopAction && Vector2.Distance(owner.Tile,new Vector2(x,y))<=3) {
                var stand=StandingSpot(mate,new Point(x,y));if(stand.HasValue)return stand;
            }
        }
        return null;
    }
    private (Item Item,ItemStockInformation Stock,Dictionary<ISalable,ItemStockInformation> All)? Offer(ShoppingOrder order) {
        if(!DataLoader.Shops(Game1.content).TryGetValue(order.Shop,out var data) || data.Currency!=0)return null;
        var stock=ShopBuilder.GetShopStock(order.Shop,data);
        foreach(var pair in stock)if(pair.Key is StardewValley.Object item && item.QualifiedItemId==order.Item && item.Stack==1 && !item.IsRecipe
            && (item.Category==-74 || item.QualifiedItemId is "(O)178" or "(O)388" or "(O)390" or "(O)378" or "(O)380" or "(O)382" or "(O)384")
            && pair.Value.Stock>0 && pair.Value.Price>=0 && pair.Value.Price<=order.MaxUnitPrice && pair.Value.TradeItem==null && !(pair.Value.ActionsOnPurchase?.Count>0)
            && item.CanBuyItem(Game1.player))return (item,pair.Value,stock);
        return null;
    }
    private IEnumerable<Candidate> EconomyCandidates(ISquadMate mate) {
        if(!farmPolicy.Enabled)yield break;
        var ledger=Ledger();
        foreach(var order in farmPolicy.Shopping.Where(o=>o.Enabled && o.Count>ledger.Purchased.GetValueOrDefault(o.Id)).Take(16)) {
            var stand=ShopCounter(mate,order.Shop);if(!stand.HasValue || Pouch(mate).Count(i=>i!=null)>=12)continue;
            var offer=Offer(order);if(!offer.HasValue)continue;
            int spendable=Math.Min(farmPolicy.DailyBudget-ledger.Spent,Game1.player.Money-farmPolicy.KeepGold);
            if(offer.Value.Stock.Price>spendable)continue;
            var target=StableOrder(order);yield return new(TargetId(target)+":buy","buy",stand.Value,target,stand.Value);
        }
        if(mate.Npc.currentLocation is not Farm farm)yield break;
        if(!farm.buildings.Any(b=>b.buildingType.Value=="Shipping Bin" && b.daysOfConstructionLeft.Value==0))yield break;
        foreach(var chest in farm.objects.Values.OfType<Chest>().Where(c=>Role(c)=="sell")) {
            if(!chest.GetItemsForPlayer(mate.RecruiterUniqueId).Any(i=>i is StardewValley.Object o && o.canBeShipped() && FreeCount(i)>0))continue;
            var stand=StandingSpot(mate,chest.TileLocation.ToPoint());if(!stand.HasValue)continue;
            yield return new(TargetId(chest)+":ship","ship",chest.TileLocation.ToPoint(),chest,stand.Value);
        }
    }
    private bool EconomyPending(Record r) {
        if(!farmPolicy.Enabled)return false;
        if(r.Skill=="buy")return r.Source is ShoppingOrder order && farmPolicy.Shopping.Any(o=>o.Id==order.Id && o.Enabled) && ShopCounter(r.Mate,order.Shop).HasValue;
        return r.Source is Chest chest && Role(chest)=="sell" && r.Location.objects.Values.Contains(chest);
    }
    private void DriveEconomy(Record r,bool slow,Farmer player) {
        var npc=r.Mate.Npc;reservationTick=-1;
        if(r.Skill=="ship" && r.Resources?.PickedUp==true) {
            var farm=(Farm)r.Location;
            var bin=farm.buildings.FirstOrDefault(b=>b.buildingType.Value=="Shipping Bin" && b.daysOfConstructionLeft.Value==0);
            if(bin==null){Finish(r,"failed","shipping_bin_removed");return;}
            var spot=StandingSpot(r.Mate,new Point(bin.tileX.Value,bin.tileY.Value));
            if(!spot.HasValue){Finish(r,"failed","shipping_bin_unreachable");return;}
            if(npc.TilePoint!=spot.Value){mod.FollowerManager.WalkAgent(r.Mate,spot.Value,slow,player);return;}
            r.Mate.Halt();npc.faceGeneralDirection(new Vector2(bin.tileX.Value,bin.tileY.Value)*64);
            r.WorkSeconds+=Game1.currentGameTime.ElapsedGameTime.TotalSeconds;if(r.WorkSeconds<.6)return;
            var item=Pouch(r.Mate).FirstOrDefault(i=>i!=null && i.QualifiedItemId==r.Resources.Input && i.Quality==r.Resources.Quality);
            if(!EconomyPending(r) || item is not StardewValley.Object o || !o.canBeShipped() || FreeCount(item)<r.ShipCount){Finish(r,"failed","sale_permission_or_reservation_changed");return;}
            var copy=item.getOne();copy.Stack=r.ShipCount;farm.getShippingBin(player).Add(copy);Consume(r,item,r.ShipCount);
            farm.lastItemShipped=copy;farm.playSound("Ship");
            var ledger=Ledger();ledger.Entries.Add($"{ledger.Day}:ship:{r.Id}:{copy.QualifiedItemId}:{copy.Stack}:overnight");StoreLedger(ledger);
            r.EffectByActor=true;r.Mate.ActionCooldown=24;return;
        }
        if(npc.TilePoint!=r.Stand){mod.FollowerManager.WalkAgent(r.Mate,r.Stand,slow,player);return;}
        r.Mate.Halt();npc.faceGeneralDirection(r.Target.ToVector2()*64);r.WorkSeconds+=Game1.currentGameTime.ElapsedGameTime.TotalSeconds;if(r.WorkSeconds<.6)return;r.WorkSeconds=0;
        if(!EconomyPending(r)){Finish(r,"failed","shop_or_permission_changed");return;}
        if(r.Skill=="ship") {
            var chest=(Chest)r.Source!;var inventory=chest.GetItemsForPlayer(r.Mate.RecruiterUniqueId);
            var item=inventory.FirstOrDefault(i=>i is StardewValley.Object o && o.canBeShipped() && FreeCount(i)>0);
            if(item==null || Pouch(r.Mate).Count(i=>i!=null)>=12){Finish(r,"failed","sale_cargo_unavailable");return;}
            r.ShipCount=Math.Min(99,FreeCount(item));var copy=item.getOne();copy.Stack=r.ShipCount;Pouch(r.Mate).Add(copy);
            item.Stack-=r.ShipCount;if(item.Stack==0)inventory.Remove(item);
            r.Resources=new(){PickedUp=true,Input=copy.QualifiedItemId,Quality=copy.Quality};r.PickupTile=Tile(npc.TilePoint);return;
        }
        var order=farmPolicy.Shopping.First(o=>o.Id==((ShoppingOrder)r.Source!).Id);
        var offer=Offer(order);var state=Ledger();
        if(!offer.HasValue || state.Purchased.GetValueOrDefault(order.Id)>=order.Count || Pouch(r.Mate).Count(i=>i!=null)>=12){Finish(r,"failed","stock_or_order_changed");return;}
        var value=offer.Value;
        int cost=value.Stock.Price;
        if(cost>farmPolicy.DailyBudget-state.Spent || cost>player.Money-farmPolicy.KeepGold){Finish(r,"failed","budget_exceeded");return;}
        var purchased=value.Item.getOne();purchased.Stack=1;
        // Restrict to plain items with no recipe/unlock/trade actions. Use native stock synchronization
        // and native overnight shipping; never create shop stock or book future sales as cash.
        player.Money-=cost;Pouch(r.Mate).Add(purchased);
        if(value.Stock.Stock!=int.MaxValue)player.team.synchronizedShopStock.OnItemPurchased(order.Shop,value.Item,value.All,1);
        state.Spent+=cost;state.Purchased[order.Id]=state.Purchased.GetValueOrDefault(order.Id)+1;
        state.Entries.Add($"{state.Day}:buy:{r.Id}:{purchased.QualifiedItemId}:1:{cost}");StoreLedger(state);
        r.ResourceChanges[purchased.QualifiedItemId+":"+purchased.Quality]=1;r.ResourceChanges["gold"]=-cost;r.EffectByActor=true;r.Mate.ActionCooldown=24;
    }
}
