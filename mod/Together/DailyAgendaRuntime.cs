using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Together;
public sealed record DayOption(string skill,int slot,int x,int y,int stand_x,int stand_y,string item,string purpose,int route_tiles,int estimated_minutes,float estimated_energy,bool useful,bool fits);
public sealed partial class ModEntry {
    private int dayReviewed=-1;
    private object AgentDay() {
        RefreshFacts(true);Data.Autoplay.Agenda.EnterDay(Game1.Date.TotalDays);
        var options=DayOptions();dayReviewed=Game1.Date.TotalDays;
        var home=Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName;
        return new {
            day=Game1.Date.TotalDays,time=Game1.timeOfDay,location=Game1.currentLocation.NameOrUniqueName,
            policy="正常时间；农务完成后选择有价值的下一批工作，不能只因种完就睡。程序估算时间与体力，模型决定优先级和分工。",
            budget=new{stamina=Game1.player.Stamina,energy_reserve=15,work_minutes=DailyBudget.WorkMinutes(Game1.timeOfDay,ReturnReserve()),return_reserve_minutes=ReturnReserve(),return_by=2300,home,estimate_note="路线格数来自当前地图原生寻路；耗时按移动速度加动作余量估算，跨图返家预留为保守预算，非到达保证"},
            chores=new{Facts.DryCrops,Facts.RipeCrops,Facts.AnimalsUnpetted,Facts.FeedNeeded,Facts.MachinesReady},
            priorities=Data.Autoplay.Agenda.Priorities,resource_targets=Data.Autoplay.Agenda.Resources.Select(r=>new{r.Item,r.Count,r.Purpose,owned=Facts.Stock.Where(s=>s.Item==r.Item).Sum(s=>s.Count),missing=Math.Max(0,r.Count-Facts.Stock.Where(s=>s.Item==r.Item).Sum(s=>s.Count))}),
            shared_goals=GoalContext(),quests=Facts.Quests.Take(8),
            options,resource_policy="材料优先满足day.plan目标库存和共同心愿缺口；没有经营目标时才使用木材50/石料25的起步储备。useful=false表示当前没有已声明用途，不要求清空整个农场。",options_scope="仅当前地图最近一批已核验路径的农活、石块、树枝和采集物，非全世界；空列表不能证明没有可做的事，换地点、查百科/任务、整理与补给也要考虑。",
            next_review="每批完成、换地图、换日或失败后刷新；不要按每个格子调用模型。伙伴可通过world.read并行派工；出货前先核对材料预留。",
            today_completed_batches=Data.Autoplay.Agenda.CompletedBatches,recent_days=Data.Autoplay.Agenda.History.TakeLast(3)
        };
    }
    private int ReturnReserve()=>Game1.currentLocation==Utility.getHomeOfFarmer(Game1.player)?20:Game1.currentLocation is StardewValley.Farm?45:90;
    private List<DayOption> DayOptions() {
        var l=Game1.currentLocation;var p=Game1.player;var candidates=new List<(Point Tile,string Skill,int Slot,string Item,string Purpose,float Energy)>();
        int Slot(Type type)=>Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]!=null && type.IsInstanceOfType(p.Items[i]),-1);
        int axe=Slot(typeof(Axe)),pick=Slot(typeof(Pickaxe)),can=Slot(typeof(WateringCan));
        foreach(var pair in l.terrainFeatures.Pairs)if(pair.Value is HoeDirt dirt && dirt.crop!=null && !dirt.crop.dead.Value) {
            if(dirt.readyForHarvest())candidates.Add((pair.Key.ToPoint(),"harvest",-1,"(O)"+dirt.crop.indexOfHarvest.Value,"收获成熟作物",0));
            else if(dirt.state.Value!=1 && can>=0 && ((WateringCan)p.Items[can]).WaterLeft>0)candidates.Add((pair.Key.ToPoint(),"water",can,"","今日照料",2));
        }
        foreach(var pair in l.objects.Pairs) {
            var o=pair.Value;var tile=pair.Key.ToPoint();
            if(o.IsTwig() && axe>=0)candidates.Add((tile,"clear",axe,"(O)388","收集木材用于建设与制作",4));
            else if(o.BaseName=="Stone" && pick>=0)candidates.Add((tile,"clear",pick,"(O)390","收集石料；产物以实际掉落为准",Math.Max(2,o.MinutesUntilReady*2)));
            else if(o.isForage() && !o.bigCraftable.Value)candidates.Add((tile,"forage",-1,o.QualifiedItemId,"拾取季节采集物，留用/补给/出货",0));
        }
        bool Useful(string skill,string item) {
            if(skill!="clear")return true;
            int owned=Facts.Stock.Where(s=>s.Item==item).Sum(s=>s.Count);
            if(Data.Autoplay.Agenda.Resources.Any(r=>r.Item==item && r.Count>owned))return true;
            if(Data.SharedGoals.Any(g=>g.Status=="active" && g.Nodes.Any(n=>n.Item==item&&n.ToPrepare>0)))return true;
            return Data.Autoplay.Agenda.Resources.Count==0 && !Data.SharedGoals.Any(g=>g.Status=="active") && owned<(item=="(O)388"?50:item=="(O)390"?25:0);
        }
        var result=new List<DayOption>();
        bool inventoryRoom=p.Items.Any(i=>i==null);
        foreach(var c in candidates.OrderBy(c=>c.Skill is "water" or "harvest"?0:Useful(c.Skill,c.Item)?1:2).ThenBy(c=>Vector2.DistanceSquared(c.Tile.ToVector2(),p.Tile)).Take(24)) {
            if(AgentTileBusy(l.NameOrUniqueName,c.Tile.X,c.Tile.Y))continue;
            Point? stand=null;int count=0;
            foreach(var at in new[]{new Point(c.Tile.X,c.Tile.Y+1),new Point(c.Tile.X-1,c.Tile.Y),new Point(c.Tile.X+1,c.Tile.Y),new Point(c.Tile.X,c.Tile.Y-1)}.OrderBy(x=>Vector2.DistanceSquared(x.ToVector2(),p.Tile))) {
                if(!PlayerExecutor.Passable(l,at))continue;
                if(at==p.TilePoint){stand=at;break;}
                var path=new PathFindController(p,l,at,-1);
                if(path.pathToEndPoint?.Count>0){stand=at;count=path.pathToEndPoint.Count;break;}
            }
            if(stand==null)continue;
            int minutes=Math.Max(10,(int)Math.Ceiling((count*64/Math.Max(1,p.getMovementSpeed())/60.0+4+c.Energy)*10/7/10)*10);
            result.Add(new(c.Skill,c.Slot,c.Tile.X,c.Tile.Y,stand.Value.X,stand.Value.Y,c.Item,c.Purpose,count,minutes,c.Energy,Useful(c.Skill,c.Item),
                (inventoryRoom || c.Skill=="water" || p.Items.Any(i=>i?.QualifiedItemId==c.Item && i.Stack<i.maximumStackSize())) && DailyBudget.Fits(Game1.timeOfDay,p.Stamina,ReturnReserve(),minutes,c.Energy)));
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
        var options=DayOptions();RefreshFacts(true);
        var block=DailyBudget.SleepBlock(Game1.timeOfDay,Game1.player.Stamina,options.Any(o=>o.fits&&o.useful&&o.estimated_energy==0),options.Any(o=>o.fits&&o.useful),Facts.DryCrops+Facts.RipeCrops>0,dayReviewed==Game1.Date.TotalDays,AgentToolRegistry.Text(args,"reason"));
        if(block!=null)throw new InvalidOperationException(block);
        // An empty local list is not evidence that the whole day is exhausted.
        if(Game1.timeOfDay<2200 && Game1.player.Stamina>15 && !args.TryGetProperty("review",out _))throw new InvalidOperationException("sleep_requires_review_of_alternatives");
        if(Game1.timeOfDay<2200 && Game1.player.Stamina>15 && AgentToolRegistry.Text(args,"review").Length<20)throw new InvalidOperationException("sleep_requires_review_of_alternatives");
    }
}
