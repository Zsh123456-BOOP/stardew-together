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
    public List<FarmCell> Tiles {get;set;}=new();
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
    internal object PlanFarm(JsonElement args) {
        var l=Game1.currentLocation;
        if(!l.IsFarm&&!l.IsGreenhouse)throw new InvalidOperationException("plan_on_farm_or_greenhouse");
        if(playerExecutor.Busy || WorkActorBusy("player"))throw new InvalidOperationException("wait_for_player_before_layout");
        string requested=AgentToolRegistry.Text(args,"seed");int max=Math.Clamp(AgentToolRegistry.Number(args,"count",24),1,96);
        int manual=Math.Clamp(AgentToolRegistry.Number(args,"max_daily_manual_water",24),0,96);
        bool protectedOnly=args.TryGetProperty("require_scarecrow",out var protect)&&protect.ValueKind==JsonValueKind.True;
        var owned=Game1.player.Items.Where(i=>i?.Category==-74).GroupBy(i=>i!.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
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
            bool passable=PlayerExecutor.Passable(l,new(x,y));
            bool plantable=passable&&!l.objects.ContainsKey(v)&&(feature==null||dirt is {crop:null})&&l.doesTileHaveProperty(x,y,"Diggable","Back")!=null;
            if(l.doesTileHaveProperty(x,y,"NoSpawn","Back")=="All" || l.doesTileHaveProperty(x,y,"Action","Buildings")!=null || l.doesTileHaveProperty(x,y,"TouchAction","Back")!=null)plantable=false;
            bool irrigation=irrigated.Contains(v)&&l.doesTileHaveProperty(x,y,"NoSprinklers","Back")!="T";
            grid.Add(new(new(x,y),plantable,passable,dirt?.state.Value==1,irrigation,l.IsGreenhouse||scares.Any(s=>Vector2.Distance(s.Key,v)<s.Value.GetRadiusForScarecrow()),dirt!=null,0));
        }
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
                growth[c.Tile]=CropGrowth.Stages(data.DaysInPhase,dirt?.GetFertilizerSpeedBoost()??0,p.professions.Contains(5),paddy).Sum();
                return c with{Irrigated=c.Irrigated||paddy,Plantable=c.Plantable&&l.CanPlantSeedsHere(id,c.Tile.X,c.Tile.Y,false,out _)&&Game1.dayOfMonth+growth[c.Tile]<=horizon};
            }).ToList();
            var chosen=FarmLayout.Choose(seedGrid,new(p.TilePoint.X,p.TilePoint.Y),anchors,Math.Min(max,seed.Value),data.IsRaised,manual,protectedOnly);
            if(chosen.Tiles.Count==0)continue;
            int harvests=calc.NumHarvests(Game1.dayOfMonth,horizon);
            var plan=new FarmPlantPlan{Epoch=agentSaveEpoch,Day=Game1.Date.TotalDays,Location=l.NameOrUniqueName,Seed=seed.Key,Tiles=chosen.Tiles,GrowDays=days,Harvests=harvests,ManualWatering=chosen.ManualWatering,Unprotected=chosen.Unprotected,StopReason=chosen.StopReason,GrowthByTile=chosen.Tiles.ToDictionary(t=>t,t=>growth[t]),LastGrowingDay=horizon,SalePrice=(int)calc.sellPrice,RegrowDays=calc.yieldRate};
            farmPlantPlans[plan.Id]=plan;
            double gross=plan.GrowthByTile.Values.Sum(d=>new StardewCropCalculatorLibrary.Crop(seed.Key,d,calc.yieldRate,0,calc.sellPrice).NumHarvests(Game1.dayOfMonth,horizon)*calc.sellPrice);
            bool missing=!p.basicShipped.ContainsKey(data.HarvestItemId)||Facts.Bundles.Any(b=>!b.Complete&&b.Missing.Any(n=>n.Item=="(O)"+data.HarvestItemId));
            double score=priority=="collection"?(missing?100000:0)+gross:priority=="low_labor"?gross/Math.Max(1,plan.ManualWatering*Math.Max(1,horizon-Game1.dayOfMonth)):gross;
            options.Add((score,new{plan_id=plan.Id,plan.Seed,count=plan.Tiles.Count,tiles=plan.Tiles,harvest_day_range=new[]{Game1.dayOfMonth+plan.GrowthByTile.Values.Min(),Game1.dayOfMonth+plan.GrowthByTile.Values.Max()},growing_window_end=horizon,manual_water_per_day=plan.ManualWatering,unprotected_tiles=plan.Unprotected,seed_purchase_cost=0,owned_seeds_only=true,plan.StopReason,forecast=FarmForecast(plan)}));
        }
        foreach(var key in farmPlantPlans.Where(p=>p.Value.Epoch!=agentSaveEpoch||p.Value.Day!=Game1.Date.TotalDays).Select(p=>p.Key).ToArray())farmPlantPlans.Remove(key);
        foreach(var key in farmPlantPlans.Keys.Take(Math.Max(0,farmPlantPlans.Count-128)).ToArray())farmPlantPlans.Remove(key);
        return new{stamp=SnapshotStamp(),priority,options=options.OrderByDescending(o=>o.Score).Take(3).Select(o=>o.Value),evaluated_seeds=options.Count,limitations=new[]{"仅已持有种子；采购现金流/加工收益优化待补","按现有肥料/职业/临水水稻与连续季节计算，假定每天正常照料；未假定未知天气","只用当前合法空地，不拆现有作物/设备/树木；布局不足明确报告","洒水器覆盖是后续日维护估算，播种当天仍检查实际水分"}};
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
        foreach(var tile in plan.Tiles) {
            var v=new Vector2(tile.X,tile.Y);l.terrainFeatures.TryGetValue(v,out var f);var dirt=f as HoeDirt;
            if(l.objects.ContainsKey(v)||f!=null&&dirt==null){StopSemanticWork(j,"plant_plot_occupied_replan");return;}
            if(dirt?.crop is {} existing) {
                if("(O)"+existing.netSeedIndex.Value!=plan.Seed){StopSemanticWork(j,"different_crop_on_planned_tile");return;}
                if(dirt.state.Value==1)continue;
                if(Game1.player.Stamina<j.Reserve+4){if(TryWorkFood(j))return;StopSemanticWork(j,"energy_reserve_reached");return;}
                int slot=WorkSlot(i=>i is WateringCan);if(slot<0){StopSemanticWork(j,"watering_can_missing");return;}
                var can=(WateringCan)Game1.player.Items[slot];
                if(can.WaterLeft==0){RefillWork(j,slot,can);return;}
                WorkChild(j,"player.work",new{skill="water",slot,tiles=new[]{new{x=tile.X,y=tile.Y}}},"plant_water");return;
            }
            if(WorkStand(l,new(tile.X,tile.Y))==null){StopSemanticWork(j,"planned_tile_unreachable");return;}
            if(dirt==null) {
                if(Game1.player.Stamina<j.Reserve+4){if(TryWorkFood(j))return;StopSemanticWork(j,"energy_reserve_reached");return;}
                int slot=WorkSlot(i=>i is Hoe);if(slot<0){StopSemanticWork(j,"hoe_missing");return;}
                WorkChild(j,"player.work",new{skill="till",slot,tiles=new[]{new{x=tile.X,y=tile.Y}}},"plant_till");return;
            }
            int seedSlot=WorkSlot(i=>i.QualifiedItemId==plan.Seed);if(seedSlot<0){StopSemanticWork(j,"planned_seed_supply_missing");return;}
            WorkChild(j,"player.work",new{skill="plant",slot=seedSlot,tiles=new[]{new{x=tile.X,y=tile.Y}}},"plant_seed");return;
        }
        j.completed=plan.Tiles.Count;StopSemanticWork(j,"planned_crops_planted_and_watered",true);
    }
}
