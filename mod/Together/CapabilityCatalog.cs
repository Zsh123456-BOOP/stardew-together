using System.Text.Json;
using StardewValley;

namespace Together;

public sealed record CapabilityDefinition(string id,string title,string[] tools,string[] actors,string gap,string verifier);

// Capability declarations are intentionally conservative. A generic interact/menu
// endpoint never implies that every quest or minigame has an autonomous executor.
public static class CapabilityCatalog {
    public static readonly CapabilityDefinition[] All={
        new("F01","状态与地图",new[]{"world.read","map.read","map.scan","inventory.read"},new[]{"player","companion"},"全状态变更索引与离线规划隔离", "原生快照"),
        new("F02","移动与跨图",new[]{"player.travel","player.move","companion.assign"},new[]{"player","companion"},"特殊区域通道覆盖", "角色实际位置"),
        new("F03","工具装备",new[]{"inventory.read","equipment.read","player.equip","player.use_tool"},new[]{"player"},"饰品/工具附件/装备策略与实机验收", "实际工具与装备"),
        new("F04","连续采集",new[]{"work.run"},new[]{"player","companion"},"特殊地图再生/全矿种实机覆盖", "原生掉落与实际入包"),
        new("F05","农田布局",new[]{"farm.plan","farm.economy","farm.economy_status","farm.execute"},new[]{"player"},"未来跨季用地和全部设施布局", "未来占用图可达性"),
        new("F06","种植排期",new[]{"player.work","work.run"},new[]{"player","companion"},"多轮再投资/机器加工/未知商店排期优化", "地块/种子消耗/成熟日期"),
        new("F07","动物照料",new[]{"player.care","animals.read","player.animal","animal_shop.read","player.buy_animal","companion.assign"},new[]{"player","companion"},"农牧经济规划/特殊区域与完整农牧验收", "动物原生照料/产物状态"),
        new("F08","仓储物流",new[]{"work.run"},new[]{"player","companion"},"全局品质分配与扩容实机验收", "源目标库存守恒"),
        new("F09","补给恢复",new[]{"work.run","player.eat"},new[]{"player"},"低健康撤退与补给预算完善", "水量/体力/物品消耗"),
        new("F10","制作烹饪",new[]{"player.craft","player.cook","goal.requirements","goal.prepare","goal.run"},new[]{"player"},"完整配方解锁/替代材料与厨房覆盖", "原生配方计数+消耗+产物"),
        new("F11","生产加工",new[]{"player.machine","companion.assign","player.interact"},new[]{"player","companion"},"加工原料自动补给、跨日批次与特殊机器条件", "机器在制品/原料/产物"),
        new("F12","买卖出货",new[]{"companion.assign","player.ship","shop.read","player.buy","menu.choose"},new[]{"player","companion"},"移动商人/特殊商品/出售覆盖", "钱/货品/次日收入"),
        new("F13","建造升级",new[]{"player.service","construction.read","player.build","player.upgrade_house","player.place","menu.choose"},new[]{"player"},"特殊建筑完整流程与房屋/建造验收", "原生建筑与升级状态"),
        new("F14","钓鱼",new[]{"player.fish","companion.assign"},new[]{"player","companion"},"鱼种/地点选择、宝箱奖励、补给和原生控杆验收", "Farmer fishCaught；NPC货物不等价"),
        new("F15","探索战斗",new[]{"player.travel","player.combat","player.mine_descend","player.mine_access","work.run","companion.assign"},new[]{"player","companion"},"特殊敌种战术/骷髅矿/火山适配及矿洞闭环实机验收", "原生层数/击杀归属"),
        new("F16","任务交付",new[]{"progress.read","quest_board.read","player.accept_quest","player.social","player.claim_reward","menu.choose"},new[]{"player"},"特殊订单多目标交付/非金币领奖与接取验收", "quest/order 原生完成条件"),
        new("F17","献祭捐赠",new[]{"progress.catalog","player.service","player.donate_museum","player.geodes","player.bundle","menu.choose"},new[]{"player"},"遗失收集包/Joja路线/领奖与献祭捐赠实机验收", "原生提交与解锁"),
        new("F18","社交关系",new[]{"player.social","companion.assign","menu.choose"},new[]{"player","companion"},"家庭/分支剧情完整流程", "原生友情与剧情状态"),
        new("F19","特殊剧情区域",new[]{"menu.read","menu.choose"},new[]{"player"},"节日/后期区域专属适配", "逐事件原生证据"),
        new("F20","菜单过夜",new[]{"player.sleep","strategy.profession","player.collect_reward","menu.read","menu.choose"},new[]{"player"},"特殊夜间选择与职业策略实机验收", "原生保存+实际次日"),
        new("F21","小游戏",Array.Empty<string>(),new[]{"player"},"逐小游戏状态控制器", "正常通关与原生奖励"),
        new("F22","成就目标图",new[]{"progress.catalog","progress.read","progress.roadmap"},new[]{"player"},"成就条件执行图/平台验证", "原生成就集合；平台独立核验"),
        new("F23","双角色调度",new[]{"plan.submit","plan.read","plan.cancel"},new[]{"player","companion"},"完整资源预约与恢复", "角色队列及结果证据"),
        new("F24","全天经营",new[]{"day.read","day.plan"},new[]{"player","companion"},"完整工作候选及恢复预算", "实际日程/收益/有效劳动"),
        new("F25","百科检索",new[]{"knowledge.search","knowledge.get","goal.requirements"},new[]{"player","companion"},"特殊规则与完整条件覆盖", "游戏内容与现场条件"),
        new("F26","记忆与调用",new[]{"memory.search","memory.evidence","usage.read"},new[]{"player","companion"},"运行中跨日/跨存档/取消请求预算验收", "证据ID/时间线/实际usage"),
        new("F27","恢复与接管",new[]{"action.cancel","action.status","agent.pause"},new[]{"player","companion"},"全技能恢复与统一快照", "无重复消耗/晚到指令拒绝")
    };
    public static object Read()=>new{schema_version=1,scope="实现入口不等于完整技能验收；gap不为空则该类尚未齐全",capabilities=All};
}

