using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private DateTime progressCampaignAt;
    internal object ConfigureProgressCampaign(JsonElement args) {
        RefreshFacts(true);var campaign=Data.Autoplay.Campaign;var known=ReadNativeGoalRows().Select(r=>r.id).ToHashSet();
        if(args.TryGetProperty("targets",out var raw)) {
            var targets=JsonSerializer.Deserialize<List<string>>(raw.GetRawText());
            if(targets==null||targets.Count is <1 or >16||targets.Distinct().Count()!=targets.Count||targets.Any(id=>!known.Contains(id)))throw new InvalidOperationException("one_to_sixteen_observed_progress_targets_required");
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
        return new{campaign,note="持续追踪原生目标；当前自动衔接已解锁制作/烹饪成就，其他目标保留具体阻碍供模型规划。不把子任务成功记作原生成就完成。"};
    }
    private void PausePursuitChild(ProgressPursuit pursuit) {
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
        foreach(var pursuit in Data.Autoplay.Campaign.Targets) {
            if(!native.TryGetValue(pursuit.Target,out var target)){PursuitState(pursuit,"blocked","native_target_no_longer_available");continue;}
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
