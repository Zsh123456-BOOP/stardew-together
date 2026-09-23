using System.Text.Json;
using StardewValley;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Together;
public sealed partial class ModEntry {
    private IEnumerable<Item> OwnedSeeds()=>Game1.player.Items.Concat(SharedStorage().Where(s=>!s.Chest.GetMutex().IsLocked()).SelectMany(s=>s.Chest.GetItemsForPlayer())).Where(i=>i?.Category==-74);
    private object PlantingExecutionFacts()=>new {
        unplanted_owned_seeds=OwnedSeeds().GroupBy(i=>i.QualifiedItemId).Select(g=>new{item=g.Key,name=g.First().DisplayName,count=g.Sum(i=>i.Stack)}).ToArray(),
        actual_farm_crops=Game1.getFarm().terrainFeatures.Values.OfType<HoeDirt>().Where(d=>d.crop!=null&&!d.crop.dead.Value).GroupBy(d=>d.crop.netSeedIndex.Value).Select(g=>new{seed=g.Key,seed_name=ItemRegistry.GetDataOrErrorItem(ItemRegistry.QualifyItemId(g.Key)??g.Key).DisplayName,planted=g.Count(),watered=g.Count(d=>d.state.Value==1),ripe=g.Count(d=>d.readyForHarvest())}).ToArray(),
        queued_planting=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal&&t.spec.tool=="work.run"&&AgentToolRegistry.Text(t.spec.args,"goal")=="plant").Select(t=>new{t.spec.id,t.state,t.spec.args}).ToArray(),
        evidence="原生种子库存与地里作物分开计数；采购或口头计划不算播种完成。未种种子由模型安排，不自动派工。"
    };
    private void AddBusinessOpportunities(List<OperatingOpportunity> rows) {
        var p=Game1.player;var farm=Game1.getLocationFromName(Data.FarmInvestment.CropLocation)??Game1.getFarm();
        int care=farm.terrainFeatures.Values.OfType<HoeDirt>().Count(d=>d.crop!=null&&!d.crop.dead.Value);
        if(Game1.activeClickableMenu==null&&!OpportunityPlayerOccupied())foreach(var seed in OwnedSeeds().GroupBy(i=>i.QualifiedItemId).Take(4)) {
            var possible=NativeSeedPlan.Options(seed.Key,farm);if(possible.Count==0||possible.Values.Any(c=>!NativeSeedPlan.Fits(c,farm)))continue;
            int count=seed.Sum(i=>i.Stack);bool atFarm=Game1.currentLocation==farm;
            var ready=farmPlantPlans.Values.LastOrDefault(plan=>plan.Epoch==agentSaveEpoch&&plan.Day==Game1.Date.TotalDays&&plan.Location==farm.NameOrUniqueName&&plan.Seed==seed.Key&&plan.Tiles.Any(t=>!farm.terrainFeatures.TryGetValue(new(t.X,t.Y),out var f)||f is HoeDirt {crop:null}));
            // A failed child is a recovery decision, never a reason to erase
            // owned inventory or blindly replay the same blocked plan.
            if(ready!=null&&Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.tool=="work.run"&&AgentToolRegistry.Text(t.spec.args,"plan_id")==ready.Id&&t.state is "failed" or "partial" or "blocked" or "cancelled" or "needs_review"))ready=null;
            if(ready!=null) {
                rows.Add(new("plant:owned-seeds:"+seed.Key,"执行已生成的田块方案，消费现有种子；完成后可释放整叠种子占格","work.run",new{goal="plant",plan_id=ready.Id},$"native_owned_seeds={seed.Sum(i=>i.Stack)};plan={ready.Id};tiles={ready.Tiles.Count};not_yet_planted",0,30));continue;
            }

            rows.Add(new("plan:owned-seeds:"+seed.Key,"已有未种种子：可选择规划播种，也可保留；购买完成不等于种植完成",atFarm?"farm.plan":"player.travel",atFarm?(object)new{seed=seed.Key,count=Math.Min(96,count)}:new{location=farm.NameOrUniqueName},$"native_unplanted_seed={seed.Key};owned={count};actual_existing_crops={care};tools_derived_from_selected_tiles;next=farm.plan_then_work.run_plant;layout_and_labor_not_yet_evaluated",0,atFarm?1:30));
        }
        var land=ExpansionLand();bool seasonal=DataLoader.Crops(Game1.content).Values.Any(c=>NativeSeedPlan.Fits(c,farm));
        var window=ServiceWindow("SeedShop");
        if(seasonal&&land.Count>0&&SeedAllowance()>0&&Game1.activeClickableMenu==null&&Game1.currentLocation.NameOrUniqueName!="SeedShop"&&window.Reason=="available"&&Game1.timeOfDay>=window.Open&&Game1.timeOfDay<window.Close&&Data.FarmInvestment.Phase is not ("executing" or "planning" or "observing_shop"))
            rows.Add(new("inspect:seed-offers","可去种子店核价比较是否扩种；已有作物照料负担供决策，无固定株数上限", "player.service",new{location="SeedShop",shop="SeedShop",service="shop"},$"native_season={farm.GetSeason()};cash_available={SeedAllowance()};existing_crops={care};open={window.Open}-{window.Close}",0,30,new{land=new{observed_empty_diggable=land.Count,note="初步空地观察；连续田块、道路和每日劳动仍以farm.plan核验"},quotes=ObservedSeedBasis(),cost_scope="询价/采购耗时与种植劳动分开；可先买后种；未知报价不表示收益为零",next="核对报价后由模型选品；价格未知不推测"}));
        if(!Data.Autoplay.Routine.Enabled&&(Facts.DryCrops>0||Facts.RipeCrops>0||Game1.mailbox.Count>0))
            rows.Add(new("configure:routine","可建立或调整跨日照料例行，不影响直接使用工具","day.routine",new{enabled=true,assignments=new Dictionary<string,string>{{"water","player"},{"harvest","player"},{"clear_dead","player"},{"mail","player"}}},$"routine_disabled;dry={Facts.DryCrops};ripe={Facts.RipeCrops};mail={Game1.mailbox.Count}",0,1));
        if(Game1.activeClickableMenu is ShopMenu shop) {
            rows.Add(new("inspect:current-shop","读取当前已经打开的商店真实报价，不能把到店当作已经购买","shop.read",new{},$"native_shop={shop.ShopId};stock_entries={shop.itemPriceAndStock.Count};cash={p.Money}",0,1));
        } else if(Game1.activeClickableMenu==null&&Game1.currentLocation.NameOrUniqueName=="SeedShop") {
            var counterWindow=ServiceWindow("SeedShop");
            if(counterWindow.Reason=="available"&&Game1.timeOfDay>=counterWindow.Open&&Game1.timeOfDay<counterWindow.Close&&Game1.getCharacterFromName("Pierre")?.currentLocation==Game1.currentLocation)
                rows.Add(new("inspect:seed-counter","已经到店，走近真实柜台打开商店并自动读取报价；此动作不花钱","player.service",new{location="SeedShop",shop="SeedShop",service="shop"},$"native_location;owner_present;window={window.Open}-{window.Close};cash={p.Money}",0,5));
        }
        if(Data.FarmInvestment.Phase is not ("planning" or "executing" or "start_planning")&&selectionDay==Game1.Date.TotalDays&&selectionEpoch==agentSaveEpoch&&!HasSeedSelection)
            rows.Add(new("select:seeds","根据真实报价、生长期和劳动需求选择种子与数量；不是采购完成","farm.select_seeds",new{quote_token=selectionQuote,items=Array.Empty<object>(),reason="由模型比较本轮报价后填写选品；空数组表示暂不采购"},"current_day_native_quotes;current_cash;selection_pending",0,1,new{quote_token=selectionQuote,candidates=selectionOffers,budget=SeedAllowance(),next="按真实报价用farm.select_seeds选择items和reason"}));
        // Queries only: expose unresolved dependencies without inventing their
        // acquisition requirements or duplicating the encyclopedia database.
        foreach(var g in Data.SharedGoals.Where(g=>g.Status=="active"&&g.Nodes.Any(n=>n.Status is "blocked" or "locked")).Take(2)) {
            string bookId=g.Entity.StartsWith("cook:")?"cooking:"+g.Entity[5..]:g.Entity;
            if(Knowledge.Ready&&Knowledge.Index.Get(bookId) is {} entry&&Knowledge.Visible(entry))rows.Add(new("inspect:goal:"+g.Id,"读取当前目标的配料与取得条件，再选择可执行来源","goal.requirements",new{id=bookId},$"goal={g.Id};entry={bookId};unresolved_nodes={g.Nodes.Count(n=>n.Status is "blocked" or "locked")};visible_native_entry",0,1));
            if(goalRecipes.ContainsKey(g.Entity)||!ItemRegistry.GetDataOrErrorItem(g.Entity).IsErrorItem)rows.Add(new("inspect:dependencies:"+g.Id,"展开已批准目标尚未解决的原生依赖，区分材料和能力缺口","progress.dependencies",new{id=g.Entity,depth=3,limit=40},$"goal={g.Id};registered_native_recipe_or_item={g.Entity};unresolved_nodes_present",0,1));
        }
        // Current quest progress is already present in progression; detailed progress.read remains discoverable.
        if(Game1.activeClickableMenu is Billboard or SpecialOrdersBoard)rows.Add(new("inspect:quest-board","当前原生任务板已打开，读取可接任务与期限","quest_board.read",new{},"native_board_open",0,1));
    }
}
