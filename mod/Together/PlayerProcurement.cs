using System.Text.Json;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private JsonElement procurementArgs;
    private void StartProcurement(JsonElement args) {
        procurementArgs=args.Clone();
        if(AgentToolRegistry.Text(args,"item").Length==0||AgentToolRegistry.Number(args,"budget",-1)<0||AgentToolRegistry.Number(args,"max_unit_price",-1)<0)throw new InvalidOperationException("procurement_item_and_explicit_budget_required");
        StartService(args);
    }
    private void StartAcquireAnimal(JsonElement args) {
        procurementArgs=args.Clone();StartService(JsonSerializer.SerializeToElement(new{location=AgentToolRegistry.Text(args,"location","AnimalShop"),service="animals"}));
    }
    private void TickAcquireAnimal() {
        if(Current!.phase.StartsWith("animal_"))TickLivestockPurchase();else TickService();
    }
    private void TickProcurement() {
        if(Current!.phase is "shopping" or "upgrade_confirmation")TickPurchase();else TickService();
    }
    internal static IEnumerable<(string Shop,string Location)> ShopSources(string item,bool recipe=false) {
        // Static content identifies potential sellers, not today's stock or price.
        // The real visit and purchase handler still validate conditions and cost.
        var shops=DataLoader.Shops(Game1.content).Where(s=>s.Value.Currency==0&&s.Value.Items.Any(i=>
            (ItemRegistry.QualifyItemId(i.ItemId)??i.ItemId)==item&&i.IsRecipe==recipe&&i.TradeItemId==null&&i.ActionsOnPurchase?.Count is not >0)).Select(s=>s.Key).ToHashSet();
        foreach(var location in Game1.locations) {
            if(location.Map==null)continue;var found=new HashSet<string>();
            for(int y=0;y<location.Map.Layers[0].LayerHeight;y++)for(int x=0;x<location.Map.Layers[0].LayerWidth;x++) {
                var shop=ShopFromAction(location,location.GetTilePropertySplitBySpaces("Action","Buildings",x,y));
                if(shop!=null&&shops.Contains(shop)&&found.Add(shop))yield return(shop,location.NameOrUniqueName);
            }
        }
    }
}
