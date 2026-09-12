using System.Text.Json;
using StardewValley;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Together;
public sealed partial class ModEntry {
    private void AddBusinessOpportunities(List<OperatingOpportunity> rows) {
        var p=Game1.player;var farm=Game1.getFarm();
        int care=farm.terrainFeatures.Values.OfType<HoeDirt>().Count(d=>d.crop!=null&&!d.crop.dead.Value);
        int slots=Math.Max(0,Data.FarmInvestment.ManualWaterLimit-care);
        bool seasonal=DataLoader.Crops(Game1.content).Values.Any(c=>c.Seasons.Contains(farm.GetSeason())&&c.DaysInPhase.Sum()<=28-Game1.dayOfMonth);
        bool tools=p.Items.Any(i=>i is Hoe)&&p.Items.Any(i=>i is WateringCan);
        if(!Data.Business.Enabled&&seasonal&&tools&&slots>0&&p.Money>Data.Business.KeepGold) {
            int budget=Math.Min(Data.Business.DailyBudget,p.Money-Data.Business.KeepGold);
            rows.Add(new("approve:business","选择是否启用持续经营：先农务与仓储，再现场核价，按原生季节/土地/劳动容量选择种子。此调用明确授权预算，可先查询 farm.business_status 比较", "farm.business",new{enabled=true,expand=false,budget_per_day=budget,keep_gold=Data.Business.KeepGold},$"policy_disabled;native_season={farm.GetSeason()};mature_before_season_end={seasonal};cash={p.Money};additional_care_capacity={slots};tools_owned;budget_ceiling={budget};not_yet_approved",0,1));
        }
        if(!Data.Autoplay.Routine.Enabled&&(Facts.DryCrops>0||Facts.RipeCrops>0||Game1.mailbox.Count>0))
            rows.Add(new("approve:routine","批准跨日照料与读信，不授予支出预算","day.routine",new{enabled=true,assignments=new Dictionary<string,string>{{"water","player"},{"harvest","player"},{"clear_dead","player"},{"mail","player"}}},$"routine_disabled;dry={Facts.DryCrops};ripe={Facts.RipeCrops};mail={Game1.mailbox.Count}",0,1));
        if(Game1.activeClickableMenu is ShopMenu shop) {
            rows.Add(new("inspect:current-shop","读取当前已经打开的商店真实报价，不能把到店当作已经购买","shop.read",new{},$"native_shop={shop.ShopId};stock_entries={shop.itemPriceAndStock.Count};cash={p.Money}",0,1));
        } else if(Game1.activeClickableMenu==null&&Game1.currentLocation.NameOrUniqueName=="SeedShop") {
            var window=ServiceWindow("SeedShop");
            if(window.Reason=="available"&&Game1.timeOfDay>=window.Open&&Game1.timeOfDay<window.Close&&Game1.getCharacterFromName("Pierre")?.currentLocation==Game1.currentLocation)
                rows.Add(new("inspect:seed-counter","已经到店，走近真实柜台打开商店并自动读取报价；此动作不花钱","player.service",new{location="SeedShop",shop="SeedShop",service="shop"},$"native_location;owner_present;window={window.Open}-{window.Close};cash={p.Money}",0,5));
        }
        // Queries only: expose unresolved dependencies without inventing their
        // acquisition requirements or duplicating the encyclopedia database.
        foreach(var g in Data.SharedGoals.Where(g=>g.Status=="active"&&g.Nodes.Any(n=>n.Status is "blocked" or "locked")).Take(2)) {
            string bookId=g.Entity.StartsWith("cook:")?"cooking:"+g.Entity[5..]:g.Entity;
            if(Knowledge.Ready&&Knowledge.Index.Get(bookId) is {} entry&&Knowledge.Visible(entry))rows.Add(new("inspect:goal:"+g.Id,"读取当前目标的配料与取得条件，再选择可执行来源","goal.requirements",new{id=bookId},$"goal={g.Id};entry={bookId};unresolved_nodes={g.Nodes.Count(n=>n.Status is "blocked" or "locked")};visible_native_entry",0,1));
            if(goalRecipes.ContainsKey(g.Entity)||!ItemRegistry.GetDataOrErrorItem(g.Entity).IsErrorItem)rows.Add(new("inspect:dependencies:"+g.Id,"展开已批准目标尚未解决的原生依赖，区分材料和能力缺口","progress.dependencies",new{id=g.Entity,depth=3,limit=40},$"goal={g.Id};registered_native_recipe_or_item={g.Entity};unresolved_nodes_present",0,1));
        }
        if(p.questLog.Any(q=>!q.completed.Value))rows.Add(new("inspect:quests","检查存档已接任务的真实要求和进度","progress.read",new{},$"native_incomplete_quests={p.questLog.Count(q=>!q.completed.Value)}",0,1));
        if(Game1.activeClickableMenu is Billboard or SpecialOrdersBoard)rows.Add(new("inspect:quest-board","当前原生任务板已打开，读取可接任务与期限","quest_board.read",new{},"native_board_open",0,1));
    }
}