public sealed record NativeGoalDefinition(string id,string title,string kind,bool? completed,string[] capabilities,string[] dependencies,object requirements,string evidence,string gap,string actor="player");

public sealed partial class ModEntry {
    internal object AgentProgressCatalog(JsonElement args) {
        RefreshFacts(true);
        string kind=AgentToolRegistry.Text(args,"kind");int offset=AgentToolRegistry.Number(args,"offset",0),limit=Math.Clamp(AgentToolRegistry.Number(args,"limit",30),1,80);
        if(offset<0)throw new InvalidOperationException("invalid_offset");
        var rows=new List<NativeGoalDefinition>();var p=Game1.player;
        foreach(var pair in Game1.achievements.OrderBy(x=>x.Key)) {
            var text=pair.Value.Split('^');
            rows.Add(new("achievement:"+pair.Key,text[0],"achievement",p.achievements.Contains(pair.Key),new[]{"F22"},Array.Empty<string>(),new{native_definition=pair.Value,rule=AchievementRules.Native(pair.Key)},"Farmer.achievements", "读取实际计数与部分原生条件；完整前置执行图仍待补"));
        }
        foreach(bool cooking in new[]{false,true}) {
            var definitions=cooking?DataLoader.CookingRecipes(Game1.content):DataLoader.CraftingRecipes(Game1.content);
            foreach(var pair in definitions.OrderBy(x=>x.Key,StringComparer.Ordinal)) {
                var recipe=new CraftingRecipe(pair.Key,cooking);string prefix=cooking?"cook:":"craft:";
                bool known=cooking?p.cookingRecipes.ContainsKey(pair.Key):p.craftingRecipes.ContainsKey(pair.Key);
                // Cooking counters are keyed by the recipe's actual output, not its display name.
                var output=recipe.createItem();
                int count=cooking?p.recipesCooked.TryGetValue(output.ItemId,out var cooked)?cooked:0:p.craftingRecipes.TryGetValue(pair.Key,out var crafted)?crafted:0;
                rows.Add(new(prefix+pair.Key,recipe.DisplayName,cooking?"cooking":"crafting",count>0,new[]{"F08","F10"},known?Array.Empty<string>():new[]{"unlock:"+prefix+pair.Key},
                    new{known,produced=count,output=output.QualifiedItemId,ingredients=recipe.recipeList.Select(i=>new{item=i.Key,count=i.Value}),native_definition=pair.Value},
                    cooking?"Farmer.recipesCooked":"Farmer.craftingRecipes",known?"已接高层制作/烹饪，完整配方实机核验待完成":"解锁规则待补；高层执行要求配方已知"));
            }
        }
        foreach(var g in Facts.Goals.Where(g=>g.Kind!="craft"))rows.Add(new(g.Id,g.Title,g.Kind=="special_order"?"order":g.Kind=="joja"?"route":"quest",g.Complete,new[]{g.Kind=="joja"?"F17":"F16"},Array.Empty<string>(),new{g.Kind,g.Deadline,g.Gold,g.Needs},"原生任务/订单/邮件条件",g.Complete?"":"专属交互执行待接通"));
        foreach(var b in Facts.Bundles)rows.Add(new("bundle:"+b.Id,b.Name,"bundle",b.Complete,new[]{"F08","F17"},new[]{"route:community"},new{b.RequiredSlots,b.CompletedSlots,b.Missing},"原生 bundle 状态",b.Complete?"":"献祭交互执行待接通"));
        rows.Add(new("platform:achievements","平台成就独立核验","scope",null,new[]{"F22"},Array.Empty<string>(),new{platform_connected="not_verified"},"平台成就接口","存档原生成就不作为平台成功证据"));
        rows.AddRange(AchievementRules.PlatformConditions());
        rows.Add(new("scope:perfection","完美度与后期发展","scope",null,new[]{"F19","F22"},Array.Empty<string>(),new{},"原生完美度条件","完美度条目专属解析待补"));
        var filtered=rows.Where(g=>kind.Length==0||g.kind==kind).ToArray();
        return new{schema_version=1,day=Game1.Date.TotalDays,save_id=Game1.uniqueIDForThisGame.ToString(),total=filtered.Length,offset,limit,next_offset=offset+limit<filtered.Length?(int?)(offset+limit):null,
            goals=filtered.Skip(offset).Take(limit),policy=new{routes="路线互斥需逐条件核查，独立路线存档分开统计",repeatable="只枚举当前已接原生任务；重复委托没有有限全部完成终点",unknown="未适配条件明确标 gap，不伪造依赖或完成",actor="进度默认归属 Farmer，NPC 劳动不自动等于原生计数"}};
    }
}
