using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed class AgentToolRegistry {
    private readonly ModEntry mod;
    private readonly PlayerExecutor player;
    private readonly NativeMenuTools menus=new();
    public AgentToolRegistry(ModEntry mod,PlayerExecutor player){this.mod=mod;this.player=player;}
    public void Reset()=>menus.Reset();
    public static bool IsPlayerMutation(string name)=>name.StartsWith("player.") || name.StartsWith("menu.") && name!="menu.read";
    public static readonly Dictionary<string,string> Catalog=new(){
        ["plan.read"]="{}: 持续任务队列、revision、双角色独立状态和真实回执；queued不是完成",
        ["plan.submit"]="{submission_id:string,expected_revision:int,tasks:[{id:string,actor:player或真实actor_id,tool:string,args:{},after?:[任务id],location?:string,day?:绝对day,not_before?:HHMM,deadline?:HHMM,purpose?:string}]}: 一次提交1到24步，允许player动作与companion.assign；同角色依次执行，不同角色并行。当前日默认，最远7天；跨地图后动作写明location；未观察的参数先查询。重复submission_id幂等。",
        ["plan.cancel"]="{ids:[任务id]}: 取消指定任务；保存开始后不可取消。失败后取消受阻旧计划，再根据真实状态提交新任务",
        ["plan.archive"]="{}: 清理已结束且不再被依赖的任务记录，保留在用依赖与全局核验计数",
        ["day.read"]="{}: 今日农务、任务、材料缺口、可达工作候选与时间/体力预算；每批完成自动刷新",
        ["day.plan"]="{priorities:[string],resources?:[{item:string,count:int,purpose:string}]}: 保存1至8项优先事项和最多8项目标库存（总量，非增量）；按实际库存核验",
        ["world.read"]="{}: 日期、环境、农场、伙伴actor_id及真实candidates、共同目标",
        ["map.read"]="{x?:int,y?:int,radius?:1..20}: 当前地图局部格子、障碍、作物、矿物、交互、真实出口；坐标可用于移动与操作",
        ["inventory.read"]="{}: 玩家背包slot、ID、数量、工具；含手持物",
        ["knowledge.search"]="{query:string}: 原生百科模糊检索",
        ["knowledge.get"]="{query?:string,id?:string}: 百科详细证据与实时条件",
        ["goal.requirements"]="{id:string}: 已有百科物品/配方条目的需求与现有库存",
        ["progress.missing"]="{}: 按原生Data/Achievements列出未完成条目的名称、描述与ID；不等同于平台全成就检查",
        ["progress.read"]="{}: 玩家原生任务、技能、配方计数、邮件、成就；不是Steam成就证明",
        ["player.work"]="{skill:water|till|plant|harvest|clear|forage,slot?:int,tiles:[{x:int,y:int}]}: 最多36格同图农活/资源收集（clear仅石块/树枝，须正确工具；forage无需工具）；自动寻路、工具动画、逐格核验；避免每格请求模型",
        ["player.move"]="{x:int,y:int}: 原生寻路走到当前地图目标；返回动作ID",
        ["player.travel"]="{location:string}: 按实际出口/建筑门前往已加载地点；锁门会失败",
        ["player.use_tool"]="{slot:int,x:int,y:int}: 使用实际工具击打相邻格，保留动画与原生结算",
        ["player.interact"]="{x:int,y:int,slot?:int}: 邻格原生交互，如收获、NPC、机器、门、矿梯",
        ["player.place"]="{slot:int,x:int,y:int}: 使用真实持有的种子/可放物品，原生判定及消耗",
        ["player.ship"]="{slot:int}: 在真实农场出货箱旁，将指定槽位整叠可售物品投入出货箱；次日原生结算，不提前加钱",
        ["player.sleep"]="{reason:string,review?:string}: 正常经营须先查看day.read；提前休息必须说明替代活动为何不可行，有可行工作时拒绝。 回家、真实床位、睡眠确认、结算、保存、第二天；须选择的夜间菜单用menu工具",
        ["menu.read"]="{}: 原生菜单文本、可选响应、组件id、token、手持物；不使用截图",
        ["menu.open"]="{page:inventory|crafting|journal}: 打开相应原生菜单",
        ["menu.choose"]="{token:string,id:string,right?:bool}: 点击刚读取的原生组件，过期token拒绝；返回菜单状态，不声称业务完成",
        ["menu.scroll"]="{direction:up|down}: 原生菜单滚动一页",
        ["menu.close"]="{}: 仅当原生允许安全关闭且无手持物时关闭",
        ["companion.assign"]="{actor_id:string,skill:string,target_id?:string,destination?:string,seconds?:int}: travel 必须带 destination，只负责到达；mine/water/harvest/forage/clear/collect/pet/till/plant/feed/tend/buy/ship/gift/refill/deposit 必须带当前 world.read 候选的 target_id；劳动不能用 destination 代替目标。follow/stay 切换模式，guard/rest/fish 可带 seconds。跨图劳动分两轮：travel 成功→读取新候选→派劳动。",
        ["action.status"]="{id:string}: 动作真实进度和前后证据；也接受 plan 的任务 id，排队状态不是完成",
        ["action.cancel"]="{id:string}: 取消尚可取消的动作，已消耗物资不回滚",
        ["agent.wait"]="{seconds:1..60}: 等待游戏进展，期间不重复请求模型",
        ["agent.pause"]="{reason:string}: 保存计划并暂停接管"
    };
    internal static string Text(JsonElement a,string k,string fallback="")=>a.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??fallback:fallback;
    internal static int Number(JsonElement a,string k,int fallback=0)=>a.TryGetProperty(k,out var v)&&v.TryGetInt32(out int n)?n:fallback;
    public object Execute(string tool,JsonElement args) {
        if(!Context.IsWorldReady || Context.IsMultiplayer)throw new InvalidOperationException("single_player_world_required");
        if(!Catalog.ContainsKey(tool))throw new InvalidOperationException("unknown_tool");
        if(tool.StartsWith("menu.") && tool!="menu.read" && player.Busy && !player.NeedsMenuChoice)throw new InvalidOperationException("player_busy");
        if(tool is "player.use_tool" or "player.place" or "player.interact" or "player.work") {
            IEnumerable<JsonElement> targets=tool=="player.work" && args.TryGetProperty("tiles",out var tiles) && tiles.ValueKind==JsonValueKind.Array?tiles.EnumerateArray().ToArray():new[]{args};
            if(targets.Any(t=>mod.AgentTileBusy(Game1.currentLocation.NameOrUniqueName,Number(t,"x",-1),Number(t,"y",-1))))throw new InvalidOperationException("target_claimed_by_companion");
        }
        if(tool=="player.sleep")mod.CheckAgentSleep(args);
        return tool switch {
            "plan.read"=>mod.AgentPlanRead(),"plan.submit"=>mod.AgentPlanSubmit(args),"plan.cancel"=>mod.AgentPlanCancel(args),"plan.archive"=>mod.AgentPlanArchive(),
            "day.read"=>mod.AgentDailyRead(),"day.plan"=>mod.AgentDailyPlan(args),
            "world.read"=>mod.AgentWorld(),"map.read"=>ReadMap(args),"inventory.read"=>Inventory(),
            "knowledge.search"=>mod.Knowledge.Search(Text(args,"query"),limit:8),
            "knowledge.get" or "goal.requirements"=>mod.Knowledge.Query(Text(args,"query"),Text(args,"id") is {Length:>0} id?id:null),
            "progress.read"=>Progress(),"progress.missing"=>Game1.achievements.Where(a=>!Game1.player.achievements.Contains(a.Key)).Select(a=>new{id=a.Key,name=a.Value.Split('^')[0],native_definition=a.Value,source="Data/Achievements"}).ToArray(),
            "player.work" or "player.move" or "player.travel" or "player.use_tool" or "player.interact" or "player.place" or "player.sleep" or "player.ship"=>player.Start(tool,args),
            "menu.read"=>menus.Read(),"menu.open"=>menus.Open(Text(args,"page")),"menu.choose"=>menus.Choose(args),
            "menu.scroll"=>menus.Scroll(Text(args,"direction")),"menu.close"=>menus.Close(),
            "companion.assign"=>mod.AgentCompanion(args),
            "action.status"=>mod.AgentReceipt(Text(args,"id"),false),"action.cancel"=>mod.AgentReceipt(Text(args,"id"),true),
            "agent.wait"=>Wait(Number(args,"seconds",1)),"agent.pause"=>Pause(Text(args,"reason","模型请求暂停")),
            _=>throw new InvalidOperationException("unknown_tool")
        };
    }
    private object Wait(int seconds){int actual=mod.AgentWait(seconds);return new{status="waiting",seconds=actual,note="按原生时间限制等待长度，深夜前重新决策"};}
    private object Pause(string reason){mod.PauseAutoplay(reason);return new{status="paused",reason};}
    internal static object ItemInfo(Item? item)=>item==null?new{empty=true}:(object)new{id=item.QualifiedItemId,name=item.DisplayName,count=item.Stack,quality=item.Quality,kind=item.GetType().Name};
    internal static object Inventory()=>new{selected=Game1.player.CurrentToolIndex,items=Game1.player.Items.Select((v,i)=>new{slot=i,item=ItemInfo(v)}).ToArray()};
    private static object Progress()=>new{scope="native Farmer and team; platform achievements not verified",achievements=Game1.player.achievements.ToArray(),
        crafting=Game1.player.craftingRecipes.Pairs.ToDictionary(p=>p.Key,p=>p.Value),cooking=Game1.player.cookingRecipes.Pairs.ToDictionary(p=>p.Key,p=>p.Value),
        skills=new{farming=Game1.player.FarmingLevel,mining=Game1.player.MiningLevel,fishing=Game1.player.FishingLevel,foraging=Game1.player.ForagingLevel,combat=Game1.player.CombatLevel},
        shipped=Game1.player.basicShipped.Pairs.ToDictionary(p=>p.Key,p=>p.Value),cooked=Game1.player.recipesCooked.Pairs.ToDictionary(p=>p.Key,p=>p.Value),
        fish_caught=Game1.player.fishCaught.Pairs.ToDictionary(p=>p.Key,p=>p.Value),minerals=Game1.player.mineralsFound.Pairs.ToDictionary(p=>p.Key,p=>p.Value),
        deepest_mine=Game1.player.deepestMineLevel,mail=Game1.player.mailReceived.ToArray(),
        quests=Game1.player.questLog.Select(q=>new{id=q.id.Value,title=q.questTitle,description=q.questDescription,completed=q.completed.Value}).ToArray(),
        note="未覆盖全部成就条件；缺少条目不能解释为已完成"};
    private static object ReadMap(JsonElement args) {
        var l=Game1.currentLocation;int r=Math.Clamp(Number(args,"radius",8),1,20),cx=Number(args,"x",Game1.player.TilePoint.X),cy=Number(args,"y",Game1.player.TilePoint.Y);
        int width=l.Map.Layers[0].LayerWidth,height=l.Map.Layers[0].LayerHeight;
        cx=Math.Clamp(cx,0,width-1);cy=Math.Clamp(cy,0,height-1);var cells=new List<object>();var rows=new List<string>();
        int x0=Math.Max(0,cx-r),y0=Math.Max(0,cy-r);
        for(int y=y0;y<=Math.Min(height-1,cy+r);y++){var row=new System.Text.StringBuilder();for(int x=x0;x<=Math.Min(width-1,cx+r);x++) {
            var v=new Vector2(x,y);l.objects.TryGetValue(v,out var o);l.terrainFeatures.TryGetValue(v,out var feature);var dirt=feature as HoeDirt;
            string? action=l.doesTileHaveProperty(x,y,"Action","Buildings"),touch=l.doesTileHaveProperty(x,y,"TouchAction","Back");
            bool passable=PlayerExecutor.Passable(l,new(x,y)),water=l.isWaterTile(x,y);
            row.Append(water?'~':!passable?'#':l.doesTileHaveProperty(x,y,"Diggable","Back")!=null?'.':'_');
            if(o!=null||feature!=null||action!=null||touch!=null)cells.Add(new{x,y,
                item=o==null?null:ItemInfo(o),terrain=feature?.GetType().Name,watered=dirt?.state.Value==1,
                crop=dirt?.crop==null?null:new{harvest=dirt.crop.indexOfHarvest.Value,phase=dirt.crop.currentPhase.Value,dead=dirt.crop.dead.Value,ready=dirt.readyForHarvest()},action,touch});
        }rows.Add(row.ToString());}
        return new{location=l.NameOrUniqueName,width,height,center=new[]{cx,cy},radius=r,x0,y0,
            exits=PlayerExecutor.Exits(l).Select(e=>new{e.X,e.Y,e.TargetName,e.TargetX,e.TargetY}),
            buildings=l.buildings.Select(b=>new{type=b.buildingType.Value,x=b.tileX.Value,y=b.tileY.Value,width=b.tilesWide.Value,height=b.tilesHigh.Value}),
            characters=l.characters.Select(n=>new{name=n.Name,x=n.TilePoint.X,y=n.TilePoint.Y,monster=n.IsMonster}),
            furniture=l.furniture.Select(f=>new{name=f.Name,x=f.TileLocation.X,y=f.TileLocation.Y}),
            home=Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName,grid=rows,legend="# blocked, ~ water, . clear diggable, _ clear non-diggable; origin x0,y0; moving characters may block",cells=cells.Take(36),cells_truncated=cells.Count>36,note="建筑和出口先列出，cells截断时缩小radius或移动中心查询，缺省格子不等于没有对象"};
    }
}
