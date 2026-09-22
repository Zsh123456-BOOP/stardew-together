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
    private string seedSelectionIntent="",selectionDeferralBasis="";
    private string SeedDecisionBasis()=>FailureKnowledge.Hash(AgentJson.Encode(new{day=Game1.Date.TotalDays,Game1.player.Money,crops=Game1.getFarm().terrainFeatures.Values.OfType<StardewValley.TerrainFeatures.HoeDirt>().Count(d=>d.crop!=null),seeds=OwnedSeeds().Sum(i=>i.Stack),tools=AvailableTools<StardewValley.Tools.Hoe>().Count()+AvailableTools<StardewValley.Tools.WateringCan>().Count(),window=ServiceWindow("SeedShop").Reason}));
    private int SeedAllowance()=>AutonomyPolicy.Cash(Game1.player.Money,Data.FarmInvestment.KeepGold,Data.FarmInvestment.BudgetPerDay,NativePurchaseSpent(),PendingPurchaseCash(),UnscheduledDevelopmentCash());
    private object EnrichSeedQuote(object native,ShopMenu menu) {
        var rows=new List<SeedOffer>();var crops=DataLoader.Crops(Game1.content);var farm=Game1.getLocationFromName(Data.FarmInvestment.CropLocation)??Game1.getFarm();int budget=SeedAllowance();
        foreach(var q in seedQuotes.Values.Where(q=>q.Shop==menu.ShopId&&q.Day==Game1.Date.TotalDays&&q.Units==1)) {
            if(!crops.TryGetValue(q.Seed.StartsWith("(O)")?q.Seed[3..]:q.Seed,out var c)||!NativeSeedPlan.Fits(c,farm))continue;
            int growth=NativeSeedPlan.EarliestGrowth(c,farm);
            int last=CropGrowth.SeasonEnd((int)farm.GetSeason(),Game1.dayOfMonth,c.Seasons.Select(x=>(int)x).ToHashSet(),farm.SeedsIgnoreSeasonsHere());
            int harvests=growth<=last-Game1.dayOfMonth?1+(c.RegrowDays>0?(last-Game1.dayOfMonth-growth)/c.RegrowDays:0):0;
            int sale=ItemRegistry.Create<StardewValley.Object>(ItemRegistry.QualifyItemId(c.HarvestItemId)!).sellToStorePrice();
            rows.Add(new(q.Seed,ItemRegistry.GetDataOrErrorItem(q.Seed).DisplayName,q.Price,q.Stock,growth,last,sale,c.RegrowDays,harvests,harvests>1?last-Game1.dayOfMonth:growth,harvests==0?0:Math.Min(q.Stock,q.Price==0?Data.FarmInvestment.Plots:budget/q.Price)));
        }
        string signature=AgentJson.Encode(new{day=Game1.Date.TotalDays,shop=menu.ShopId,rows,budget});
        if(selectionEpoch!=agentSaveEpoch||selectionDay!=Game1.Date.TotalDays||selectionSignature!=signature){selectionQuote=Guid.NewGuid().ToString("N");selectionSignature=signature;selectionApproved=false;selectedSeeds.Clear();}
        selectionEpoch=agentSaveEpoch;selectionDay=Game1.Date.TotalDays;selectionOffers=rows;
        var root=JsonSerializer.SerializeToNode(native)!.AsObject();
        root["seed_decision"]=JsonSerializer.SerializeToNode(new{quote_token=selectionQuote,labor_budget=FarmLaborBudget(),budget,keep_gold=Data.FarmInvestment.KeepGold,candidates=rows,comparison=rows.Select(r=>new{r.Item,r.Name,r.Price,can_buy_now=r.Maximum,mature_on=new{year=(Game1.Date.TotalDays+r.Growth)/112+1,season=new[]{"spring","summer","fall","winter"}[((Game1.Date.TotalDays+r.Growth)/28)%4],day=(Game1.Date.TotalDays+r.Growth)%28+1},r.Growth,r.Harvests,r.Regrow,base_first_harvest_net=r.Sale-r.Price,base_net_per_growth_day=(r.Sale-r.Price)/(double)Math.Max(1,r.Growth),estimated_water_energy=2*r.Water,existing_dry_crops=Facts.DryCrops}),fastest_available=rows.Where(r=>r.Maximum>0).OrderBy(r=>r.Growth).Select(r=>r.Item).FirstOrDefault(),algorithm_recommendation=rows.Where(r=>r.Maximum>0).OrderByDescending(r=>(r.Sale-r.Price)/(double)Math.Max(1,r.Growth)).Select(r=>r.Item).FirstOrDefault(),assumptions="Growth是基础土地或已观察空耕地中的最早成熟时间（含真实肥料/稻田/职业减免）；并非所有地块都能达到，数量及每格成熟由farm.plan再次核验。原生作物数据；基础品质每次一份，Water为预计每株浇水天数，约每次2点基础体力，实际受技能与工具影响；成熟日=当前日+Growth，照料需求是建议而非株数上限，不预言天气/额外产量；报价不是选品授权",next="farm.select_seeds 提交商品ID、数量上限和取舍理由；空items明确选择暂不采购。算法只缩减不可行数量，不替换商品。"});return root;
    }
    private string selectionSignature="";
    internal object SelectSeeds(JsonElement args) {
        if(selectionEpoch!=agentSaveEpoch||selectionDay!=Game1.Date.TotalDays||AgentToolRegistry.Text(args,"quote_token")!=selectionQuote)throw new InvalidOperationException("seed_quote_expired_read_shop_again");
        // Observed quotes authorize selection; there is no secondary business permission.
        if(Data.FarmInvestment.Phase is "planning" or "executing" or "start_planning")throw new InvalidOperationException("seed_selection_not_pending_do_not_replace_active_plan");
        string reason=AgentToolRegistry.Text(args,"reason");if(string.IsNullOrWhiteSpace(reason))throw new InvalidOperationException("seed_selection_reason_required");
        if(!args.TryGetProperty("items",out var items)||items.ValueKind!=JsonValueKind.Array)throw new InvalidOperationException("seed_selection_items_required");
        var requested=new List<SeedRequest>();var seen=new HashSet<string>();
        foreach(var row in items.EnumerateArray()) {
            string id=AgentToolRegistry.Text(row,"item");int count=AgentToolRegistry.Number(row,"count",0);var offer=selectionOffers.FirstOrDefault(o=>o.Item==id);
            if(offer==null||count<1||count>9999||offer.Harvests==0||!seen.Add(id))throw new InvalidOperationException("seed_selection_not_feasible:"+id);
            requested.Add(new(id,count,offer.Price,offer.Stock));
        }
        var allocation=AutonomyPolicy.Seeds(requested,SeedAllowance());
        var chosen=allocation.Where(p=>p.Value>0).ToDictionary(p=>p.Key,p=>p.Value);
        int cost=requested.Sum(r=>allocation[r.Item]*r.Price);
        if(Game1.activeClickableMenu is ShopMenu menu){if(menu.heldItem!=null||!menu.readyToClose())throw new InvalidOperationException("receive_shop_held_item_before_selection");menu.exitThisMenu();}
        selectedSeeds=chosen;selectionApproved=true;selectionDeferralBasis=SeedDecisionBasis();seedSelectionIntent=decisionIntent;Data.FarmInvestment.Enabled=true;
        if(Data.FarmInvestment.Day!=Game1.Date.TotalDays){Data.FarmInvestment.Day=Game1.Date.TotalDays;Data.FarmInvestment.ReservedToday=0;Data.FarmInvestment.Tasks.Clear();}
        Data.FarmInvestment.Error="";Data.FarmInvestment.OwnedSeedsPassDone=true;Data.FarmInvestment.Phase="start_planning";Data.FarmInvestment.OwnedSeedsOnly=chosen.Count==0;
        var result=new{status="selection_approved_not_purchased",quote_token=selectionQuote,candidates=selectionOffers,requested,chosen,adjustments=requested.Where(r=>allocation[r.Item]!=r.Count).Select(r=>new{r.Item,requested=r.Count,accepted=allocation[r.Item],reason="current_cash_or_stock"}),reason,estimated_cost=cost,source="model_explicit_selection"};
        Data.Autoplay.Record("seed_selection",AgentJson.Encode(result));return result;
    }
    private bool HasSeedSelection=>selectionApproved&&selectionEpoch==agentSaveEpoch&&selectionDay==Game1.Date.TotalDays&&(selectedSeeds.Count>0||selectionDeferralBasis==SeedDecisionBasis());
}
