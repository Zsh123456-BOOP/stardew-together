using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class ModEntry {
    private sealed record EconomyJob(string Epoch,string Location,EconomySnapshot Snapshot,Task<EconomyResult> Task) {public bool Submitted {get;set;}}
    private readonly Dictionary<string,EconomyJob> economyJobs=new();
    private readonly Dictionary<string,SeedQuote> seedQuotes=new();
    private string quoteEpoch="";
    internal object ObserveShop(JsonElement args) {
        var result=PlayerExecutor.ReadShop(args);var menu=(ShopMenu)Game1.activeClickableMenu;
        if(quoteEpoch!=agentSaveEpoch){seedQuotes.Clear();quoteEpoch=agentSaveEpoch;}
        if(menu.currency==0)foreach(var pair in menu.itemPriceAndStock)if(pair.Key is Item {Category:-74,IsRecipe:false} item&&pair.Value.TradeItem==null&&pair.Value.ActionsOnPurchase?.Count is not >0&&pair.Value.Price>=0&&pair.Value.Stock>0&&item.CanBuyItem(Game1.player))
            seedQuotes[menu.ShopId+":"+item.QualifiedItemId]=new(item.QualifiedItemId,menu.ShopId,Game1.currentLocation.NameOrUniqueName,Game1.Date.TotalDays,pair.Value.Price,pair.Value.Stock,item.Stack);
        return result;
    }
    internal object PlanFarmEconomy(JsonElement args) {
        RefreshFacts(true);var l=PlayerExecutor.LoadedLocation(AgentToolRegistry.Text(args,"location","Farm"))??throw new InvalidOperationException("unknown_farm_location");var p=Game1.player;
        if(!(l.IsFarm||l.IsGreenhouse)||l!=Game1.currentLocation&&PlayerExecutor.NextExit(Game1.currentLocation,l.NameOrUniqueName)==null)throw new InvalidOperationException("reachable_farm_or_greenhouse_required");
        int budget=AgentToolRegistry.Number(args,"budget",0),keep=AgentToolRegistry.Number(args,"keep_gold",500),limit=AgentToolRegistry.Number(args,"plots",24),dailyManual=AgentToolRegistry.Number(args,"max_daily_manual_water",24);
        string priority=AgentToolRegistry.Text(args,"priority","income");
        if(budget is <0 or >10000000||keep<0||limit is <1 or >96||dailyManual is <0 or >96||priority is not ("income" or "cashflow" or "collection" or "low_labor"))throw new InvalidOperationException("invalid_economy_limits");
        if(economyJobs.Values.Any(j=>!j.Task.IsCompleted))throw new InvalidOperationException("farm_economy_calculation_already_running");
        var irrigation=l.objects.Values.Where(o=>o.IsSprinkler()).SelectMany(o=>o.GetSprinklerTiles()).Where(v=>l.doesTileHaveProperty((int)v.X,(int)v.Y,"NoSprinklers","Back")!="T").ToHashSet();
        var scares=l.objects.Pairs.Where(o=>o.Value.IsScarecrow()).ToArray();var grid=new List<LayoutCell>();var water=new List<FarmCell>();
        var anchors=PlayerExecutor.Exits(l).Select(e=>new FarmCell(e.X,e.Y)).ToList();
        foreach(var b in l.buildings)if(b.humanDoor.Value.X>=0)anchors.Add(new(b.tileX.Value+b.humanDoor.Value.X,b.tileY.Value+b.humanDoor.Value.Y+1));
        foreach(var o in l.objects.Pairs.Where(o=>o.Value.bigCraftable.Value))if(WorkStand(l,o.Key.ToPoint()) is {} at)anchors.Add(new(at.X,at.Y));
        foreach(var crop in l.terrainFeatures.Pairs.Where(x=>x.Value is HoeDirt {crop:not null}))if(WorkStand(l,crop.Key.ToPoint()) is {} at)anchors.Add(new(at.X,at.Y));
        for(int y=0;y<l.Map.Layers[0].LayerHeight;y++)for(int x=0;x<l.Map.Layers[0].LayerWidth;x++) {
            var at=new FarmCell(x,y);var v=new Vector2(x,y);var feature=l.terrainFeatures.GetValueOrDefault(v);var dirt=feature as HoeDirt;int clearance=PlotClearCost(l,new(x,y));bool pass=PlayerExecutor.Passable(l,new(x,y))||clearance>0;
            if(l.CanRefillWateringCanOnTile(x,y))water.Add(at);
            bool legal=!IsPlacementProtected(l.NameOrUniqueName,new(x,y))&&pass&&(!l.objects.ContainsKey(v)||clearance>0)&&(feature==null||dirt is {crop:null})&&l.doesTileHaveProperty(x,y,"Diggable","Back")!=null&&l.doesTileHaveProperty(x,y,"NoSpawn","Back")!="All"&&l.doesTileHaveProperty(x,y,"TouchAction","Back")==null&&l.doesTileHaveProperty(x,y,"Action","Buildings")==null;
            grid.Add(new(at,legal,pass,dirt?.state.Value==1,irrigation.Contains(v),l.IsGreenhouse||scares.Any(o=>Vector2.Distance(o.Key,v)<o.Value.GetRadiusForScarecrow()),dirt!=null,0,clearance,l.objects.TryGetValue(v,out var equipment)&&equipment.IsSprinkler()));
        }
        ApplyFarmZoning(l,grid,anchors);grid=DistrictGrid(l,grid);
        var distances=FarmLayout.WaterDistances(grid,water);grid=grid.Select(c=>c with{DistanceToWater=distances.GetValueOrDefault(c.Tile,10000)}).ToList();
        var home=l.buildings.FirstOrDefault(b=>b.buildingType.Value=="Farmhouse");
        var start=Game1.currentLocation==l?new FarmCell(p.TilePoint.X,p.TilePoint.Y):home!=null?new(home.tileX.Value+home.humanDoor.Value.X,home.tileY.Value+home.humanDoor.Value.Y+1):anchors.FirstOrDefault(a=>grid.Any(c=>c.Tile==a&&c.Passable));
        var stock=p.Items.Where(i=>i?.Category==-74).Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer().Where(i=>i?.Category==-74))).GroupBy(i=>i!.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
        var quotes=quoteEpoch==agentSaveEpoch?seedQuotes.Values.Where(q=>q.Day==Game1.Date.TotalDays&&q.Units==1).GroupBy(q=>q.Seed).ToDictionary(g=>g.Key,g=>g.OrderBy(q=>q.Price).First()):new Dictionary<string,SeedQuote>();
        var seeds=new List<EconomySeed>();var crops=DataLoader.Crops(Game1.content);
        var paddy=water.SelectMany(w=>Enumerable.Range(-3,7).SelectMany(dx=>Enumerable.Range(-3,7).Select(dy=>new FarmCell(w.X+dx,w.Y+dy)))).ToHashSet();
        foreach(string id in stock.Keys.Union(quotes.Keys)) {
            string raw=id.StartsWith("(O)")?id[3..]:id;if(!crops.TryGetValue(raw,out var data)||!l.SeedsIgnoreSeasonsHere()&&!data.Seasons.Contains(l.GetSeason())||!l.CheckItemPlantRules(raw,false,true,out _))continue;
            var growth=new Dictionary<FarmCell,int>();foreach(var cell in grid.Where(c=>c.Plantable))if(l.CanPlantSeedsHere(raw,cell.Tile.X,cell.Tile.Y,false,out _)) {
                var dirt=l.terrainFeatures.GetValueOrDefault(new Vector2(cell.Tile.X,cell.Tile.Y)) as HoeDirt;
                growth[cell.Tile]=CropGrowth.Stages(data.DaysInPhase,dirt?.GetFertilizerSpeedBoost()??0,p.professions.Contains(5),data.IsPaddyCrop&&paddy.Contains(cell.Tile)).Sum();
            }
            string output=ItemRegistry.QualifyItemId(data.HarvestItemId)!;int owned=Facts.Stock.Where(s=>s.Item==output).Sum(s=>s.Count);
            int reserved=AllReservations().Where(r=>r.Item==output).Sum(r=>r.Count);
            int bundleMissing=Data.Autoplay.Campaign.Enabled?Facts.Bundles.Where(b=>!b.Complete).SelectMany(b=>b.Missing).Where(n=>n.Item==output).Sum(n=>n.Count):0;
            int reserve=Math.Max(0,Math.Max(reserved,bundleMissing)-owned);
            int price=ItemRegistry.Create<StardewValley.Object>(output).sellToStorePrice();
            seeds.Add(new(id,Math.Max(0,stock.GetValueOrDefault(id)-Data.Reservations.GetValueOrDefault(id)),quotes.GetValueOrDefault(id),price,data.RegrowDays>0?data.RegrowDays:-1,CropGrowth.SeasonEnd((int)l.GetSeason(),Game1.dayOfMonth,data.Seasons.Select(x=>(int)x).ToHashSet(),l.SeedsIgnoreSeasonsHere()),data.IsRaised,reserve,!p.basicShipped.ContainsKey(data.HarvestItemId)?1:0,growth,data.IsPaddyCrop?paddy:new()));
        }
        int existingManual=l.terrainFeatures.Pairs.Count(x=>x.Value is HoeDirt {crop:not null} d&&!d.crop.dead.Value&&!d.readyForHarvest()&&!irrigation.Contains(x.Key)&&!(crops.TryGetValue(d.crop.netSeedIndex.Value,out var cropData)&&cropData.IsPaddyCrop&&paddy.Contains(new((int)x.Key.X,(int)x.Key.Y))));
        var processing=CropProcessingLanes(seeds,crops);
        if(Data.Business.Enabled&&priority=="income"&&(Data.Operating.Direction=="cashflow"||p.Money<keep+1000))priority="cashflow";
        var snapshot=new EconomySnapshot(Game1.Date.TotalDays,Game1.dayOfMonth,p.Money,budget,keep,limit,Math.Max(0,dailyManual-existingManual),priority,start,grid,anchors,seeds){EnergyBudget=AvailablePlantingEnergy(),Processing=processing,Carry=SeedCapacity(seeds.Select(s=>s.Seed))};
        string jobId=Guid.NewGuid().ToString("N");economyJobs[jobId]=new(agentSaveEpoch,l.NameOrUniqueName,snapshot,Task.Run(()=>CropPortfolio.Plan(snapshot)));
        foreach(var old in economyJobs.Where(j=>j.Key!=jobId&&j.Value.Task.IsCompleted).Take(Math.Max(0,economyJobs.Count-8)).Select(j=>j.Key).ToArray())economyJobs.Remove(old);
        return new{status="planning",plan_id=jobId,existing_manual_water=existingManual,new_manual_limit=snapshot.ManualLimit,observed_seed_quotes=quotes.Count,next="farm.economy_status 查询；纯快照后台计算，不阻塞角色行动，不购买或播种。"};
    }
    internal object ReadFarmEconomy(JsonElement args) {
        var job=FindEconomyJob(args);if(!job.Task.IsCompleted)return new{status="planning"};
        return new{status="planned",result=job.Task.GetAwaiter().GetResult(),quote_day=job.Snapshot.Day,assumptions="比较利润/周转/资金效率三类可行布局，并以有限宽度多轮补种现金流筛选；不是全局最优。Reinvestment是条件预测，未来报价必须重新读取；执行仅提交今日方案。仅今天观察的金币种子报价及已持有种子。保留目标/献祭产物不算销售收入；按每株每次一份基础品质估计、次日入账，未来行情/天气/额外产量不作保证；Processing仅估计已安装设备容量内的额外加工收益，不计入可用现金。实际执行重新校验位置与供货。"};
    }
    private List<ProcessingLane> CropProcessingLanes(List<EconomySeed> seeds,Dictionary<string,StardewValley.GameData.Crops.CropData> crops) {
        var outputs=seeds.ToDictionary(s=>s.Seed,s=>ItemRegistry.QualifyItemId(crops[s.Seed.StartsWith("(O)")?s.Seed[3..]:s.Seed].HarvestItemId)!);
        var objects=outputs.Values.Distinct().Select(id=>ItemRegistry.Create<StardewValley.Object>(id)).ToArray();
        var options=BusinessProductionChoices(true,objects).ToArray();var lanes=new List<ProcessingLane>();
        // Fuel is allocated conservatively once across machines, not counted once per crop.
        var fuel=options.SelectMany(o=>o.Fuel.Keys).Distinct().ToDictionary(id=>id,id=>Math.Min(AccessibleStock(id),DisposableStock(id)));
        foreach(var machine in GoalMachines()) {
            var choices=options.Where(o=>o.Machine==machine.Object.QualifiedItemId&&o.Location==machine.Location.NameOrUniqueName).ToArray();if(choices.Length==0)continue;
            int batches=28;foreach(var key in choices.SelectMany(c=>c.Fuel.Keys).Distinct()){int cost=choices.Max(c=>c.Fuel.GetValueOrDefault(key));if(cost>0)batches=Math.Min(batches,fuel.GetValueOrDefault(key)/cost);}
            if(batches==0)continue;
            foreach(var key in choices.SelectMany(c=>c.Fuel.Keys).Distinct())fuel[key]-=choices.Max(c=>c.Fuel.GetValueOrDefault(key))*batches;
            string id=machine.Location.NameOrUniqueName+":"+machine.Object.TileLocation;
            foreach(var option in choices)foreach(var seed in outputs.Where(s=>s.Value==option.Input))lanes.Add(new(id,seed.Key,option.Count,Math.Max(1,(int)Math.Ceiling(option.Minutes/1440d)),option.Margin,Game1.dayOfMonth+(int)Math.Ceiling(Math.Max(0,machine.Object.MinutesUntilReady)/1440d),batches));
        }
        return lanes;
    }
    private EconomyJob FindEconomyJob(JsonElement args) {
        if(!economyJobs.TryGetValue(AgentToolRegistry.Text(args,"plan_id"),out var job)||job.Epoch!=agentSaveEpoch||job.Snapshot.Day!=Game1.Date.TotalDays)throw new InvalidOperationException("economy_plan_expired_recalculate");return job;
    }
    private static SeedCarryBudget SeedCapacity(IEnumerable<string> ids) {
        var room=new Dictionary<string,int>();var sizes=new Dictionary<string,int>();
        foreach(var id in ids.Distinct()) {
            var prototype=ItemRegistry.Create(id);sizes[id]=prototype.maximumStackSize();
            room[id]=Game1.player.Items.Where(i=>i!=null&&i.canStackWith(prototype)).Sum(i=>Math.Max(0,i.maximumStackSize()-i.Stack));
        }
        return new(Game1.player.freeSpotsInInventory(),room,sizes);
    }
    internal object ExecuteFarmEconomy(JsonElement args) {
        var job=FindEconomyJob(args);if(!job.Task.IsCompleted)throw new InvalidOperationException("economy_plan_still_computing");var result=job.Task.GetAwaiter().GetResult();
        if(job.Submitted)return new{status="already_submitted"};
        if(result.FirstDayEnergy>AvailablePlantingEnergy())throw new InvalidOperationException("farm_labor_budget_changed_recalculate");
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("close_observed_menu_before_submitting_farm_plan");
        if(Game1.player.Money-result.Spent<job.Snapshot.KeepGold)throw new InvalidOperationException("farm_budget_changed_recalculate");
        if(!SeedCapacity(result.Purchases.Select(b=>b.Seed)).Fits(result.Purchases.ToDictionary(b=>b.Seed,b=>b.Count)))throw new InvalidOperationException("seed_manifest_capacity_changed_recalculate");
        string planId=AgentToolRegistry.Text(args,"plan_id");if(Data.Autoplay.Schedule.Submissions.ContainsKey("farm-"+planId))return new{status="already_submitted"};
        var tasks=new List<AgentTaskSpec>();void Add(string tool,object a,string purpose)=>tasks.Add(new(){id="farm-"+Guid.NewGuid().ToString("N"),tool=tool,args=JsonSerializer.SerializeToElement(a),purpose=purpose,day=job.Snapshot.Day,deadline=2200});
        foreach(var buy in result.Purchases) {
            Add("player.service",new{location=buy.Location,service="shop",shop=buy.Shop},"按已观察报价购买种子");
            Add("player.buy",new{shop=buy.Shop,item=buy.Seed,count=buy.Count,max_unit_price=buy.UnitPrice,budget=buy.Count*buy.UnitPrice,keep_gold=job.Snapshot.KeepGold},"原生采购种子并核验消耗");
        }
        foreach(var group in result.Plants.GroupBy(p=>p.Seed)) {
            var seed=job.Snapshot.Seeds.First(s=>s.Seed==group.Key);int purchased=result.Purchases.Where(b=>b.Seed==group.Key).Sum(b=>b.Count);
            int bag=Game1.player.Items.Where(i=>i?.QualifiedItemId==group.Key).Sum(i=>i.Stack),withdraw=Math.Max(0,group.Count()-bag-purchased);
            if(withdraw>0)Add("work.run",new{goal="withdraw",item=group.Key,count=withdraw},"取回已拥有的种子");
            var plan=new FarmPlantPlan{Epoch=agentSaveEpoch,Day=job.Snapshot.Day,Location=job.Location,Seed=group.Key,AccessOrigin=job.Snapshot.Start,RaisedTiles=result.Plants.Where(p=>job.Snapshot.Seeds.Any(s=>s.Seed==p.Seed&&s.Trellis)).Select(p=>p.Tile).ToList(),PreparationTiles=result.PreparationTiles,CultivationTiles=result.Plants.Select(p=>p.Tile).ToList(),Tiles=group.Select(p=>p.Tile).ToList(),GrowthByTile=group.ToDictionary(p=>p.Tile,p=>p.Growth),LastGrowingDay=seed.LastDay,SalePrice=seed.SalePrice,RegrowDays=seed.Regrow,ManualWatering=group.Count(p=>p.Manual),GrowDays=group.Max(p=>p.Growth)};
            farmPlantPlans[plan.Id]=plan;Add("work.run",new{goal="plant",plan_id=plan.Id},"按预算组合与通道布局播种照料");
        }
        if(tasks.Count==0)return new{status="no_feasible_planting_work",result.StopReason};
        Data.Autoplay.Schedule.Submit("farm-"+planId,Data.Autoplay.Schedule.Revision,tasks,Game1.Date.TotalDays,ordered:true);job.Submitted=true;
        if(!PlayerExecutor.LoadedLocation(job.Location)!.IsGreenhouse)District(PlayerExecutor.LoadedLocation(job.Location)!).Commit(result.PreparationTiles);
        return new{status="queued",tasks=tasks.Select(t=>t.id),result.Spent,note="采购/播种以各阶段原生回执为准。"};
    }
}
