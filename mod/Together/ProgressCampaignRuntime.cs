using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private DateTime progressCampaignAt;
    internal object ConfigureProgressCampaign(JsonElement args) {
        RefreshFacts(true);var campaign=Data.Autoplay.Campaign;var known=ReadNativeGoalRows().Select(r=>r.id).ToHashSet();
        int budget=AgentToolRegistry.Number(args,"budget_per_day",campaign.BudgetPerDay),keep=AgentToolRegistry.Number(args,"keep_gold",campaign.KeepGold);
        string route=AgentToolRegistry.Text(args,"route",campaign.Route);
        if(budget is <0 or >10000000||keep<0||route is not ("" or "community" or "joja"))throw new InvalidOperationException("invalid_progress_budget_or_route");
        if(route=="community"&&Game1.player.hasOrWillReceiveMail("JojaMember")||route=="joja"&&Game1.player.mailReceived.Contains("ccIsComplete"))throw new InvalidOperationException("progress_route_conflicts_with_native_save");
        campaign.BudgetPerDay=budget;campaign.KeepGold=keep;campaign.Route=route;
        if(args.TryGetProperty("targets",out var raw)) {
            var targets=JsonSerializer.Deserialize<List<string>>(raw.GetRawText());
            if(targets==null||targets.Count is <1 or >128||targets.Distinct().Count()!=targets.Count||targets.Any(id=>!known.Contains(id)))throw new InvalidOperationException("one_to_128_observed_progress_targets_required");
            foreach(var prior in campaign.Targets.Where(p=>!targets.Contains(p.Target)))PausePursuitChild(prior);
            campaign.Targets=targets.Select(id=>campaign.Targets.FirstOrDefault(p=>p.Target==id)??new(){Target=id}).ToList();
        }
        if(args.TryGetProperty("enabled",out var enabled)) {
            if(enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("campaign_enabled_must_be_boolean");
            campaign.Enabled=enabled.GetBoolean();
            foreach(var target in campaign.Targets) {
                if(!campaign.Enabled)PausePursuitChild(target);
                else if(target.State is "blocked" or "waiting"){target.State="pending";target.Fingerprint="";target.Reason="";}
            }
        }
        progressCampaignAt=DateTime.MinValue;
        return new{campaign,note="按预算持续衔接已绑定的原生目标，备料与购买不代表成就完成；任务或材料条件改变后继续。未绑定的特殊条件明确报告。"};
    }
    private void PausePursuitChild(ProgressPursuit pursuit) {
        Data.Autoplay.Schedule.CancelPending(Data.Autoplay.Schedule.Tasks.Where(t=>pursuit.Tasks.Contains(t.spec.id)&&!t.Terminal&&t.state!="running").Select(t=>t.spec.id).ToArray());
        var goal=Data.SharedGoals.FirstOrDefault(g=>g.Id==pursuit.ChildGoal);
        if(goal is {Status:"active"})AgentGoalRun(JsonSerializer.SerializeToElement(new{id=goal.Id,mode="pause"}));
    }
    private void PursuitState(ProgressPursuit pursuit,string state,string reason) {
        bool changed=pursuit.State!=state||pursuit.Reason!=reason;pursuit.State=state;pursuit.Reason=reason;
        if(!changed)return;
        Data.Autoplay.Record("progress_pursuit",AgentJson.Encode(new{pursuit.Target,state,reason,pursuit.ChildGoal}));
        if(state is "blocked" or "complete" or "waiting")WakeAgent("progress_"+state+":"+pursuit.Target);
    }
    private void TickProgressCampaign() {
        if(!AutoplayRunning||!Data.Autoplay.Campaign.Enabled||DateTime.UtcNow<progressCampaignAt)return;progressCampaignAt=DateTime.UtcNow.AddSeconds(3);
        if(playerExecutor.Busy||Game1.activeClickableMenu!=null||Game1.eventUp||Game1.fadeToBlack||Game1.locationRequest!=null||Game1.timeOfDay>=2100||Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.actor=="player"&&!t.Terminal))return;
        RefreshFacts(true);var native=ReadNativeGoalRows().GroupBy(r=>r.id).ToDictionary(g=>g.Key,g=>g.First());
        var policy=Data.Autoplay.Campaign;if(policy.BudgetDay!=Game1.Date.TotalDays){policy.BudgetDay=Game1.Date.TotalDays;policy.ReservedGold=0;}
        foreach(var pursuit in Data.Autoplay.Campaign.Targets) {
            if(!ReconcilePursuitTasks(pursuit))continue;
            if(!native.TryGetValue(pursuit.Target,out var target)) {
                if(pursuit.CompletionObserved&&pursuit.State!="blocked"&&pursuit.Target.StartsWith("quest:"))PursuitState(pursuit,"complete","已观察原生完成，领奖后从活动日志移除");
                else PursuitState(pursuit,"blocked","native_target_no_longer_available");continue;
            }
            pursuit.CompletionObserved=target.completed==true;
            if(TryClaimPursuitReward(pursuit)){return;}
            if(target.completed==true){PausePursuitChild(pursuit);PursuitState(pursuit,"complete",target.evidence);continue;}
            if(pursuit.State=="complete")PursuitState(pursuit,"pending","native_progress_changed_reobserve");
            if(pursuit.State=="blocked")continue;
            var current=Data.SharedGoals.FirstOrDefault(g=>g.Id==pursuit.ChildGoal);
            if(current is {Status:"active"}) {
                if(!current.AutoExecute&&current.AutoBlockedReason.Length>0){PursuitState(pursuit,"blocked",current.AutoBlockedReason);continue;}
                if(!current.AutoExecute)AgentGoalRun(JsonSerializer.SerializeToElement(new{id=current.Id}));
                if(current.AutoBlockedReason.Length>0){PursuitState(pursuit,"waiting",current.AutoBlockedReason);continue;}
                PursuitState(pursuit,"running","执行当前依赖，普通子动作由队列续接");return;
            }
            if(current is {Status:"paused"}){PursuitState(pursuit,"blocked","dependent_shared_goal_paused");continue;}
            try {if(TryAdvanceNativePursuit(pursuit,target)) {if(pursuit.State=="running")return;continue;}}
            catch(Exception e){PursuitState(pursuit,"blocked",e is InvalidOperationException?e.Message:e.GetType().Name);continue;}
            IEnumerable<string>? candidates=null;
            if(pursuit.Target.StartsWith("craft:")||pursuit.Target.StartsWith("cook:"))candidates=new[]{pursuit.Target};
            else if(pursuit.Target.StartsWith("achievement:")&&int.TryParse(pursuit.Target[12..],out int id)&&id is 15 or 16 or 17 or 20 or 21 or 22) {
                var rule=JsonSerializer.SerializeToElement(AchievementRules.Native(id));
                if(rule.GetProperty("condition_satisfied").GetBoolean()){PursuitState(pursuit,"waiting","原生统计已达标，等待游戏原生成就登记；不直接写完成字段");continue;}
                candidates=rule.GetProperty("details").GetProperty("missing").EnumerateArray().Select(v=>v.GetString()!).ToArray();
            }
            if(candidates==null){PursuitState(pursuit,"blocked","此目标尚需模型结合 progress.dependencies 选择路线/交互，未绑定完整自动执行链");continue;}
            string fingerprint=FailureKnowledge.Hash(AgentJson.Encode(new{day=Game1.Date.TotalDays,known=goalRecipes.Values.Where(r=>r.Known).Select(r=>r.Id),stock=Facts.Stock.Select(s=>new{s.Item,s.Count,s.Quality})}));
            if(pursuit.State=="waiting"&&pursuit.Fingerprint==fingerprint)continue;pursuit.Fingerprint=fingerprint;
            var options=new List<(GoalRecipe Recipe,int Cost)>();
            foreach(string candidate in candidates) {
                if(!goalRecipes.TryGetValue(candidate,out var recipe)||!recipe.Known)continue;
                var preview=new SharedGoal{Entity=candidate,Item=recipe.Item,Completion=recipe.Kind=="cook"?"cooked":"crafted",BaselineCrafts=0};
                var ledger=new GoalLedger(Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category}));
                foreach(var reserve in AllReservations().OrderByDescending(r=>r.Quality))ledger.Take(reserve.Item,reserve.Count,reserve.Quality);
                GoalPlanner.Rebuild(preview,goalRecipes,ledger,Game1.Date.TotalDays,item=>item);
                if(preview.Nodes.Any(n=>n.Status is "locked" or "blocked"))continue;
                var missing=preview.Nodes.Where(n=>n.Kind=="gather"&&n.ToPrepare>0).ToArray();
                bool obtainable=missing.All(n=>n.Quality==0&&(n.Item is "(O)388" or "(O)390" or "(O)771"||n.Item=="(O)709"&&FindGoalResourceLocation(n.Item,"hardwood")!=null||ResourceRules.Nodes.Values.Contains(n.Item)&&FindGoalResourceLocation(n.Item,"resource")!=null));
                if(!obtainable)continue;
                options.Add((recipe,missing.Sum(n=>n.ToPrepare)+preview.Nodes.Count));
            }
            var selected=options.OrderBy(o=>o.Cost).ThenBy(o=>o.Recipe.Id,StringComparer.Ordinal).FirstOrDefault().Recipe;
            if(selected==null){PursuitState(pursuit,"waiting","没有当前可自主完成的未制作配方；需要先解锁配方、厨房或补齐特殊材料路线");continue;}
            string request="progress-"+FailureKnowledge.Hash(pursuit.Target)[..12]+"-"+(++pursuit.Revision);
            try {
                var goal=(SharedGoal)AgentGoalCreate(JsonSerializer.SerializeToElement(new{request_id=request,entity=selected.Id,count=1,completion=selected.Kind=="cook"?"cooked":"crafted"}));
                pursuit.ChildGoal=goal.Id;AgentGoalRun(JsonSerializer.SerializeToElement(new{id=goal.Id}));PursuitState(pursuit,"running","按现有物资与可执行采集路线选择尚未完成的原生配方："+selected.Name);return;
            }catch(Exception error){PursuitState(pursuit,"blocked",error is InvalidOperationException?error.Message:error.GetType().Name);}
        }
    }
}
