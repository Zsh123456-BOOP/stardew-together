using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Together;
public sealed record DayOption(string skill,int slot,int x,int y,int stand_x,int stand_y,string item,string purpose,int route_tiles,int estimated_minutes,float estimated_energy,bool useful,bool fits);
public sealed partial class ModEntry {
    private object AgentDay(bool compact=false) {
        RefreshFacts(true);Data.Autoplay.Agenda.EnterDay(Game1.Date.TotalDays);
        var options=compact?new List<DayOption>():DayOptions();
        var home=Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName;
        return new {
            day=Game1.Date.TotalDays,time=Game1.timeOfDay,location=Game1.currentLocation.NameOrUniqueName,
            policy="正常时间；农务完成后选择有价值的下一批工作，不能只因种完就睡。程序估算时间与体力，模型决定优先级和分工。",
            budget=new{stamina=Game1.player.Stamina,energy_reserve=DailyBudget.EnergyReserve,work_minutes=DailyBudget.WorkMinutes(Game1.timeOfDay,ReturnReserve()),return_reserve_minutes=ReturnReserve(),return_by=2500,home,estimate_note="路线格数来自当前地图原生寻路；耗时按移动速度加动作余量估算，跨图返家预留为保守预算，非到达保证"},
            planted_crops=Game1.getFarm().terrainFeatures.Values.OfType<HoeDirt>().Where(d=>d.crop!=null&&!d.crop.dead.Value).GroupBy(d=>"(O)"+d.crop!.indexOfHarvest.Value).Select(g=>new{item=g.Key,name=ItemRegistry.GetDataOrErrorItem(g.Key).DisplayName,count=g.Count(),ripe=g.Count(d=>d.readyForHarvest())}),
            chores=new{Facts.DryCrops,Facts.RipeCrops,Facts.AnimalsUnpetted,Facts.FeedNeeded,Facts.MachinesReady},
            routine=Data.Autoplay.Routine,farm_investment=InvestmentObservation(),priorities=Data.Autoplay.Agenda.Priorities,resource_targets=Data.Autoplay.Agenda.Resources.Select(r=>new{r.Item,r.Count,r.Purpose,owned=Facts.Stock.Where(s=>s.Item==r.Item).Sum(s=>s.Count),missing=Math.Max(0,r.Count-Facts.Stock.Where(s=>s.Item==r.Item).Sum(s=>s.Count))}),
            shared_goals=GoalContext(),quests=Facts.Quests.Take(8),
            watering=compact?(object)new{automatic="work.run water/refill自动定位水源并补水"}:AgentWatering(),options=compact?(object)new{details_available_via="day.read",not_enumerated=true,note="常驻决策不对每一格重复寻路；使用经营候选，高层工作在执行时核验路径"}:options,resource_policy="材料优先满足day.plan目标库存和共同心愿缺口；统一经营账本根据已批准项目计算储备，不采用另一套固定数量。useful=false表示当前没有已声明用途，不要求清空整个农场。",options_scope="仅当前地图最近一批已核验路径的农活、石块、树枝和采集物，非全世界；空列表不能证明没有可做的事，换地点、查百科/任务、整理与补给也要考虑。",
            next_review="每批完成、换地图、换日或失败后刷新；不要按每个格子调用模型。伙伴可通过world.read并行派工；出货前先核对材料预留。",
            today_completed_batches=Data.Autoplay.Agenda.CompletedBatches,recent_days=Data.Autoplay.Agenda.History.TakeLast(3)
        };
    }
    private object AgentWatering() {
        int slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is WateringCan,-1);
        if(slot<0)return new{available=false};
        var can=(WateringCan)Game1.player.Items[slot];var l=Game1.currentLocation;var sources=new List<Point>();var options=new List<object>();
        if(can.WaterLeft==0) {
            for(int y=0;y<l.Map.Layers[0].LayerHeight;y++)for(int x=0;x<l.Map.Layers[0].LayerWidth;x++)
                if(l.CanRefillWateringCanOnTile(x,y))sources.Add(new(x,y));
            foreach(var tile in sources.OrderBy(p=>Vector2.DistanceSquared(p.ToVector2(),Game1.player.Tile)).Take(64)) {
                foreach(var at in new[]{new Point(tile.X,tile.Y+1),new Point(tile.X-1,tile.Y),new Point(tile.X+1,tile.Y),new Point(tile.X,tile.Y-1)}) {
                    if(!PlayerExecutor.Passable(l,at))continue;
                    var path=PlayerExecutor.PreviewPath(l,at);
                    if(at!=Game1.player.TilePoint && path?.Count is not >0)continue;
                    options.Add(new{location=l.NameOrUniqueName,move=new{x=at.X,y=at.Y},use_tool=new{slot,x=tile.X,y=tile.Y},route_tiles=path?.Count??0});break;
                }
                if(options.Count==3)break;
            }
        }
        return new{available=true,slot,water_left=can.WaterLeft,refill_options=options,instruction="空水壶可先 player.move 到 move，再 player.use_tool 到水源，核验 water_left 增加；也可把浇水交给伙伴。"};
    }
    private int ReturnReserve()=>Game1.currentLocation==Utility.getHomeOfFarmer(Game1.player)?20:Game1.currentLocation is StardewValley.Farm?45:90;
    private List<DayOption> DayOptions() {
        var l=Game1.currentLocation;var p=Game1.player;var candidates=new List<(Point Tile,string Skill,int Slot,string Item,string Purpose,float Energy)>();
        int Slot(Type type)=>Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]!=null && type.IsInstanceOfType(p.Items[i]),-1);
        int axe=Slot(typeof(Axe)),pick=Slot(typeof(Pickaxe)),can=Slot(typeof(WateringCan));
        int scythe=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i] is Tool t&&t.isScythe(),-1);
        foreach(var pair in l.terrainFeatures.Pairs)if(pair.Value is HoeDirt dirt && dirt.crop!=null) {
            if(dirt.crop.dead.Value){if(scythe>=0)candidates.Add((pair.Key.ToPoint(),"clear_dead",scythe,"","清理枯苗，腾出可用耕地",0));continue;}
            if(dirt.readyForHarvest())candidates.Add((pair.Key.ToPoint(),"harvest",-1,"(O)"+dirt.crop.indexOfHarvest.Value,"收获成熟作物",0));
            else if(dirt.state.Value!=1 && can>=0 && ((WateringCan)p.Items[can]).WaterLeft>0)candidates.Add((pair.Key.ToPoint(),"water",can,"","今日照料",4));
        }
        foreach(var pair in l.objects.Pairs) {
            var o=pair.Value;var tile=pair.Key.ToPoint();
            if(o.IsWeeds() && scythe>=0)candidates.Add((tile,"clear",scythe,"(O)771","清理杂草，回收纤维并整理农场空间",0));
            else if(o.IsTwig() && axe>=0)candidates.Add((tile,"clear",axe,"(O)388","收集木材用于建设与制作",4));
            else if(o.BaseName=="Stone" && pick>=0)candidates.Add((tile,"clear",pick,"(O)390","收集石料；产物以实际掉落为准",Math.Max(4,o.MinutesUntilReady*2+2)));
            else if(o.isForage() && !o.bigCraftable.Value)candidates.Add((tile,"forage",-1,o.QualifiedItemId,"拾取季节采集物，留用/补给/出货",0));
        }
        var stockCache=new Dictionary<string,int>();
        bool Useful(string skill,string item) {
            if(skill!="clear")return true;
            int owned=stockCache.TryGetValue(item,out var stock)?stock:stockCache[item]=TeamStock(item);
            if(Data.Autoplay.Agenda.Resources.Any(r=>r.Item==item && r.Count>owned))return true;
            if(Data.SharedGoals.Any(g=>g.Status=="active" && g.Nodes.Any(n=>n.Item==item&&n.ToPrepare>0)))return true;
            return owned<Data.Operating.MaterialTargets.GetValueOrDefault(item);
        }
        var result=new List<DayOption>();
        bool inventoryRoom=CapacityAdapter.HasSlots(p,1);
        foreach(var c in candidates.OrderBy(c=>c.Skill is "water" or "harvest"?0:Useful(c.Skill,c.Item)?1:2).ThenBy(c=>Vector2.DistanceSquared(c.Tile.ToVector2(),p.Tile)).Take(24)) {
            if(AgentTileBusy(l.NameOrUniqueName,c.Tile.X,c.Tile.Y)||c.Skill=="clear"&&MaintenanceProtects(l,c.Tile))continue;
            Point? stand=null;int count=0;
            foreach(var at in new[]{new Point(c.Tile.X,c.Tile.Y+1),new Point(c.Tile.X-1,c.Tile.Y),new Point(c.Tile.X+1,c.Tile.Y),new Point(c.Tile.X,c.Tile.Y-1)}.OrderBy(x=>Vector2.DistanceSquared(x.ToVector2(),p.Tile))) {
                if(!PlayerExecutor.Passable(l,at))continue;
                if(at==p.TilePoint){stand=at;break;}
                var path=PlayerExecutor.PreviewPath(l,at);
                if(path?.Count>0){stand=at;count=path.Count;break;}
            }
            if(stand==null)continue;
            int minutes=Math.Max(10,(int)Math.Ceiling((count*64/Math.Max(1,p.getMovementSpeed())/60.0+4+c.Energy)*10/7/10)*10);
            result.Add(new(c.Skill,c.Slot,c.Tile.X,c.Tile.Y,stand.Value.X,stand.Value.Y,c.Item,c.Purpose,count,minutes,c.Energy,Useful(c.Skill,c.Item),
                (c.Skill=="water" || (c.Item.Length>0?CapacityAdapter.CanReceive(p,ItemRegistry.Create(c.Item)):inventoryRoom)) && DailyBudget.Fits(Game1.timeOfDay,p.Stamina,ReturnReserve(),minutes,c.Energy)));
            if(result.Count>=12)break;
        }
        return result;
    }
    internal object AgentDailyRead()=>AgentDay();
    internal object AgentDailyPlan(JsonElement args) {
        var priorities=args.TryGetProperty("priorities",out var list)?JsonSerializer.Deserialize<List<string>>(list.GetRawText()):null;
        var resources=args.TryGetProperty("resources",out var material)?JsonSerializer.Deserialize<List<DailyResource>>(material.GetRawText(),new JsonSerializerOptions{PropertyNameCaseInsensitive=true}):new();
        if(priorities==null || priorities.Count is <1 or >8 || priorities.Any(p=>string.IsNullOrWhiteSpace(p)||p.Length>160) || resources==null || resources.Count>8 || resources.Any(r=>r==null||r.Count is <1 or >9999||string.IsNullOrWhiteSpace(r.Purpose)||r.Purpose.Length>160||string.IsNullOrWhiteSpace(r.Item)||ItemRegistry.GetData(r.Item)==null))throw new InvalidOperationException("invalid_day_plan");
        Data.Autoplay.Agenda.Priorities=priorities;Data.Autoplay.Agenda.Resources=resources;return AgentDay();
    }
    internal void CheckAgentSleep(JsonElement args) {
        if(!AutoplayRunning)return; // Direct lab executor tests do not pretend to be model planning.
        RefreshFacts(true);
        List<OperatingOpportunity> opportunities;
        reviewingSleep=true;
        try{opportunities=OperatingOpportunities();}finally{reviewingSleep=false;}
        var options=SleepAlternatives(opportunities);int minutes=DailyBudget.WorkMinutes(Game1.timeOfDay,ReturnReserve());
        var facts=new{time=Game1.timeOfDay,stamina=Game1.player.Stamina,work_minutes=minutes,Facts.DryCrops,Facts.RipeCrops,alternatives=options};
        var block=DecisionReviewPolicy.Sleep(args,Game1.player.Stamina,minutes,options);
        Data.Autoplay.Record("sleep_review",AgentJson.Encode(new{facts,blocking_rule=block,args}));
        RecordDecisionReview("sleep",args,facts,block);
    }
}
