using System.Text.Json;
using StardewValley;

namespace Together;
public sealed record ProgressEdge(string id,int count=1,int quality=0,string relation="all");
public sealed class ProgressDependency {
    public string id {get;set;}="";
    public string title {get;set;}="";
    public string kind {get;set;}="";
    public string state {get;set;}="unknown";
    public object evidence {get;set;}=new{};
    public List<ProgressEdge> dependencies {get;set;}=new();
    public List<object> actions {get;set;}=new();
    public List<string> gaps {get;set;}=new();
}
public sealed partial class ModEntry {
    internal object ReadProgressDependencies(JsonElement args) {
        RefreshFacts(true);ReadGoalRecipes();string root=AgentToolRegistry.Text(args,"id");
        int depth=Math.Clamp(AgentToolRegistry.Number(args,"depth",4),1,8),limit=Math.Clamp(AgentToolRegistry.Number(args,"limit",160),10,600);
        var native=ReadNativeGoalRows().GroupBy(n=>n.id).ToDictionary(g=>g.Key,g=>g.First());var nodes=new Dictionary<string,ProgressDependency>();var truncated=new HashSet<string>();
        object Action(string tool,object arguments)=>new{tool,args=arguments};
        void Add(string id,int level) {
            if(nodes.ContainsKey(id))return;
            if(nodes.Count>=limit||level>depth){truncated.Add(id);return;}
            var node=new ProgressDependency{id=id,title=id};nodes[id]=node;
            if(native.TryGetValue(id,out var definition)) {
                node.title=definition.title;node.kind=definition.kind;node.state=definition.completed==true?"complete":"unmet";node.evidence=definition.requirements;
                if(definition.completed==true)return;
                if(id=="scope:perfection"||id.StartsWith("perfection:")) {
                    foreach(string dep in definition.dependencies)node.dependencies.Add(new(dep));
                    node.actions.Add(Action("perfection.read",new{}));
                }
                else if(id.StartsWith("build:")) {
                    var data=DataLoader.Buildings(Game1.content)[id[6..]];
                    foreach(var material in data.BuildMaterials??new())node.dependencies.Add(new(ItemRegistry.QualifyItemId(material.ItemId)??material.ItemId,material.Amount));
                    node.dependencies.Add(new("gold",data.BuildCost));
                    foreach(string dep in definition.dependencies)node.dependencies.Add(new(dep));
                    node.actions.Add(Action("player.service",new{location=data.Builder=="Wizard"?"WizardHouse":"ScienceHouse",service="build"}));
                    node.actions.Add(Action("player.build",new{blueprint=id[6..],budget=data.BuildCost}));
                    node.gaps.Add("需原生建筑条件和实际柜台可用图纸；巫师需要魔法墨水流程解锁");
                }
                else if(id.StartsWith("friendship:")) {
                    node.actions.Add(Action("player.social",new{npc=id[11..],mode="talk"}));
                    node.actions.Add(Action("progress.pursue",new{targets=new[]{id},enabled=true}));
                    node.gaps.Add("礼物价值预算明确配置后按真实喜好和预留选择；生日/次数/人物可达性每次重查");
                }
                else if(id.StartsWith("fish:")) {
                    if(PlayerExecutor.IsTrapFish(id[5..])) {
                        node.dependencies.Add(new("(O)710"));node.dependencies.Add(new("(O)685"));
                        node.actions.Add(Action("crab_pots.read",new{}));node.actions.Add(Action("progress.pursue",new{targets=new[]{id},enabled=true}));
                    }else {node.actions.Add(Action("fishing.options",new{item=id[5..]}));node.actions.Add(Action("work.run",new{goal="fish",item=id[5..],count=1}));}
                    node.gaps.Add("原生捕获概率仍随机，蟹笼等待正常过夜；特殊动态鱼池另行适配");
                }
                else if(id.StartsWith("arcade:")) {
                    node.actions.Add(Action("player.arcade",new{game=id=="arcade:kart"?"kart":"prairie",mode=id=="arcade:kart"?"progress":id=="arcade:deathless"?"deathless":"continue",seconds=1800,attempts=3}));
                    node.gaps.Add("原生通关成功率及无伤仍待集中实机验收，算法不会直接设置完成标记");
                }
                else if(id.StartsWith("achievement:")&&int.TryParse(id[12..],out int achievement)) {
                    var rule=JsonSerializer.SerializeToElement(AchievementRules.Native(achievement));node.evidence=new{native=definition.evidence,rule};
                    if(rule.TryGetProperty("known",out var known)&&known.GetBoolean()) {
                        if(rule.TryGetProperty("details",out var details)&&details.ValueKind==JsonValueKind.Object&&details.TryGetProperty("missing",out var missing)&&missing.ValueKind==JsonValueKind.Array) {
                            foreach(var item in missing.EnumerateArray())if(item.ValueKind==JsonValueKind.String) {
                                string dep=item.GetString()!;if(achievement is 24 or 25 or 26)dep="fish:"+dep;
                                if(dep.StartsWith("craft:")||dep.StartsWith("cook:"))node.dependencies.Add(new(dep,relation:"choose_remaining_distinct"));
                                else if(achievement is 24 or 25 or 26)node.dependencies.Add(new(dep,relation:"choose_remaining_distinct"));
                            }
                        }
                        if(achievement is 18 or 19)node.dependencies.Add(new("house:"+(achievement==18?1:2)));
                        if(achievement is 0 or 1 or 2 or 3 or 4)node.actions.Add(Action("farm.economy",new{priority="income"}));
                        if(achievement is 31 or 32&&rule.TryGetProperty("details",out var cropList)&&cropList.ValueKind==JsonValueKind.Array)foreach(var crop in cropList.EnumerateArray())if(crop.GetProperty("shipped").GetInt32()<crop.GetProperty("required").GetInt32())node.dependencies.Add(new("ship:"+crop.GetProperty("item").GetString(),crop.GetProperty("required").GetInt32()-crop.GetProperty("shipped").GetInt32(),relation:achievement==31?"all":"any"));
                        if(node.dependencies.Count==0)node.gaps.Add("该计数可读；经济/关系/探索等策略执行链仍需选择，不能按计数直接授予成就");
                    }else node.gaps.Add("special_achievement_dependency_adapter_missing");
                }else if(goalRecipes.TryGetValue(id,out var recipe)) {
                    foreach(var input in recipe.Inputs)node.dependencies.Add(new(input.Item,input.Count,input.Quality));
                    if(!recipe.Known)node.dependencies.Add(new("unlock:"+id));
                    if(recipe.Kind=="cook")node.dependencies.Add(new("house:1"));
                    node.actions.Add(Action("goal.create",new{entity=id,count=1,completion=recipe.Kind=="cook"?"cooked":"crafted",request_id="unique_request_required"}));
                    node.actions.Add(Action("goal.run",new{id="returned_shared_goal_id"}));
                    node.gaps.Add("材料数量由 goal.prepare/goal.run 在共享库存账本上整体展开；不能将图中重复物品分别计为可用");
                }else if(definition.kind=="bundle") {
                    var bundle=Facts.Bundles.First(b=>"bundle:"+b.Id==id);
                    foreach(var item in bundle.Missing)node.dependencies.Add(new(item.Item,item.Count,item.Quality,"choose_missing_slots"));
                    node.actions.Add(Action("player.bundle",new{bundle=int.Parse(bundle.Id)}));
                }else if(definition.kind=="island-upgrade") {
                    var parts=id.Split(':');var perch=PlayerExecutor.IslandPerches().First(p=>p.Location.NameOrUniqueName==parts[1]&&p.Perch.upgradeName.Value==parts[2]).Perch;
                    node.dependencies.Add(new("currency:walnuts",perch.requiredNuts.Value));node.actions.Add(Action("player.island_upgrade",new{location=parts[1],upgrade=parts[2],budget_nuts=perch.requiredNuts.Value}));
                    if(!perch.IsAvailable())node.gaps.Add("未满足原生前置邮件："+perch.requiredMail.Value);
                }else if(id=="region:caldera") {
                    foreach(var prerequisite in definition.dependencies)node.dependencies.Add(new(prerequisite));
                    node.actions.Add(Action("work.run",new{goal="volcano_trip",target_level=10,travel_budget=1000}));node.gaps.Add("行动开始前需实际票价、补给与装备，门槛未满足不能直接跳转地图");
                }else if(definition.kind=="mastery") {
                    int skill=int.Parse(id[8..]);node.actions.Add(Action("player.mastery",new{skill}));node.gaps.Add("原生五技能与精通经验达标后才能领取；经验通过实际劳动获得");
                }else if(definition.kind=="book") {
                    string item=id[5..];node.dependencies.Add(new(item));node.actions.Add(Action("inventory.read",new{}));node.actions.Add(Action("player.read_book",new{item,slot="observed_book_slot"}));
                }else if(definition.kind=="house") {
                    int targetLevel=int.Parse(id[6..]);node.state=Game1.player.daysUntilHouseUpgrade.Value>=0?"waiting_construction":"unmet";
                    if(targetLevel>1)node.dependencies.Add(new("house:"+(targetLevel-1)));
                    int cost=targetLevel==1?10000:targetLevel==2?65000:100000;node.dependencies.Add(new("gold",cost));
                    if(targetLevel<3)node.dependencies.Add(new(targetLevel==1?"(O)388":"(O)709",targetLevel==1?450:100));
                    node.actions.Add(Action("player.upgrade_house",new{budget=cost,keep_gold=500}));
                }else if(definition.kind=="boat") {
                    var part=id[5..];var need=part switch{"hull"=>("(O)709",200),"anchor"=>("(O)337",5),_=>("(O)787",5)};
                    node.dependencies.Add(new(need.Item1,need.Item2));node.actions.Add(Action("player.repair_boat",new{part}));
                    node.gaps.Add("需先通过原生剧情开放船坞；捐料登记后施工仍需过夜");
                }else if(definition.kind=="shipping") {
                    string item=id[5..];node.dependencies.Add(new(item));node.actions.Add(Action("player.ship_items",new{items=new[]{new{item,count=1}}}));
                }else if(id.StartsWith("joja:")) {
                    var project=Facts.Goals.First(g=>g.Id==id);node.dependencies.Add(new("gold",project.Gold));
                    node.actions.Add(Action("player.joja",new{route="joja",mode="project",project=id[5..],budget=project.Gold,keep_gold=500}));
                }else if(definition.kind is "quest" or "order") {
                    var quest=Facts.Goals.FirstOrDefault(g=>g.Id==id);
                    if(quest!=null)foreach(var requirement in quest.Needs)node.dependencies.Add(new(requirement.Item,requirement.Count,requirement.Quality));
                    node.actions.Add(Action("progress.read",new{}));node.gaps.Add("交付目标/期限/原生计数分别核验；仅备齐物品不代表任务完成");
                }else node.gaps.Add(definition.gap);
            }else if(id.StartsWith("house:")&&int.TryParse(id[6..],out int targetLevel)&&targetLevel is >=1 and <=3) {
                node.kind="house_upgrade";node.title="房屋升级 "+targetLevel;int currentLevel=Game1.player.HouseUpgradeLevel;
                node.state=currentLevel>=targetLevel?"complete":Game1.player.daysUntilHouseUpgrade.Value>=0?"waiting_construction":"unmet";
                node.evidence=new{level=currentLevel,days=Game1.player.daysUntilHouseUpgrade.Value};
                if(currentLevel<targetLevel) {
                    if(targetLevel>1)node.dependencies.Add(new("house:"+(targetLevel-1)));
                    int cost=targetLevel==1?10000:targetLevel==2?65000:100000;node.dependencies.Add(new("gold",cost));
                    if(targetLevel<3)node.dependencies.Add(new(targetLevel==1?"(O)388":"(O)709",targetLevel==1?450:100));
                    node.actions.Add(Action("player.upgrade_house",new{budget=cost,keep_gold=500}));
                }
            }else if(id.StartsWith("unlock:craft:")||id.StartsWith("unlock:cook:")) {
                node.kind="recipe_unlock";string recipeId=id[7..];bool cook=recipeId.StartsWith("cook:");string key=recipeId[(cook?5:6)..];
                var recipes=cook?DataLoader.CookingRecipes(Game1.content):DataLoader.CraftingRecipes(Game1.content);var fields=recipes.GetValueOrDefault(key,"").Split('/');string raw=fields.Length>(cook?3:4)?fields[cook?3:4]:"";
                bool learned=cook?Game1.player.cookingRecipes.ContainsKey(key):Game1.player.craftingRecipes.ContainsKey(key);node.state=learned?"complete":"locked";node.evidence=new{raw,learned};
                if(!learned) {
                    var parts=raw.Split(' ',StringSplitOptions.RemoveEmptyEntries);if(parts.Length==3&&parts[0]=="s")parts=parts.Skip(1).ToArray();
                    if(parts.Length==2&&int.TryParse(parts[1],out int skillLevel)&&new[]{"farming","fishing","mining","foraging","combat"}.Contains(parts[0].ToLowerInvariant()))node.dependencies.Add(new("skill:"+parts[0].ToLowerInvariant(),skillLevel));
                    else if(parts.Length==3&&parts[0]=="f"&&int.TryParse(parts[2],out int hearts))node.dependencies.Add(new("friendship:"+parts[1],hearts*250));
                    else node.gaps.Add("shop_event_or_special_recipe_unlock_requires_native_source_adapter");
                    if(cook)node.actions.Add(Action("player.watch_tv",new{channel="cooking"}));
                    node.actions.Add(Action("player.read_mail",new{}));
                    node.gaps.Add("满足等级或关系条件后仍需原生升级/邮件/菜单真正授予配方");
                }
            }else if(id=="currency:walnuts") {node.kind="currency";node.state="observed";node.evidence=new{available=Game1.netWorldState.Value.GoldenWalnuts,found=Game1.netWorldState.Value.GoldenWalnutsFound};}
            else if(id=="gold") {node.kind="currency";node.state="observed";node.evidence=new{amount=Game1.player.Money,pending_shipping_not_spendable=true};}
            else if(id.StartsWith("skill:")) {
                string skill=id[6..];int value=skill switch{"farming"=>Game1.player.FarmingLevel,"fishing"=>Game1.player.FishingLevel,"mining"=>Game1.player.MiningLevel,"foraging"=>Game1.player.ForagingLevel,"combat"=>Game1.player.CombatLevel,_=>-1};
                node.kind="skill";node.state=value<0?"unknown":"observed";node.evidence=new{level=value};
            }else if(id.StartsWith("friendship:")) {node.kind="friendship";node.state="observed";node.evidence=new{points=Game1.player.friendshipData.TryGetValue(id[11..],out var f)?f.Points:0};node.actions.Add(Action("player.social",new{npc=id[11..],mode="talk"}));}
            else if(id.StartsWith("catch:")) {
                string item=id[6..];node.kind="catch";node.state=Game1.player.fishCaught.ContainsKey(item)?"complete":"unmet";node.evidence=new{native="Farmer.fishCaught",item};
                node.actions.Add(Action("knowledge.get",new{id=item}));node.gaps.Add("指定鱼种的地点/钓点/时段与补给执行策略待全覆盖；拥有鱼不替代亲自捕获记录");
            }else if(id.StartsWith("ship:")) {
                string item=id[5..];node.kind="shipping";node.state="observed";node.evidence=new{shipped=Game1.player.basicShipped.GetValueOrDefault(item.Replace("(O)",""))};node.dependencies.Add(new(item));node.actions.Add(Action("player.ship_items",new{items=new[]{new{item,count=1}}}));
            }else {
                bool category=int.TryParse(id.Replace("(O)",""),out int cat)&&cat<0;var itemDefinition=ItemRegistry.GetData(id);
                if(category||itemDefinition!=null) {
                    node.kind=category?"ingredient_category":"item";node.title=category?id:itemDefinition!.DisplayName;node.state="observed";
                    node.evidence=new{stock=Facts.Stock.Where(s=>s.Item==id||category&&s.Category==cat).Select(s=>new{s.Item,s.Count,s.Quality}),note="库存节点的数量/品质需与入边共同核算"};
                    var routes=goalRecipes.Values.Where(r=>r.Item==id).Take(8).ToArray();foreach(var recipe in routes)node.dependencies.Add(new(recipe.Id,relation:"alternative_source"));
                    node.actions.Add(Action("knowledge.get",new{id}));
                    if(routes.Length==0)node.gaps.Add("按百科与当前地图选择种植/采集/采购/钓鱼获得路线");
                }else if(goalRecipes.TryGetValue(id,out var processing)) {
                    node.kind="processing";node.state=processing.Known?"available":"locked";node.title=processing.Name;node.evidence=new{processing.Facility,processing.Output};
                    foreach(var input in processing.Inputs)node.dependencies.Add(new(input.Item,input.Count,input.Quality));node.dependencies.Add(new(processing.Facility));
                    node.actions.Add(Action("goal.create",new{entity=id,count=1,completion="owned",request_id="unique_request_required"}));
                }else {node.gaps.Add("unknown_dependency_or_unimplemented_route");}
            }
            foreach(var edge in node.dependencies.ToArray())Add(edge.id,level+1);
        }
        if(root.Length==0)throw new InvalidOperationException("progress_target_id_required");Add(root,0);
        return new{schema_version=1,root,day=Game1.Date.TotalDays,nodes=nodes.Values,truncated,policy="只读依赖解释图；choose/alternative 是可选分支，不能全都执行。库存不重复分配；action 参数占位符需用实际返回值替换。循环配方和预算由 goal.prepare 的整体账本处理。图覆盖不等于全通关执行已齐全。"};
    }
}
