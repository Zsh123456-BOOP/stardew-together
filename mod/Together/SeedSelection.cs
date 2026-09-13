using System.Text.Json;
using System.Text.Json.Nodes;
using StardewValley;
using StardewValley.Menus;
namespace Together;
public sealed partial class ModEntry {
    private sealed record SeedOffer(string Item,string Name,int Price,int Stock,int Growth,int LastDay,int Sale,int Regrow,int Harvests,int Water,int Maximum);
    private string selectionQuote="",selectionEpoch="";
    private int selectionDay=-1;
    private List<SeedOffer> selectionOffers=new();
    private Dictionary<string,int> selectedSeeds=new();
    private bool selectionApproved;
    private int SeedAllowance()=>Math.Max(0,Math.Min(Data.FarmInvestment.BudgetPerDay-Data.FarmInvestment.ReservedToday,Game1.player.Money-Data.FarmInvestment.KeepGold-(Data.Business.Enabled?Data.Operating.DevelopmentCashHeld:0)));
    private object EnrichSeedQuote(object native,ShopMenu menu) {
        var rows=new List<SeedOffer>();var crops=DataLoader.Crops(Game1.content);var farm=Game1.getFarm();int budget=SeedAllowance();
        foreach(var q in seedQuotes.Values.Where(q=>q.Shop==menu.ShopId&&q.Day==Game1.Date.TotalDays&&q.Units==1)) {
            if(!crops.TryGetValue(q.Seed.StartsWith("(O)")?q.Seed[3..]:q.Seed,out var c)||!c.Seasons.Contains(farm.GetSeason()))continue;
            int growth=CropGrowth.Stages(c.DaysInPhase,0,Game1.player.professions.Contains(5),false).Sum();
            int last=CropGrowth.SeasonEnd((int)farm.GetSeason(),Game1.dayOfMonth,c.Seasons.Select(x=>(int)x).ToHashSet(),false);
            int harvests=growth<=last-Game1.dayOfMonth?1+(c.RegrowDays>0?(last-Game1.dayOfMonth-growth)/c.RegrowDays:0):0;
            int sale=ItemRegistry.Create<StardewValley.Object>(ItemRegistry.QualifyItemId(c.HarvestItemId)!).sellToStorePrice();
            rows.Add(new(q.Seed,ItemRegistry.GetDataOrErrorItem(q.Seed).DisplayName,q.Price,q.Stock,growth,last,sale,c.RegrowDays,harvests,harvests>1?last-Game1.dayOfMonth:growth,harvests==0?0:Math.Min(q.Stock,q.Price==0?Data.FarmInvestment.Plots:budget/q.Price)));
        }
        string signature=AgentJson.Encode(new{day=Game1.Date.TotalDays,shop=menu.ShopId,rows,budget});
        if(selectionEpoch!=agentSaveEpoch||selectionDay!=Game1.Date.TotalDays||selectionSignature!=signature){selectionQuote=Guid.NewGuid().ToString("N");selectionSignature=signature;selectionApproved=false;selectedSeeds.Clear();}
        selectionEpoch=agentSaveEpoch;selectionDay=Game1.Date.TotalDays;selectionOffers=rows;
        var root=JsonSerializer.SerializeToNode(native)!.AsObject();
        root["seed_decision"]=JsonSerializer.SerializeToNode(new{quote_token=selectionQuote,budget,keep_gold=Data.FarmInvestment.KeepGold,candidates=rows,algorithm_recommendation=rows.Where(r=>r.Maximum>0).OrderByDescending(r=>(r.Sale-r.Price)/(double)Math.Max(1,r.Growth)).Select(r=>r.Item).FirstOrDefault(),assumptions="原生作物数据；基础品质每次一份，露天每日手动浇水上限，不预言天气/额外产量；报价不是选品授权",next="farm.select_seeds 提交商品ID、数量上限和取舍理由；空items明确选择暂不采购。算法只缩减不可行数量，不替换商品。"});return root;
    }
    private string selectionSignature="";
    internal object SelectSeeds(JsonElement args) {
        if(selectionEpoch!=agentSaveEpoch||selectionDay!=Game1.Date.TotalDays||AgentToolRegistry.Text(args,"quote_token")!=selectionQuote)throw new InvalidOperationException("seed_quote_expired_read_shop_again");
        if(!Data.FarmInvestment.Enabled)throw new InvalidOperationException("seed_selection_requires_approved_investment_policy");
        if(Data.FarmInvestment.Phase!="awaiting_selection")throw new InvalidOperationException("seed_selection_not_pending_do_not_replace_active_plan");
        string reason=AgentToolRegistry.Text(args,"reason");if(string.IsNullOrWhiteSpace(reason))throw new InvalidOperationException("seed_selection_reason_required");
        if(!args.TryGetProperty("items",out var items)||items.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("seed_selection_items_required");
        var chosen=new Dictionary<string,int>();long cost=0;
        foreach(var row in items.EnumerateArray()) {
            string id=AgentToolRegistry.Text(row,"item");int count=AgentToolRegistry.Number(row,"count",0);var offer=selectionOffers.FirstOrDefault(o=>o.Item==id);
            if(offer==null||count<1||count>offer.Maximum||!chosen.TryAdd(id,count))throw new InvalidOperationException("seed_selection_not_feasible:"+id);
            cost+=(long)count*offer.Price;
        }
        if(cost>SeedAllowance())throw new InvalidOperationException("seed_selection_budget_changed");
        if(Game1.activeClickableMenu is ShopMenu menu){if(menu.heldItem!=null||!menu.readyToClose())throw new InvalidOperationException("receive_shop_held_item_before_selection");menu.exitThisMenu();}
        selectedSeeds=chosen;selectionApproved=true;Data.FarmInvestment.Phase="start_planning";Data.FarmInvestment.OwnedSeedsOnly=chosen.Count==0;
        var result=new{status="selection_approved_not_purchased",quote_token=selectionQuote,candidates=selectionOffers,chosen,reason,estimated_cost=cost,source="model_explicit_selection"};
        Data.Autoplay.Record("seed_selection",AgentJson.Encode(result));return result;
    }
    private bool HasSeedSelection=>selectionApproved&&selectionEpoch==agentSaveEpoch&&selectionDay==Game1.Date.TotalDays;
}
