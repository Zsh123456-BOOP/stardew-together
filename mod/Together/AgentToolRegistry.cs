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
        ["world.read"]="{}: 日期、环境、农场、伙伴actor_id及真实candidates、共同目标",
        ["map.read"]="{x?:int,y?:int,radius?:1..20}: 当前地图局部格子、障碍、作物、矿物、交互、真实出口；坐标可用于移动与操作",
        ["inventory.read"]="{}: 玩家背包slot、ID、数量、工具；含手持物",
        ["knowledge.search"]="{query:string}: 原生百科模糊检索",
        ["knowledge.get"]="{query?:string,id?:string}: 百科详细证据与实时条件",
        ["goal.requirements"]="{id:string}: 已有百科物品/配方条目的需求与现有库存",
        ["progress.read"]="{}: 玩家原生任务、技能、配方计数、邮件、成就；不是Steam成就证明",
        ["player.work"]="{skill:water|till|plant|harvest,slot?:int,tiles:[{x:int,y:int}]}: 最多36格同图农活；自动寻路、工具动画、逐格核验；避免每格请求模型",
        ["player.move"]="{x:int,y:int}: 原生寻路走到当前地图目标；返回动作ID",
        ["player.travel"]="{location:string}: 按实际出口/建筑门前往已加载地点；锁门会失败",
        ["player.use_tool"]="{slot:int,x:int,y:int}: 使用实际工具击打相邻格，保留动画与原生结算",
        ["player.interact"]="{x:int,y:int,slot?:int}: 邻格原生交互，如收获、NPC、机器、门、矿梯",
        ["player.place"]="{slot:int,x:int,y:int}: 使用真实持有的种子/可放物品，原生判定及消耗",
        ["player.sleep"]="{}: 回家、真实床位、睡眠确认、结算、保存、第二天；须选择的夜间菜单用menu工具",
        ["menu.read"]="{}: 原生菜单文本、可选响应、组件id、token、手持物；不使用截图",
        ["menu.open"]="{page:inventory|crafting|journal}: 打开相应原生菜单",
        ["menu.choose"]="{token:string,id:string,right?:bool}: 点击刚读取的原生组件，过期token拒绝；返回菜单状态，不声称业务完成",
        ["menu.scroll"]="{direction:up|down}: 原生菜单滚动一页",
        ["menu.close"]="{}: 仅当原生允许安全关闭且无手持物时关闭",
        ["companion.assign"]="{actor_id:string,skill:string,target_id?:string,destination?:string,seconds?:int}: 使用world.read的真实伙伴及候选；不猜目标ID",
        ["action.status"]="{id:string}: 动作真实进度和前后证据",
        ["action.cancel"]="{id:string}: 取消尚可取消的动作，已消耗物资不回滚",
        ["agent.wait"]="{seconds:1..60}: 等待游戏进展，期间不重复请求模型",
        ["agent.pause"]="{reason:string}: 保存计划并暂停接管"
    };
    internal static string Text(JsonElement a,string k,string fallback="")=>a.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??fallback:fallback;
    internal static int Number(JsonElement a,string k,int fallback=0)=>a.TryGetProperty(k,out var v)&&v.TryGetInt32(out int n)?n:fallback;
    public object Execute(string tool,JsonElement args) {
        if(!Context.IsWorldReady || Context.IsMultiplayer)throw new InvalidOperationException("single_player_world_required");
        if(!Catalog.ContainsKey(tool))throw new InvalidOperationException("unknown_tool");
        return tool switch {
            "world.read"=>mod.AgentWorld(),"map.read"=>ReadMap(args),"inventory.read"=>Inventory(),
            "knowledge.search"=>mod.Knowledge.Search(Text(args,"query"),limit:8),
            "knowledge.get" or "goal.requirements"=>mod.Knowledge.Query(Text(args,"query"),Text(args,"id") is {Length:>0} id?id:null),
            "progress.read"=>Progress(),
            "player.work" or "player.move" or "player.travel" or "player.use_tool" or "player.interact" or "player.place" or "player.sleep"=>player.Start(tool,args),
            "menu.read"=>menus.Read(),"menu.open"=>menus.Open(Text(args,"page")),"menu.choose"=>menus.Choose(args),
            "menu.scroll"=>menus.Scroll(Text(args,"direction")),"menu.close"=>menus.Close(),
            "companion.assign"=>mod.AgentCompanion(args),
            "action.status"=>mod.AgentReceipt(Text(args,"id"),false),"action.cancel"=>mod.AgentReceipt(Text(args,"id"),true),
            "agent.wait"=>Wait(Number(args,"seconds",1)),"agent.pause"=>Pause(Text(args,"reason","模型请求暂停")),
            _=>throw new InvalidOperationException("unknown_tool")
        };
    }
    private object Wait(int seconds){mod.AgentWait(seconds);return new{status="waiting",seconds=Math.Clamp(seconds,1,60)};}
    private object Pause(string reason){mod.PauseAutoplay(reason);return new{status="paused",reason};}
    internal static object ItemInfo(Item? item)=>item==null?new{empty=true}:(object)new{id=item.QualifiedItemId,name=item.DisplayName,count=item.Stack,quality=item.Quality,kind=item.GetType().Name};
    internal static object Inventory()=>new{selected=Game1.player.CurrentToolIndex,items=Game1.player.Items.Select((v,i)=>new{slot=i,item=ItemInfo(v)}).ToArray()};
    private static object Progress()=>new{scope="native Farmer and team; platform achievements not verified",achievements=Game1.player.achievements.ToArray(),
        crafting=Game1.player.craftingRecipes.Pairs.ToDictionary(p=>p.Key,p=>p.Value),cooking=Game1.player.cookingRecipes.Pairs.ToDictionary(p=>p.Key,p=>p.Value),
        skills=new{farming=Game1.player.FarmingLevel,mining=Game1.player.MiningLevel,fishing=Game1.player.FishingLevel,foraging=Game1.player.ForagingLevel,combat=Game1.player.CombatLevel},
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
        return new{location=l.NameOrUniqueName,width,height,center=new[]{cx,cy},radius=r,x0,y0,grid=rows,legend="# blocked, ~ water, . clear diggable, _ clear non-diggable; origin x0,y0; moving characters may block",cells=cells.Take(100),cells_truncated=cells.Count>100,
            exits=PlayerExecutor.Exits(l).Select(e=>new{e.X,e.Y,e.TargetName,e.TargetX,e.TargetY}),
            characters=l.characters.Select(n=>new{name=n.Name,x=n.TilePoint.X,y=n.TilePoint.Y,monster=n.IsMonster}),
            furniture=l.furniture.Select(f=>new{name=f.Name,x=f.TileLocation.X,y=f.TileLocation.Y}),
            home=Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName};
    }
}
