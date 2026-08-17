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
        ["capabilities.read"]="{}: 27类能力的已接工具、角色、核验方式及明确缺口；存在工具不代表完整验收",
        ["memory.search"]="{query?:string,actor?:string,limit?:1..20,offset?:int}: 检索本存档已归档事件/回执，不含读档后的未来记录；返回证据ID和截断提示",
        ["memory.evidence"]="{id:string,offset?:int}: 按归档证据ID读取原文，每页最多4000字符；继续next_offset能读完整记录；不能访问本存档时间线之外的历史",
        ["map.scan"]="{actor_id?:string,offset?:int,limit?:1..120}: 指定角色当前地图完整对象/地形分页，含树木、作物和状态版本；不会把局部地图截断当资源不存在",
        ["player.eat"]="{slot:int}: 吃真实背包的一份普通食物，原生动画/恢复/消耗核验；保留物由高层补给政策决定",
        ["player.craft"]="{recipe:原生配方名,count?:1..99}: 连续制作指定批次，自动原生菜单/材料消耗/成品入包/统计核验；只用背包原料，缺料需先取货",
        ["player.cook"]="{recipe:原生配方名,count?:1..99}: 回已升级住宅厨房烹饪，使用真实背包原料与原生烹饪统计；目前需已解锁家中厨房",
        ["storage.configure"]="{location?:string,x:int,y:int,role:output|none}: 给已观察的真实玩家箱设置同行收货/取货标记；不转移物资；work.run(goal=withdraw,item=ID,count=数量,quality=最低品质)自动去共享箱取货",
        ["farm.plan"]="{seed?:物品ID,count?:1..96,max_daily_manual_water?:0..96,require_scarecrow?:bool}: 在农场/温室按已有种子和真实可达地形生成地块方案，保护出入口、工作站位，架子作物检查种下后可达性；返回plan_id，work.run(goal=plant,plan_id=...)自动翻土播种浇水补水。当前不采购种子、无肥料/跨季优化，不把收益估算当实收。",
        ["progress.catalog"]="{kind?:achievement|crafting|cooking|quest|order|route|bundle|scope,offset?:int,limit?:1..80}: 当前原生目标及配方分页，含依赖、材料、完成证据、缺口；按next_offset继续，未知条件不能猜",
        ["plan.read"]="{}: 持续任务队列、revision、双角色独立状态和真实回执；queued不是完成",
        ["plan.submit"]="{submission_id:string,expected_revision:int,tasks:[{id:string,actor:player或真实actor_id,tool:string,args:{},after?:[任务id],location?:string,day?:绝对day,not_before?:HHMM,deadline?:HHMM,purpose?:string}]}: 一次提交1到24步，允许player动作与companion.assign；同角色依次执行，不同角色并行。当前日默认，最远7天；跨地图后动作写明location；未观察的参数先查询。重复submission_id幂等。",
        ["plan.cancel"]="{ids:[任务id]}: 取消指定任务；保存开始后不可取消。失败后取消受阻旧计划，再根据真实状态提交新任务",
        ["plan.archive"]="{}: 清理已结束且不再被依赖的任务记录，保留在用依赖与全局核验计数",
        ["day.read"]="{}: 今日农务、任务、材料缺口、可达工作候选与时间/体力预算；每批完成自动刷新",
        ["day.plan"]="{priorities:[string],resources?:[{item:string,count:int,purpose:string}]}: 保存1至8项优先事项和最多8项目标库存（总量，非增量）；按实际库存核验",
        ["world.read"]="{}: 日期、环境、农场、伙伴actor_id及真实candidates、共同目标",
        ["map.read"]="{actor_id?:player或真实伙伴ID,x?:int,y?:int,radius?:1..20}: 指定角色所在地图局部格子、障碍、作物、矿物、交互、真实出口；坐标可用于移动与操作",
        ["inventory.read"]="{}: 玩家背包slot、ID、数量、工具；含手持物",
        ["knowledge.search"]="{query:string}: 原生百科模糊检索",
        ["knowledge.get"]="{query?:string,id?:string}: 百科详细证据与实时条件",
        ["goal.requirements"]="{id:string}: 已有百科物品/配方条目的需求与现有库存",
        ["goal.create"]="{request_id:string,entity:物品ID或craft:配方名,count?:int}: 幂等创建持久共同目标，原生配方自动展开依赖并预留材料，不打开UI",
        ["goal.prepare"]="{id:共同目标ID}: 根据真实库存生成下一批可执行任务（共享箱取料、普通资源收集、原生制作）；返回tasks可直接交plan.submit；每批后重新核算，未接通路线返回gaps",
        ["progress.missing"]="{}: 按原生Data/Achievements列出未完成条目的名称、描述与ID；不等同于平台全成就检查",
        ["progress.roadmap"]="{}: 原生成就、实际技能与下一阶段建议；建议不是已经完成的成就，按季节与前置条件并行安排",
        ["progress.read"]="{}: 玩家原生任务、技能、配方计数、邮件、成就；不是Steam成就证明",
        ["work.run"]="{actor_id?:player或真实伙伴ID,goal:withdraw|plant|stone|wood|fiber|water|refill|harvest|forage|clear_dead|store,item?:物品ID,quality?:int,plan_id?:string,include_trees?:bool,max_food?:0..10,location?:真实地图名,count?:int,reserve_stamina?:15..270,until?:HHMM<=2300}: 高层持续劳动，无需坐标/工具槽/target_id。石/木/纤维count为本次实际新增物品数量（默认20），其它count为目标数，0做完当前地图可做目标。自动走到指定地图、选工具、逐次寻路换目标，玩家浇水自动补水再继续。木材默认树枝，include_trees=true时玩家可砍成熟未挂树液器的普通树；plant用farm.plan返回的plan_id执行布局，plant/clear_dead/refill限玩家；NPC无原生体力条，按能力/货物/时间限制。中断或部分完成返回实际数量与stop_reason，不伪报达标。默认保留20体力、22点停止；体力不足可吃最多max_food份普通非预留食物（默认3），然后续作；采集前自动留2空槽，不足则走回Farm的output箱卸货再回来。store可主动存货；保留工具/种子/补给/预留物，不会丢弃出售。可取消/查进度。",
        ["player.work"]="{skill:water|till|plant|harvest|clear|clear_dead|forage,slot?:int,tiles:[{x:int,y:int}]}: 最多36格同图农活/资源收集（clear支持石块用镐、树枝用斧、杂草用镰刀；clear_dead仅镰刀清理枯死作物，不清理活苗；forage仅拾取真实野生采集物）；自动寻路、工具动画、逐格核验；避免每格请求模型",
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
    public object Execute(string tool,JsonElement args) => mod.WithExecutionContract(ExecuteCore(tool,args));
    private object ExecuteCore(string tool,JsonElement args) {
        if(!Context.IsWorldReady || Context.IsMultiplayer)throw new InvalidOperationException("single_player_world_required");
        if(!Catalog.ContainsKey(tool))throw new InvalidOperationException("unknown_tool");
        if((IsPlayerMutation(tool)&&mod.WorkActorBusy("player")) || tool=="companion.assign"&&mod.WorkActorBusy(Text(args,"actor_id")))throw new InvalidOperationException("actor_owned_by_work_job_cancel_or_wait");
        if(tool.StartsWith("menu.") && tool!="menu.read" && player.Busy && !player.NeedsMenuChoice)throw new InvalidOperationException("player_busy");
        if(tool is "player.use_tool" or "player.place" or "player.interact" or "player.work") {
            IEnumerable<JsonElement> targets=tool=="player.work" && args.TryGetProperty("tiles",out var tiles) && tiles.ValueKind==JsonValueKind.Array?tiles.EnumerateArray().ToArray():new[]{args};
            if(targets.Any(t=>mod.AgentTileBusy(Game1.currentLocation.NameOrUniqueName,Number(t,"x",-1),Number(t,"y",-1))))throw new InvalidOperationException("target_claimed_by_companion");
        }
        if(tool=="player.sleep")mod.CheckAgentSleep(args);
        return tool switch {
            "capabilities.read"=>CapabilityCatalog.Read(),"progress.catalog"=>mod.AgentProgressCatalog(args),
            "memory.search"=>mod.ReadAgentMemory(args),
            "memory.evidence"=>mod.ReadMemoryEvidence(args),
            "map.scan"=>mod.ScanMap(args),
            "farm.plan"=>mod.PlanFarm(args),
            "storage.configure"=>mod.ConfigureStorage(args),
            "plan.read"=>mod.AgentPlanRead(),"plan.submit"=>mod.AgentPlanSubmit(args),"plan.cancel"=>mod.AgentPlanCancel(args),"plan.archive"=>mod.AgentPlanArchive(),
            "day.read"=>mod.AgentDailyRead(),"day.plan"=>mod.AgentDailyPlan(args),
            "world.read"=>mod.AgentWorld(),"map.read"=>ReadMap(args),"inventory.read"=>Inventory(),
            "knowledge.search"=>mod.Knowledge.Search(Text(args,"query"),limit:8),
            "knowledge.get" or "goal.requirements"=>mod.Knowledge.Query(Text(args,"query"),Text(args,"id") is {Length:>0} id?id:null),
            "goal.create"=>mod.AgentGoalCreate(args),"goal.prepare"=>mod.AgentGoalPrepare(args),
            "progress.read"=>Progress(),"progress.roadmap"=>mod.AgentProgression(),"progress.missing"=>Game1.achievements.Where(a=>!Game1.player.achievements.Contains(a.Key)).Select(a=>new{id=a.Key,name=a.Value.Split('^')[0],native_definition=a.Value,source="Data/Achievements"}).ToArray(),
            "work.run"=>mod.StartSemanticWork(args),
            "player.craft" or "player.cook" or "player.eat" or "player.work" or "player.move" or "player.travel" or "player.use_tool" or "player.interact" or "player.place" or "player.sleep" or "player.ship"=>player.Start(tool,args),
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
    internal static object ItemInfo(Item? item)=>item==null?new{empty=true}:(object)new{id=item.QualifiedItemId,name=item.DisplayName,count=item.Stack,quality=item.Quality,kind=item.GetType().Name,upgrade_level=item is Tool tool?(int?)tool.UpgradeLevel:null,water_left=item is StardewValley.Tools.WateringCan can?(int?)can.WaterLeft:null};
    internal static object Inventory()=>new{selected=Game1.player.CurrentToolIndex,items=Game1.player.Items.Select((v,i)=>new{slot=i,item=ItemInfo(v)}).ToArray()};
    private static object Progress()=>new{scope="native Farmer and team; platform achievements not verified",achievements=Game1.player.achievements.ToArray(),
        crafting=Game1.player.craftingRecipes.Pairs.ToDictionary(p=>p.Key,p=>p.Value),cooking=Game1.player.cookingRecipes.Pairs.ToDictionary(p=>p.Key,p=>p.Value),
        skills=new{farming=Game1.player.FarmingLevel,mining=Game1.player.MiningLevel,fishing=Game1.player.FishingLevel,foraging=Game1.player.ForagingLevel,combat=Game1.player.CombatLevel},
        shipped=Game1.player.basicShipped.Pairs.ToDictionary(p=>p.Key,p=>p.Value),cooked=Game1.player.recipesCooked.Pairs.ToDictionary(p=>p.Key,p=>p.Value),
        fish_caught=Game1.player.fishCaught.Pairs.ToDictionary(p=>p.Key,p=>p.Value),minerals=Game1.player.mineralsFound.Pairs.ToDictionary(p=>p.Key,p=>p.Value),
        deepest_mine=Game1.player.deepestMineLevel,mail=Game1.player.mailReceived.ToArray(),
        quests=Game1.player.questLog.Select(q=>new{id=q.id.Value,title=q.questTitle,description=q.questDescription,completed=q.completed.Value}).ToArray(),
        note="未覆盖全部成就条件；缺少条目不能解释为已完成"};
    private object ReadMap(JsonElement args) {
        string actorId=Text(args,"actor_id","player");var origin=mod.AgentMapOrigin(actorId);
        var l=origin.Location;int r=Math.Clamp(Number(args,"radius",8),1,20),cx=Number(args,"x",origin.Tile.X),cy=Number(args,"y",origin.Tile.Y);
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
        return new{actor_id=actorId,location=l.NameOrUniqueName,width,height,center=new[]{cx,cy},radius=r,x0,y0,
            exits=PlayerExecutor.Exits(l).Select(e=>new{e.X,e.Y,e.TargetName,e.TargetX,e.TargetY}),
            buildings=l.buildings.Select(b=>new{type=b.buildingType.Value,x=b.tileX.Value,y=b.tileY.Value,width=b.tilesWide.Value,height=b.tilesHigh.Value,door=b.humanDoor.Value.X<0?null:new{x=b.tileX.Value+b.humanDoor.Value.X,y=b.tileY.Value+b.humanDoor.Value.Y},interior=b.GetIndoors()?.NameOrUniqueName}),
            characters=l.characters.Select(n=>new{name=n.Name,x=n.TilePoint.X,y=n.TilePoint.Y,monster=n.IsMonster}),
            farm_animals=Game1.getFarm().getAllFarmAnimals().Where(a=>a.currentLocation==l).Select(a=>new{name=a.displayName,x=a.TilePoint.X,y=a.TilePoint.Y,pet=a.wasPet.Value,fullness=a.fullness.Value}),
            furniture=l.furniture.Select(f=>new{name=f.Name,x=f.TileLocation.X,y=f.TileLocation.Y}),
            home=Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName,grid=rows,legend="# blocked, ~ water, . clear diggable, _ clear non-diggable; origin x0,y0; moving characters may block",cells=cells.Take(36),cells_truncated=cells.Count>36,note="建筑和出口先列出，cells截断时缩小radius或移动中心查询，缺省格子不等于没有对象"};
    }
}
