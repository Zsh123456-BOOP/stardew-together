using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class ModEntry {
    public void AddProgressProject(string id) {
        RefreshFacts(true);var goal=Facts.Goals.FirstOrDefault(g=>g.Id==id && !g.Complete);
        if(goal==null){Notice="这个目标已经变化，请重新选择。";return;}
        if(Data.Projects.Any(p=>p.Kind==id && p.Status=="active")){Notice="已经在共同安排里。";return;}
        Data.Projects.Add(new(){Kind=id,Title=goal.Title,CreatedDay=Facts.Day,Owner=Selected});UpdateProjects();Persist();
        Notice="已经记下目标和缺口；特殊提交由你来，伙伴会分担可执行的准备。";
    }
    public void AssignProject(string id) {
        var p=Data.Projects.FirstOrDefault(p=>p.Id==id);if(p==null)return;
        p.Owner=p.Owner=="player"?Selected:p.Owner==Selected?"together":"player";UpdateProjects();Persist();
    }
    public void SetFarmBudget(int budget,int keep) {
        Data.FarmPolicy.DailyBudget=Math.Clamp(budget,0,100000);Data.FarmPolicy.KeepGold=Math.Clamp(keep,0,1000000);
        UpdateProjects();Persist();Notice="已保存采购预算和最低保留资金；清单以外不会购买。";
    }
    public void AddPlantingArea(string seed) {
        var location=Game1.currentLocation;var tile=Game1.player.TilePoint;
        if(location.Name!="Farm" && !location.IsGreenhouse){Notice="请在农场或温室里站到计划地块左上角。";return;}
        if(!Facts.Seeds.Any(s=>s.Item==seed)){Notice="请从已读取的种子列表选择种子。";return;}
        Data.FarmPolicy.Areas.Add(new(){Location=location.NameOrUniqueName,X=tile.X,Y=tile.Y,Seed=seed});
        UpdateProjects();Persist();Notice="已指定脚下向右、向下的 3×3 地块；只处理空地，不移除已有作物或设施。";
    }
    public void RemoveArea(string id) {Data.FarmPolicy.Areas.RemoveAll(a=>a.Id==id);UpdateProjects();Persist();}
    public void AddShopping(string shop,string item,int count,int ceiling) {
        if(shop is not ("SeedShop" or "AnimalShop" or "Blacksmith"))return;
        Data.FarmPolicy.Shopping.Add(new(){Shop=shop,Item=item,Count=Math.Clamp(count,1,99),MaxUnitPrice=Math.Clamp(ceiling,0,10000)});
        UpdateProjects();Persist();Notice="购物清单已保存。伙伴会核对预算、店员与当日货品。";
    }
    public void StopShopping(string id) {var order=Data.FarmPolicy.Shopping.FirstOrDefault(o=>o.Id==id);if(order!=null)order.Enabled=false;UpdateProjects();Persist();}
    private void UpdateDevelopmentProjects() {
        foreach(var p in Data.Projects.Where(p=>p.Status=="active" && p.Kind!="farm" && !p.Kind.StartsWith("bundle:"))) {
            var goal=Facts.Goals.FirstOrDefault(g=>g.Id==p.Kind);
            if(goal==null){p.Detail="当前存档未再提供此目标，请检查任务是否结束或已过期。";continue;}
            if(goal.Complete){p.Status="fulfilled";p.Remaining=0;p.Detail="游戏状态确认完成。";continue;}
            if(goal.Deadline>=0 && goal.Deadline<Facts.Day){p.Status="expired";p.Detail="任务期限已到；不再为它预留物资。";continue;}
            p.Needs=goal.Needs;p.Remaining=goal.Gold>0?Math.Max(0,goal.Gold-Facts.Money):p.Needs.Sum(n=>n.Missing);
            p.Detail=(goal.Gold>0?$"目标资金 {goal.Gold}，还差 {p.Remaining}。":p.Remaining>0?$"还缺 {p.Remaining} 件材料。":"备料齐全，等待玩家操作。")+goal.PlayerStep;
        }
    }
    private void BuildToday() {
        var nodes=new List<PlanNode>();
        if(Data.FarmHelp) {
            foreach(var entry in new[]{("water",Facts.DryCrops,"作物还需要水"),("harvest",Facts.RipeCrops,"成熟作物可以收取"),("collect",Facts.MachinesReady,"机器成品需要收取")})
                if(entry.Item2>0)nodes.Add(new(){Id="farm:"+entry.Item1,Title=Decision.Labels[entry.Item1],Skill=entry.Item1,Location="Farm",Count=entry.Item2,Reason=entry.Item3});
            foreach(var care in Facts.CareLocations)nodes.Add(new(){Id=care.Location+":"+care.Skill,Title=Decision.Labels[care.Skill],Location=care.Location,Skill=care.Skill,Count=care.Count,Reason=care.Reason});
            foreach(var area in Data.FarmPolicy.Areas.Where(a=>a.Enabled)) {
                var seed=Facts.Seeds.FirstOrDefault(s=>s.Item==a.Seed);var location=Game1.getLocationFromName(area.Location);if(seed==null || location==null)continue;
                int till=0,plant=0;
                for(int y=area.Y;y<area.Y+area.Height;y++)for(int x=area.X;x<area.X+area.Width;x++) {
                    var tile=new Vector2(x,y);if(location.objects.ContainsKey(tile))continue;
                    if(!location.terrainFeatures.TryGetValue(tile,out var feature))till++;
                    else if(feature is HoeDirt dirt && dirt.crop==null)plant++;
                }
                foreach(var step in new[]{("till",till),("plant",plant)})if(step.Item2>0)nodes.Add(new(){Id=area.Id+":"+step.Item1,Title=Decision.Labels[step.Item1]+seed.Name,Location=area.Location,Skill=step.Item1,Count=step.Item2,
                    Reason="已指定种植区；取实际种子，先检查换季能否成熟",DependsOn=step.Item1=="plant" && till>0?new(){area.Id+":till"}:new()});
            }
            foreach(var order in Data.FarmPolicy.Shopping.Where(o=>o.Enabled))nodes.Add(new(){Id="buy:"+order.Id,Title="采购清单："+ItemRegistry.GetDataOrErrorItem(order.Item).DisplayName,
                Location=order.Shop,Skill="buy",Count=order.Count,Reason="已授权购物清单，逐件核对原生价格和预算"});
        }
        foreach(var project in Data.Projects.Where(p=>p.Status=="active" && p.Kind!="farm")) {
            var goal=Facts.Goals.FirstOrDefault(g=>g.Id==project.Kind);
            foreach(var need in project.Needs.Where(n=>n.Missing>0)) {
                var data=ItemRegistry.GetDataOrErrorItem(need.Item);string skill=need.Item is "(O)378" or "(O)380" or "(O)382" or "(O)384"?"mine":data.Category==-4?"fish":"";
                nodes.Add(new(){Id=project.Id+":"+need.Item,Title="为"+project.Title+"准备"+need.Name,Owner=project.Owner,Skill=skill,Count=need.Missing,
                    Reason="共同目标缺口 "+need.Missing,Deadline=goal?.Deadline??-1,Status=skill==""?"needs_player_plan":"ready"});
            }
            nodes.Add(new(){Id=project.Id+":submit",Title=project.Title+" · 玩家交付",Owner="player",Status=project.Remaining==0?"ready":"waiting",
                Reason=goal?.PlayerStep??"材料备齐后由玩家确认交付",DependsOn=project.Needs.Where(n=>n.Missing>0).Select(n=>project.Id+":"+n.Item).ToList()});
        }
        Data.Today=nodes.Take(120).ToList();
    }
    private List<ActivityOption> OptionsFor(string name,Companion p,Situation s,JsonElement actor) {
        var options=LifePlanner.Options(p,s);
        if(p.Social.Mode=="holiday")options.RemoveAll(o=>o.Category=="shared");
        var counts=actor.GetProperty("candidates").EnumerateArray().GroupBy(c=>c.GetProperty("skill").GetString()!).ToDictionary(g=>g.Key,g=>g.Count());
        if(Data.FarmHelp && p.Energy>=25 && p.Social.Mode!="holiday") {
            foreach(string skill in new[]{"feed","tend","plant","till","forage","buy","ship"})if(counts.GetValueOrDefault(skill)>0) {
                string id="work:"+skill;if(p.Life.RetryAfter.GetValueOrDefault(id)>s.Minute)continue;
                options.Add(new(){Id=id,Title="去"+Decision.Labels[skill],Reason="真实目标可达，已在你的经营许可内",Category="shared",Score=skill=="feed"?90:skill=="plant"?83:skill=="till"?78:60,
                    Steps=new(){new(){skill=skill,count=Math.Min(5,counts[skill]),location=s.Location}}});
            }
            foreach(var node in Data.Today.Where(n=>n.Skill!="" && n.Owner!="player" && (n.Owner=="together" || n.Owner==name) && n.Status=="ready" && n.Count>0)) {
                if(node.Location==s.Location || node.Location=="" && counts.GetValueOrDefault(node.Skill)==0)continue;
                if(node.Location!="" && node.Location==s.Location && counts.GetValueOrDefault(node.Skill)==0)continue;
                string id="plan:"+node.Id;if(p.Life.RetryAfter.GetValueOrDefault(id)>s.Minute)continue;
                // Mine/fish deficits are pursued locally only when the corresponding affordance exists.
                if(node.Skill=="fish" && !s.Fishing)continue;
                if(node.Skill=="mine" && s.Mine==0)continue;
                options.Add(new(){Id=id,Title=node.Title,Reason=node.Reason,Category="shared",Score=(s.Pace=="focused"?80:55)+(node.Deadline>=0 && node.Deadline-s.Day<2?20:0),
                    Steps=new(){new(){skill=node.Skill,count=node.Skill=="fish"?1:Math.Min(5,node.Count),location=string.IsNullOrEmpty(node.Location)?s.Location:node.Location}}});
            }
        }
        if(counts.GetValueOrDefault("gift")>0 && p.Social.GiftDay!=s.Day && p.Social.Relationship.Comfort>=40 && p.Social.Mode!="quiet")
            options.Add(new(){Id="gift",Title="留一件小礼物",Reason="随身有未预留的鱼或花，也有你指定的收货箱",Category="personal",Score=100,Steps=new(){new(){skill="gift",location=s.Location}}});
        foreach(var o in options) {
            if(o.Category=="shared")o.Score+=(p.Profile.Temperament.Planning-50)*.15+p.Social.Relationship.Cooperation*.08;
            if(o.Category=="care")o.Score+=(p.Profile.Temperament.Sociability-50)*.2;
            if(o.Id=="mine")o.Score+=(p.Profile.Temperament.RiskTolerance-50)*.4;
            if(p.Life.LastSkill==o.Steps.FirstOrDefault()?.skill)o.Score-=p.Life.Variety*.2;
        }
        return options.OrderByDescending(o=>o.Score).ToList();
    }
}
