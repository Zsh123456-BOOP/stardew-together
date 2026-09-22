using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Together;
public sealed class FarmPlantPlan {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Epoch {get;set;}="";
    public int Day {get;set;}
    public string Location {get;set;}="";
    public string Seed {get;set;}="";
    public string Fertilizer {get;set;}="";
    public List<FarmCell> Tiles {get;set;}=new();
    public List<FarmCell> PreparationTiles {get;set;}=new();
    public List<FarmCell> CultivationTiles {get;set;}=new();
    public List<FarmCell> RaisedTiles {get;set;}=new();
    public FarmCell AccessOrigin {get;set;}
    public int GrowDays {get;set;}
    public int Harvests {get;set;}
    public int ManualWatering {get;set;}
    public int Unprotected {get;set;}
    public string StopReason {get;set;}="";
    public Dictionary<FarmCell,int> GrowthByTile {get;set;}=new();
    public int LastGrowingDay {get;set;}
    public int SalePrice {get;set;}
    public int RegrowDays {get;set;}
}
public sealed partial class ModEntry {
    private readonly Dictionary<string,FarmPlantPlan> farmPlantPlans=new();
    private bool IsPlacementProtected(string location,Point tile)=>IsPlacementProtected(location,tile,true);
    private bool IsPlacementProtected(string location,Point tile,bool checkClaims) {
        if(location=="Farm"&&Data.Maintenance.Zones.Any(z=>z.Contains(new(tile.X,tile.Y))&&z.Kind is "woodland" or "pasture" or "reserve"))return true;
        if(Game1.getLocationFromName(location) is {} orchard&&PlayerExecutor.ProtectsOrchardGrowth(orchard,tile))return true;
        if(Data.FarmPolicy.Areas.Any(a=>a.Enabled&&a.Location==location&&tile.X>=a.X&&tile.X<a.X+a.Width&&tile.Y>=a.Y&&tile.Y<a.Y+a.Height)||checkClaims&&AgentTileBusy(location,tile.X,tile.Y))return true;
        foreach(var task in Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal&&t.spec.tool=="work.run"&&AgentToolRegistry.Text(t.spec.args,"goal")=="plant")) {
            string id=AgentToolRegistry.Text(task.spec.args,"plan_id");
            if(farmPlantPlans.TryGetValue(id,out var plan)&&plan.Epoch==agentSaveEpoch&&plan.Day==Game1.Date.TotalDays&&plan.Location==location&&plan.PreparationTiles.Concat(plan.Tiles).Contains(new(tile.X,tile.Y)))return true;
        }
        return false;
    }
    internal object PlanFarm(JsonElement args) {
        var l=Game1.currentLocation;
        if(!l.IsGreenhouse&&(!l.IsFarm||!l.IsOutdoors))return new{status="blocked",error="plan_on_farm_or_greenhouse_first_travel_to_Farm",executed=false,prerequisites=new object[]{new{tool="player.travel",args=new{location="Farm"}},new{tool="farm.plan",args=args.Clone()}},note="先完成真实移动，再读取方案；生成plan_id后才能播种。"};
        if(playerExecutor.Busy || WorkActorBusy("player"))throw new InvalidOperationException("wait_for_player_before_layout");
        string requested=AgentToolRegistry.Text(args,"seed");int max=Math.Clamp(AgentToolRegistry.Number(args,"count",24),1,96);
        string fertilizer=AgentToolRegistry.Text(args,"fertilizer");
        if(fertilizer.Length>0) {
            fertilizer=ItemRegistry.QualifyItemId(fertilizer)??throw new InvalidOperationException("invalid_fertilizer_id");
            if(fertilizer is not ("(O)368" or "(O)369" or "(O)919" or "(O)370" or "(O)371" or "(O)920" or "(O)465" or "(O)466" or "(O)918"))throw new InvalidOperationException("crop_fertilizer_required");
            max=Math.Min(max,Game1.player.Items.Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer())).Where(i=>i?.QualifiedItemId==fertilizer).Sum(i=>i.Stack));
            if(max==0)throw new InvalidOperationException("planned_fertilizer_not_carried");
        }
        int manual=AgentToolRegistry.Number(args,"max_daily_manual_water",-1);
        if(manual < -1 || manual > 9999)throw new InvalidOperationException("invalid_manual_care_budget");
        if(manual<0)manual=max;
        bool protectedOnly=args.TryGetProperty("require_scarecrow",out var protect)&&protect.ValueKind==JsonValueKind.True;
        var owned=Game1.player.Items.Concat(SharedStorage().Where(s=>!s.Chest.GetMutex().IsLocked()).SelectMany(s=>s.Chest.GetItemsForPlayer())).Where(i=>i?.Category==-74).GroupBy(i=>i!.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
        var irrigated=l.objects.Values.Where(o=>o.IsSprinkler()).SelectMany(o=>o.GetSprinklerTiles()).ToHashSet();
        var scares=l.objects.Pairs.Where(x=>x.Value.IsScarecrow()).ToArray();
        var anchors=PlayerExecutor.Exits(l).Select(e=>new FarmCell(e.X,e.Y)).ToList();
        foreach(var b in l.buildings)if(b.humanDoor.Value.X>=0)anchors.Add(new(b.tileX.Value+b.humanDoor.Value.X,b.tileY.Value+b.humanDoor.Value.Y+1));
        // Preserve interaction stands for chests, machines, water and existing crops.
        var water=new List<Point>();var grid=new List<LayoutCell>();
        foreach(var pair in l.objects.Pairs)if(pair.Value.bigCraftable.Value){var stand=WorkStand(l,pair.Key.ToPoint());if(stand.HasValue)anchors.Add(new(stand.Value.X,stand.Value.Y));}
        foreach(var pair in l.terrainFeatures.Pairs)if(pair.Value is HoeDirt {crop:not null}){var stand=WorkStand(l,pair.Key.ToPoint());if(stand.HasValue)anchors.Add(new(stand.Value.X,stand.Value.Y));}
        int width=l.Map.Layers[0].LayerWidth,height=l.Map.Layers[0].LayerHeight;
        for(int y=0;y<height;y++)for(int x=0;x<width;x++)if(l.CanRefillWateringCanOnTile(x,y))water.Add(new(x,y));
        foreach(var w in water){var stand=WorkStand(l,w);if(stand.HasValue){anchors.Add(new(stand.Value.X,stand.Value.Y));break;}}
        for(int y=0;y<height;y++)for(int x=0;x<width;x++) {
            var v=new Vector2(x,y);l.terrainFeatures.TryGetValue(v,out var feature);var dirt=feature as HoeDirt;
            int clearance=PlotClearCost(l,new(x,y));
            bool passable=PlayerExecutor.Passable(l,new(x,y))||clearance>0;
            bool plantable=!IsPlacementProtected(l.NameOrUniqueName,new(x,y))&&passable&&(!l.objects.ContainsKey(v)||clearance>0)&&(feature==null||dirt is {crop:null})&&l.doesTileHaveProperty(x,y,"Diggable","Back")!=null;
            if(l.doesTileHaveProperty(x,y,"NoSpawn","Back")=="All" || l.doesTileHaveProperty(x,y,"Action","Buildings")!=null || l.doesTileHaveProperty(x,y,"TouchAction","Back")!=null)plantable=false;
            bool irrigation=irrigated.Contains(v)&&l.doesTileHaveProperty(x,y,"NoSprinklers","Back")!="T";
            grid.Add(new(new(x,y),plantable,passable,dirt?.state.Value==1,irrigation,l.IsGreenhouse||scares.Any(s=>Vector2.Distance(s.Key,v)<s.Value.GetRadiusForScarecrow()),dirt!=null,0,clearance,l.objects.TryGetValue(v,out var equipment)&&equipment.IsSprinkler()));
        }
        var zoning=ApplyFarmZoning(l,grid,anchors);grid=DistrictGrid(l,grid);
        var waterDistance=FarmLayout.WaterDistances(grid,water.Select(w=>new FarmCell(w.X,w.Y)));
        grid=grid.Select(c=>c with{DistanceToWater=waterDistance.GetValueOrDefault(c.Tile,10000)}).ToList();
        var paddyTiles=water.SelectMany(w=>Enumerable.Range(-3,7).SelectMany(dx=>Enumerable.Range(-3,7).Select(dy=>new FarmCell(w.X+dx,w.Y+dy)))).ToHashSet();
        var options=new List<(double Score,object Value)>();var crops=DataLoader.Crops(Game1.content);var p=Game1.player;
        string priority=AgentToolRegistry.Text(args,"priority","income");if(priority is not ("income" or "collection" or "low_labor"))throw new InvalidOperationException("unknown_farm_priority");
        foreach(var seed in owned.Where(s=>requested.Length==0||s.Key==requested).OrderBy(s=>s.Key)) {
            string id=seed.Key.StartsWith("(O)")?seed.Key[3..]:seed.Key;
            if(!crops.TryGetValue(id,out var data))continue;
            bool seasonFree=l.SeedsIgnoreSeasonsHere();if(!seasonFree&&!data.Seasons.Contains(l.GetSeason()))continue;
            if(!l.CheckItemPlantRules(id,false,l.GetData()?.CanPlantHere??l.IsFarm,out _))continue;
            int days=data.DaysInPhase.Sum();int horizon=CropGrowth.SeasonEnd((int)l.GetSeason(),Game1.dayOfMonth,data.Seasons.Select(s=>(int)s).ToHashSet(),seasonFree);
            var calc=new StardewCropCalculatorLibrary.Crop(seed.Key,days,data.RegrowDays>0?data.RegrowDays:-1,0,
                ItemRegistry.Create<StardewValley.Object>("(O)"+data.HarvestItemId).Price);
            var growth=new Dictionary<FarmCell,int>();
            var seedGrid=grid.Select(c=>{
                bool paddy=data.IsPaddyCrop&&paddyTiles.Contains(c.Tile);
                var dirt=l.terrainFeatures.GetValueOrDefault(new Vector2(c.Tile.X,c.Tile.Y)) as HoeDirt;
                float speed=dirt?.HasFertilizer()==true?dirt.GetFertilizerSpeedBoost():fertilizer switch{"(O)465"=>.1f,"(O)466"=>.25f,"(O)918"=>.33f,_=>0};
                growth[c.Tile]=CropGrowth.Stages(data.DaysInPhase,speed,p.professions.Contains(5),paddy).Sum();
                return c with{Irrigated=c.Irrigated||paddy,Plantable=c.Plantable&&l.CanPlantSeedsHere(id,c.Tile.X,c.Tile.Y,false,out _)&&Game1.dayOfMonth+growth[c.Tile]<=horizon};
            }).ToList();
            var chosen=FarmLayout.Choose(seedGrid,new(p.TilePoint.X,p.TilePoint.Y),anchors,Math.Min(max,seed.Value),data.IsRaised,manual,protectedOnly,energyBudget:AvailablePlantingEnergy());
            Data.Autoplay.Record("plot_choice_evidence",AgentJson.Encode(new{seed=seed.Key,requested=max,chosen.Score,chosen.StopReason,selected=chosen.Tiles.Select(t=>seedGrid.First(c=>c.Tile==t)),available_tilled=seedGrid.Where(c=>c.Plantable&&c.Tilled),available_untilled=seedGrid.Count(c=>c.Plantable&&!c.Tilled),score_formula="entry*2+shape_difference*3+sum(planning_penalty+clear_cost*3+unirrigated*25+unprotected*8+untilled*6+water_distance*.2)",selection="maximal_feasible_compact_area_then_score"}));
            if(chosen.Tiles.Count==0)continue;
            int harvests=calc.NumHarvests(Game1.dayOfMonth,horizon);
            var plan=new FarmPlantPlan{Epoch=agentSaveEpoch,Day=Game1.Date.TotalDays,Location=l.NameOrUniqueName,Seed=seed.Key,Tiles=chosen.Tiles,AccessOrigin=new(p.TilePoint.X,p.TilePoint.Y),RaisedTiles=data.IsRaised?chosen.Tiles:new(),GrowDays=days,Harvests=harvests,ManualWatering=chosen.ManualWatering,Unprotected=chosen.Unprotected,StopReason=chosen.StopReason,GrowthByTile=chosen.Tiles.ToDictionary(t=>t,t=>growth[t]),LastGrowingDay=horizon,SalePrice=(int)calc.sellPrice,RegrowDays=calc.yieldRate};
            plan.Fertilizer=fertilizer;farmPlantPlans[plan.Id]=plan;
            double gross=plan.GrowthByTile.Values.Sum(d=>new StardewCropCalculatorLibrary.Crop(seed.Key,d,calc.yieldRate,0,calc.sellPrice).NumHarvests(Game1.dayOfMonth,horizon)*calc.sellPrice);
            bool missing=!p.basicShipped.ContainsKey(data.HarvestItemId)||Facts.Bundles.Any(b=>!b.Complete&&b.Missing.Any(n=>n.Item=="(O)"+data.HarvestItemId));
            double score=priority=="collection"?(missing?100000:0)+gross:priority=="low_labor"?gross/Math.Max(1,plan.ManualWatering*Math.Max(1,horizon-Game1.dayOfMonth)):gross;
            options.Add((score,new{plan_id=plan.Id,next_action=new{tool="work.run",args=new{goal="plant",plan_id=plan.Id}},plan.Seed,plan.Fertilizer,count=plan.Tiles.Count,tiles=plan.Tiles,preparation="clear_entire_bed_then_till_then_plant_then_water",clearance=plan.Tiles.Where(t=>PlotClearCost(l,new(t.X,t.Y))>0),harvest_day_range=new[]{Game1.dayOfMonth+plan.GrowthByTile.Values.Min(),Game1.dayOfMonth+plan.GrowthByTile.Values.Max()},growing_window_end=horizon,manual_water_per_day=plan.ManualWatering,unprotected_tiles=plan.Unprotected,seed_purchase_cost=0,owned_seeds_only=true,plan.StopReason,forecast=FarmForecast(plan)}));
        }
        foreach(var key in farmPlantPlans.Where(p=>p.Value.Epoch!=agentSaveEpoch||p.Value.Day!=Game1.Date.TotalDays).Select(p=>p.Key).ToArray())farmPlantPlans.Remove(key);
        foreach(var key in farmPlantPlans.Keys.Take(Math.Max(0,farmPlantPlans.Count-128)).ToArray())farmPlantPlans.Remove(key);
        return new{stamp=SnapshotStamp(),priority,options=options.OrderByDescending(o=>o.Score).Take(3).Select(o=>o.Value),evaluated_seeds=options.Count,zoning=zoning.GroupBy(z=>z.Value).Select(g=>new{reason=g.Key,reserved_tiles=g.Count()}),limitations=new[]{"仅背包及授权仓库已有种子；入库种子由任务整备取回，采购现金流/加工收益优化待补","按现有肥料/职业/临水水稻与连续季节计算，假定每天正常照料；未假定未知天气","先规划连片田地，清完区域内杂草/树枝/小石头再翻土播种；保留现有作物、树木、设备与通道","洒水器覆盖是后续日维护估算，播种当天仍检查实际水分"}};
    }
    private Dictionary<FarmCell,string> ApplyFarmZoning(GameLocation l,List<LayoutCell> grid,List<FarmCell> anchors) {
        if(l.IsGreenhouse)return new();
        var buildings=l.buildings.Select(b=>new FarmFootprint(b.tileX.Value,b.tileY.Value,b.tilesWide.Value,b.tilesHigh.Value,b.buildingType.Value=="Farmhouse"||b.GetIndoors() is StardewValley.Locations.FarmHouse)).ToArray();
        var home=buildings.FirstOrDefault(b=>b.Home);
        if(home==null)return new();
        var start=grid.Where(c=>c.Passable&&c.Tile.Y>=home.Y+home.Height).OrderBy(c=>Math.Abs(c.Tile.X-(home.X+home.Width/2))+Math.Abs(c.Tile.Y-(home.Y+home.Height))).Select(c=>c.Tile).FirstOrDefault();
        // Crop approach tiles vary with the player and must not redraw permanent roads.
        var structural=PlayerExecutor.Exits(l).Select(e=>new FarmCell(e.X,e.Y)).ToList();
        foreach(var b in l.buildings)if(b.humanDoor.Value.X>=0)structural.Add(new(b.tileX.Value+b.humanDoor.Value.X,b.tileY.Value+b.humanDoor.Value.Y+1));
        var zones=FarmZoning.Reserve(grid,buildings,start,structural);
        for(int i=0;i<grid.Count;i++)if(zones.ContainsKey(grid[i].Tile)||Data.Maintenance.Zones.Any(z=>z.Contains(grid[i].Tile)&&z.Kind is "production" or "woodland" or "pasture" or "reserve"))grid[i]=grid[i] with{Plantable=false,Equipment=false};
        return zones;
    }
    private object FarmForecast(FarmPlantPlan plan) {
        var calendar=new StardewCropCalculatorLibrary.GameStateCalendar(plan.LastGrowingDay,plan.Tiles.Count,Game1.player.Money);
        foreach(var group in plan.GrowthByTile.GroupBy(p=>p.Value)) {
            var crop=new StardewCropCalculatorLibrary.Crop(plan.Seed,group.Key,plan.RegrowDays,0,plan.SalePrice);
            var batch=new StardewCropCalculatorLibrary.PlantBatch(crop,group.Count(),Game1.dayOfMonth,plan.LastGrowingDay);
            for(int day=Game1.dayOfMonth;day<=plan.LastGrowingDay;day++)calendar.GameStates[day].Plants.Add(batch);
            foreach(int harvest in batch.HarvestDays)for(int pay=harvest+1;pay<=plan.LastGrowingDay+1;pay++)calendar.GameStates[pay].Wallet+=batch.Count*plan.SalePrice;
        }
        return new{kind="conditional_base_price_forecast_not_actual_cash",days=calendar.GameStates.Where(s=>s.Key>=Game1.dayOfMonth&&(s.Key==Game1.dayOfMonth||s.Key==plan.LastGrowingDay+1||s.Value.Plants.Any(b=>b.HarvestDays.Contains(s.Key))||s.Value.Wallet!=calendar.GameStates[s.Key-1].Wallet)).Select(s=>new{day=s.Key,projected_gold=s.Value.Wallet,harvest=s.Value.Plants.Where(b=>b.HarvestDays.Contains(s.Key)).Sum(b=>b.Count)}),
            assumptions="已持有种子不重复计购买费用；按每次每株1份基础品质出货、次日到账估算。未扣献祭/加工/自用预留，未预测随机增产/品质/天气；实际支出只允许使用真实余额。"};
    }
    private void TickPlantWork(SemanticJob j) {
        if(!farmPlantPlans.TryGetValue(j.PlanId,out var plan)||plan.Epoch!=agentSaveEpoch||plan.Day!=Game1.Date.TotalDays){StopSemanticWork(j,"plant_plan_expired_replan");return;}
        var l=Game1.currentLocation;
        var preparation=plan.PreparationTiles.Count>0?plan.PreparationTiles:plan.Tiles;
        // An entire bed (including other crops in a portfolio) must be prepared
        // before any seed is consumed. Re-read reality after every native action.
        var blocked=preparation.Where(t=>l.objects.ContainsKey(new(t.X,t.Y))).ToArray();
        if(blocked.Length>0) {
            foreach(var tile in blocked.OrderBy(t=>Vector2.DistanceSquared(new(t.X,t.Y),Game1.player.Tile))) {
                var at=new Point(tile.X,tile.Y);int cost=PlotClearCost(l,at);
                if(cost<=0){StopSemanticWork(j,"planned_plot_has_protected_or_changed_obstacle");return;}
                if(AgentTileBusy(plan.Location,tile.X,tile.Y)||WorkStand(l,at)==null)continue;
                var obj=l.objects[new(tile.X,tile.Y)];int slot=WorkSlot(i=>obj.IsWeeds()?i is Tool t&&t.isScythe():obj.IsTwig()?i is Axe:i is Pickaxe);
                if(slot<0){StopSemanticWork(j,"plot_clearance_tool_missing");return;}
                if(!obj.IsWeeds()&&Game1.player.Stamina<j.Reserve+cost){if(TryWorkFood(j))return;StopSemanticWork(j,"energy_reserve_reached");return;}
                WorkChild(j,"player.work",new{skill="clear",slot,tiles=new[]{new{x=tile.X,y=tile.Y}}},"plant_clear");return;
            }
            StopSemanticWork(j,"plot_clearance_no_reachable_frontier");return;
        }
        foreach(var tile in preparation)if(l.terrainFeatures.TryGetValue(new(tile.X,tile.Y),out var feature)&&feature is not HoeDirt){StopSemanticWork(j,"planned_plot_terrain_changed");return;}
        var cultivation=plan.CultivationTiles.Count>0?plan.CultivationTiles:plan.Tiles;
        var untilled=cultivation.Where(t=>!l.terrainFeatures.ContainsKey(new(t.X,t.Y))).ToList();
        if(untilled.Count>0){PlantBatch(j,"till",WorkSlot(i=>i is Hoe),untilled,"plant_till",4);return;}
        foreach(var tile in plan.Tiles) {
            var dirt=(HoeDirt)l.terrainFeatures[new(tile.X,tile.Y)];
            if(dirt.crop is {} crop&&"(O)"+crop.netSeedIndex.Value!=plan.Seed){StopSemanticWork(j,"different_crop_on_planned_tile");return;}
        }
        var empty=plan.Tiles.Where(t=>((HoeDirt)l.terrainFeatures[new(t.X,t.Y)]).crop==null).ToList();
        if(plan.Fertilizer.Length>0) {
            var unfertilized=empty.Where(t=>!((HoeDirt)l.terrainFeatures[new(t.X,t.Y)]).HasFertilizer()).ToList();
            if(unfertilized.Count>0){PlantBatch(j,"fertilize",WorkSlot(i=>i.QualifiedItemId==plan.Fertilizer),unfertilized,"plant_fertilizer",0);return;}
        }
        if(empty.Count>0) {
            string seedId=plan.Seed.StartsWith("(O)")?plan.Seed[3..]:plan.Seed;
            if(!DataLoader.Crops(Game1.content).TryGetValue(seedId,out var data))throw new InvalidOperationException("planned_crop_definition_changed");
            foreach(var tile in empty) {
                var dirt=(HoeDirt)l.terrainFeatures[new(tile.X,tile.Y)];
                bool paddy=data.IsPaddyCrop&&Enumerable.Range(-3,7).Any(dx=>Enumerable.Range(-3,7).Any(dy=>l.CanRefillWateringCanOnTile(tile.X+dx,tile.Y+dy)));
                int days=CropGrowth.Stages(data.DaysInPhase,dirt.GetFertilizerSpeedBoost(),Game1.player.professions.Contains(5),paddy).Sum();
                int last=CropGrowth.SeasonEnd((int)l.GetSeason(),Game1.dayOfMonth,data.Seasons.Select(s=>(int)s).ToHashSet(),l.SeedsIgnoreSeasonsHere());
                if(Game1.dayOfMonth+days>last)throw new InvalidOperationException("crop_would_miss_actual_season_replan");
            }
            PlantBatch(j,"plant",WorkSlot(i=>i.QualifiedItemId==plan.Seed),empty,"plant_seed",0);return;
        }
        var dry=plan.Tiles.Where(t=>((HoeDirt)l.terrainFeatures[new(t.X,t.Y)]).state.Value!=1).ToList();
        if(dry.Count>0) {
            int slot=WorkSlot(i=>i is WateringCan);if(slot<0){StopSemanticWork(j,"watering_can_missing");return;}
            var can=(WateringCan)Game1.player.Items[slot];if(can.WaterLeft==0){RefillWork(j,slot,can);return;}
            PlantBatch(j,"water",slot,dry.Take(can.WaterLeft).ToList(),"plant_water",4);return;
        }
        j.completed=plan.Tiles.Count;StopSemanticWork(j,"planned_crops_planted_and_watered",true);
    }
    private void PlantBatch(SemanticJob j,string skill,int slot,List<FarmCell> tiles,string phase,int energy) {
        if(slot<0){StopSemanticWork(j,"plant_"+skill+"_supply_missing");return;}
        int count=Math.Min(8,tiles.Count);
        if(energy>0)count=Math.Min(count,Math.Max(0,(int)(Game1.player.Stamina-j.Reserve)/energy));
        else count=Math.Min(count,Game1.player.Items[slot].Stack);
        if(count<=0){if(TryWorkFood(j))return;StopSemanticWork(j,"energy_reserve_reached");return;}
        var batch=tiles.Take(count).ToArray();
        if(skill=="plant"&&farmPlantPlans.TryGetValue(j.PlanId,out var plan)&&plan.RaisedTiles.Count>0) {
            // Stand on the permanently connected aisle, not on a soon-to-be
            // enclosed pocket beside a trellis. Use the same final collision map.
            var location=Game1.currentLocation;var grid=new List<LayoutCell>();
            for(int y=0;y<location.Map.Layers[0].LayerHeight;y++)for(int x=0;x<location.Map.Layers[0].LayerWidth;x++)grid.Add(new(new(x,y),false,PlayerExecutor.Passable(location,new(x,y)),false,false,false,false,0));
            var reachable=FarmLayout.ReachableAfter(grid,plan.AccessOrigin,plan.RaisedTiles);
            var stands=new List<Point>();
            foreach(var t in batch) {
                var candidates=new[]{new Point(t.X-1,t.Y),new Point(t.X+1,t.Y),new Point(t.X,t.Y-1),new Point(t.X,t.Y+1)}
                    .Where(p=>reachable.Contains(new(p.X,p.Y))).OrderBy(p=>Vector2.DistanceSquared(p.ToVector2(),Game1.player.Tile));
                Point? stand=candidates.Cast<Point?>().FirstOrDefault(p=>p!.Value==Game1.player.TilePoint||PlayerExecutor.PreviewPath(location,p.Value)?.Count>0);
                if(stand==null){StopSemanticWork(j,"trellis_safe_aisle_unreachable_replan");return;}stands.Add(stand.Value);
            }
            WorkChild(j,"player.work",new{skill,slot,tiles=batch.Select(t=>new{x=t.X,y=t.Y}).ToArray(),stands=stands.Select(p=>new{x=p.X,y=p.Y}).ToArray()},phase);return;
        }
        WorkChild(j,"player.work",new{skill,slot,tiles=batch.Select(t=>new{x=t.X,y=t.Y}).ToArray()},phase);
    }
    private static int PlotClearCost(GameLocation location,Point tile) {
        var v=tile.ToVector2();
        if(!location.objects.TryGetValue(v,out var obj)||obj.bigCraftable.Value||obj.questItem.Value||obj.HasBeenInInventory||!(obj.IsWeeds()||obj.IsTwig()||obj.BaseName=="Stone"))return 0;
        if(location.terrainFeatures.TryGetValue(v,out var feature)&&feature is not HoeDirt {crop:null})return 0;
        if(!location.isTilePassable(v)||location.buildings.Any(b=>b.occupiesTile(v))||location.resourceClumps.Any(c=>c.occupiesTile(tile.X,tile.Y)))return 0;
        return obj.IsWeeds()?1:Math.Max(4,obj.MinutesUntilReady*2+2);
    }
}
