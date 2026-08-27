using System.Text.Json;
using StardewValley;
using StardewModdingAPI;
using StardewValley.Locations;
using StardewValley.Menus;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class ModEntry {
    private DateTime businessAt;
    private int BusinessMinute=>Game1.Date.TotalDays*1440+DailyBudget.Minutes(Game1.timeOfDay);
    internal object ConfigureBusiness(JsonElement args) {
        var b=Data.Business;
        int budget=AgentToolRegistry.Number(args,"budget_per_day",b.DailyBudget),keep=AgentToolRegistry.Number(args,"keep_gold",b.KeepGold),animals=AgentToolRegistry.Number(args,"max_animals",b.MaxAnimals),machines=AgentToolRegistry.Number(args,"max_machines",b.MaxMachines),feed=AgentToolRegistry.Number(args,"feed_days",b.FeedDays);
        if(budget is <0 or >10000000||keep is <0 or >10000000||animals is <0 or >96||machines is <0 or >200||feed is <2 or >28)throw new InvalidOperationException("invalid_business_policy");
        foreach(string key in new[]{"enabled","expand"})if(args.TryGetProperty(key,out var flag)&&flag.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("business_boolean_required");
        b.DailyBudget=budget;b.KeepGold=keep;b.MaxAnimals=animals;b.MaxMachines=machines;b.FeedDays=feed;
        if(args.TryGetProperty("expand",out var expand))b.Expand=expand.GetBoolean();
        if(args.TryGetProperty("enabled",out var enabled))b.Enabled=enabled.GetBoolean();
        if(b.Enabled) {
            var farm=Data.FarmInvestment;farm.Enabled=true;farm.BudgetPerDay=budget;farm.KeepGold=keep;
            string actor=World().GetProperty("actors").EnumerateArray().Select(a=>a.GetProperty("id").GetString()).FirstOrDefault()??"player";
            ConfigureDailyRoutine(JsonSerializer.SerializeToElement(new{enabled=true,assignments=new Dictionary<string,string>{{"clear_dead","player"},{"harvest",actor},{"water",actor},{"pet",actor},{"mail","player"},{"cooking_tv","player"},{"animal_collect","player"},{"milk","player"},{"shear","player"}}}));
        }
        else {
            Data.FarmInvestment.Enabled=false;
            Data.Autoplay.Schedule.CancelPending(b.Tasks.Where(id=>Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.id==id&&t.state!="running")).ToArray());
            var goal=Data.SharedGoals.FirstOrDefault(g=>g.Id==b.ChildGoal);if(goal is {Status:"active"})AgentGoalRun(JsonSerializer.SerializeToElement(new{id=goal.Id,mode="pause"}));
        }
        businessAt=DateTime.MinValue;Data.Autoplay.Record("business_policy",AgentJson.Encode(b));return new{policy=b,note="先维护，再生产与销售；投资遵循预算与工作量，模型可查询建议并调整方向。关闭不撤销已发生消费或中断原生保存。"};
    }
    internal object ReadBusiness(JsonElement args) {RefreshFacts(true);return new{ledger=ReadBusinessLedger(),options=BusinessDevelopmentOptions().ToArray(),note="选项估值来自当前原生数据与可见供给；不是保证产量或全局最优。查看真实执行及等待原因后调整政策。"};}
    private bool QueueBusiness(string id,IEnumerable<(string Tool,object Args)> actions,string reason,int cost=0) {
        var b=Data.Business;if(b.RetryAfter.GetValueOrDefault(id)>BusinessMinute)return false;
        int used=b.ReservedToday+Data.FarmInvestment.ReservedToday;
        if(cost>BusinessMath.Spendable(Game1.player.Money,b.KeepGold,b.DailyBudget,used))return false;
        var tasks=actions.Select(a=>new AgentTaskSpec{id="business-"+Guid.NewGuid().ToString("N"),actor=a.Tool=="work.run"?AgentToolRegistry.Text(JsonSerializer.SerializeToElement(a.Args),"actor_id","player"):"player",tool=a.Tool,args=JsonSerializer.SerializeToElement(a.Args),purpose=reason,day=Game1.Date.TotalDays,deadline=2200}).ToList();
        if(tasks.Count==0)return false;
        if(Data.Autoplay.Schedule.Tasks.Count+tasks.Count>180)Data.Autoplay.Schedule.Archive();
        Data.Autoplay.Schedule.Submit("business-"+Guid.NewGuid().ToString("N"),Data.Autoplay.Schedule.Revision,tasks,Game1.Date.TotalDays);
        b.ReservedToday+=cost;b.ActiveReservation=cost;b.Activity=id;b.Reason=reason;b.Tasks=tasks.Select(t=>t.id).ToList();
        Data.Autoplay.Record("business_dispatch",AgentJson.Encode(new{id,reason,cost,tasks,cash=Game1.player.Money,b.ReservedToday,seed_reserved=Data.FarmInvestment.ReservedToday}));return true;
    }
    private bool BusinessMaterials(Dictionary<string,int> needs,List<(string Tool,object Args)> actions,bool acquire=true) {
        foreach(var need in needs) {
            int available=Game1.player.Items.Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer())).Where(i=>i?.QualifiedItemId==need.Key).Sum(i=>i.Stack);
            if(available<need.Value) {
                actions.Clear();if(!acquire)return false;
                foreach(var actor in World().GetProperty("actors").EnumerateArray()) {
                    string id=actor.GetProperty("id").GetString()!;
                    if(!WorkActorBusy(id)&&actor.GetProperty("cargo").EnumerateObject().Any(c=>c.Name.StartsWith(need.Key+":")&&c.Value.GetInt32()>0)) {
                        QueueBusiness("transport:"+need.Key,new[]{("work.run",(object)new{actor_id=id,goal="store"})},"伙伴将实际材料运回共享仓库后再消费");return false;
                    }
                }
                var b=Data.Business;
                var existing=Data.SharedGoals.FirstOrDefault(g=>g.Id==b.ChildGoal&&g.Status=="active");
                if(existing!=null)return false;
                if(CanPreparePursuitItem(need.Key,need.Value)) {
                    var goal=(SharedGoal)AgentGoalCreate(JsonSerializer.SerializeToElement(new{request_id="business-"+(++b.Revision),entity=need.Key,count=Math.Min(999,need.Value),completion="owned",allow_new_facilities=true}));
                    b.ChildGoal=goal.Id;AgentGoalRun(JsonSerializer.SerializeToElement(new{id=goal.Id}));b.Reason="准备经营依赖："+need.Key;return false;
                }
                if(PrepareBusinessTreeSource(need.Key,need.Value))return false;
                if(Data.SharedGoals.Any(g=>g.Id==b.ChildGoal&&g.Status=="active"))return false;
                var preview=new SharedGoal{AllowNewFacilities=true,Entity=need.Key,Item=need.Key,Count=need.Value};
                GoalPlanner.Rebuild(preview,goalRecipes,new GoalLedger(Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category})),Game1.Date.TotalDays,id=>id);
                if(!preview.Nodes.Any(n=>n.Status=="blocked")&&preview.Nodes.Any(n=>n.Kind=="gather"&&n.ToPrepare>0&&(n.Item is "(O)388" or "(O)390" or "(O)771"||ResourceRules.Nodes.Values.Contains(n.Item)))) {
                    var partial=(SharedGoal)AgentGoalCreate(JsonSerializer.SerializeToElement(new{request_id="business-"+(++b.Revision),entity=need.Key,count=Math.Min(999,need.Value),completion="owned",allow_new_facilities=true}));
                    b.ChildGoal=partial.Id;AgentGoalRun(JsonSerializer.SerializeToElement(new{id=partial.Id}));b.Reason="先准备可达的生产前置；未知配方仍须原生解锁";return false;
                }
                int budget=BusinessMath.Spendable(Game1.player.Money,b.KeepGold,b.DailyBudget,b.ReservedToday+Data.FarmInvestment.ReservedToday);
                var source=PlayerExecutor.ShopSources(need.Key).FirstOrDefault(s=>PlayerExecutor.NextExit(Game1.currentLocation,s.Location)!=null||Game1.currentLocation.NameOrUniqueName==s.Location);
                if(source.Shop!=null&&budget>0&&Game1.timeOfDay is >=900 and <1600) {
                    int count=Math.Min(999,need.Value-available),unit=budget/count;
                    if(unit>0)QueueBusiness("procure:"+need.Key,new[]{("player.procure",(object)new{location=source.Location,shop=source.Shop,item=need.Key,count,recipe=false,max_unit_price=unit,budget,keep_gold=b.KeepGold})},"准备缺少的经营物资；按预算现场核价采购",budget);
                }
                return false;
            }
            int bag=Game1.player.Items.Where(i=>i?.QualifiedItemId==need.Key).Sum(i=>i.Stack);
            if(bag<need.Value)actions.Add(("work.run",new{goal="withdraw",item=need.Key,count=need.Value-bag}));
        }
        return true;
    }
    private void TickFarmBusiness() {
        var b=Data.Business;
        if(!AutoplayRunning||!b.Enabled||DateTime.UtcNow<businessAt)return;businessAt=DateTime.UtcNow.AddSeconds(2);
        // Regular Together life updates yield to Autoplay. Restore previously
        // agreed companions here too, including ownership after save creation.
        if(Context.IsPlayerFree&&Game1.timeOfDay<1200&&DateTime.UtcNow>=rejoinAt) {
            rejoinAt=DateTime.UtcNow.AddSeconds(10);
            foreach(var person in Data.People.Where(p=>p.Value.DailyCompanion==true))api?.ResumeDay(person.Key);
        }
        RefreshFacts(true);
        if(b.Day!=Game1.Date.TotalDays) {
            if(b.Day>=0)Data.Autoplay.Record("business_day_close",AgentJson.Encode(new{day=b.Day,opening_cash=b.OpeningCash,current_cash=Game1.player.Money,cash_delta=Game1.player.Money-b.OpeningCash,earned_delta=(long)Game1.player.totalMoneyEarned-b.OpeningEarned,note="含原生过夜结算，现金差不是扣除所有机会成本后的利润"}));
            b.Day=Game1.Date.TotalDays;b.OpeningCash=Game1.player.Money;b.OpeningEarned=Game1.player.totalMoneyEarned;b.ReservedToday=0;
            foreach(var key in b.RetryAfter.Where(kv=>kv.Value<BusinessMinute).Select(kv=>kv.Key).ToArray())b.RetryAfter.Remove(key);
        }
        if(b.Tasks.Count>0) {
            var tasks=b.Tasks.Select(id=>Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==id)).ToArray();
            if(tasks.Any(t=>t?.state=="needs_review")) {b.Reason="经营任务中断，需核对实际结果后续接";WakeAgent("business_interrupted_review");return;}
            if(tasks.Any(t=>t!=null&&!t.Terminal))return;
            // Release unused purchase allowance only from complete native receipts.
            // Missing or interrupted receipts retain the conservative reservation.
            if(b.ActiveReservation>0&&tasks.All(t=>t!=null&&!string.IsNullOrEmpty(t.receipt))) {
                int spent=0;bool verified=true;
                foreach(var task in tasks) {
                    using var receipt=JsonDocument.Parse(task!.receipt!);var root=receipt.RootElement;
                    if(root.TryGetProperty("before",out var before)&&root.TryGetProperty("after",out var after)&&before.TryGetProperty("money",out var oldCash)&&after.TryGetProperty("money",out var newCash))spent+=Math.Max(0,oldCash.GetInt32()-newCash.GetInt32());
                    else verified=false;
                }
                if(verified)b.ReservedToday=BusinessMath.Settle(b.ReservedToday,b.ActiveReservation,spent);
            }
            b.ActiveReservation=0;
            var bad=tasks.FirstOrDefault(t=>t==null||t.state!="succeeded");
            if(tasks.Any(t=>t==null||t.state!="succeeded")) {
                b.Reason=bad?.error??"business_receipt_missing";b.RetryAfter[b.Activity]=BusinessMinute+120;
                Data.Autoplay.Record("business_batch_failed",AgentJson.Encode(new{b.Activity,b.Reason,tasks}));
            } else Data.Autoplay.Record("business_batch_complete",AgentJson.Encode(new{b.Activity,tasks}));
            if(b.Activity=="storage")b.RetryAfter[b.Activity]=BusinessMinute+30;
            b.Tasks.Clear();
        }
        if(playerExecutor.Busy||Game1.activeClickableMenu!=null||Game1.eventUp||Game1.fadeToBlack||Game1.locationRequest!=null||!Game1.player.CanMove||Game1.timeOfDay>=2130||Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.actor=="player"&&!t.Terminal))return;
        try {
            Data.FarmInvestment.BudgetPerDay=Math.Max(Data.FarmInvestment.ReservedToday,b.DailyBudget-b.ReservedToday);
            Data.FarmInvestment.KeepGold=b.KeepGold;
            if(Game1.player.freeSpotsInInventory()<2&&Game1.player.Items.Any(i=>i is StardewValley.Object))if(QueueBusiness("storage",new[]{("work.run",(object)new{goal="store"})},"生产前整理背包，保留工具和补给"))return;
            int animals=Game1.getFarm().getAllFarmAnimals().Count();
            if(animals>0) {
                foreach(var tool in new[]{("(T)MilkPail",typeof(StardewValley.Tools.MilkPail)),("(T)Shears",typeof(StardewValley.Tools.Shears))}) {
                    if(Game1.player.Items.Any(i=>i!=null&&tool.Item2.IsInstanceOfType(i)))continue;
                    var prototype=ItemRegistry.Create(tool.Item1) as Tool;if(prototype==null||!Game1.getFarm().getAllFarmAnimals().Any(a=>a.CanGetProduceWithTool(prototype)))continue;
                    var shop=PlayerExecutor.ShopSources(tool.Item1).FirstOrDefault();int budget=BusinessMath.Spendable(Game1.player.Money,b.KeepGold,b.DailyBudget,b.ReservedToday+Data.FarmInvestment.ReservedToday);
                    if(shop.Shop!=null&&budget>0&&Game1.timeOfDay is >=900 and <1600&&QueueBusiness("care_tool:"+tool.Item1,new[]{("player.procure",(object)new{location=shop.Location,shop=shop.Shop,item=tool.Item1,count=1,recipe=false,max_unit_price=budget,budget,keep_gold=b.KeepGold})},"为已有动物补齐实际采收工具",budget))return;
                }
                int availableHay=Facts.HayInSilo+Facts.Stock.Where(s=>s.Item=="(O)178").Sum(s=>s.Count);
                if(Facts.FeedNeeded>0&&availableHay>=Facts.FeedNeeded) {
                    var actions=new List<(string Tool,object Args)>();int bag=Game1.player.Items.Where(i=>i?.QualifiedItemId=="(O)178").Sum(i=>i.Stack);
                    if(Facts.HayInSilo<Facts.FeedNeeded&&bag<Facts.FeedNeeded)actions.Add(("work.run",new{goal="withdraw",item="(O)178",count=Facts.FeedNeeded-bag}));
                    actions.Add(("player.care",new{mode="feed",count=0}));if(QueueBusiness("feed",actions,"优先保障畜舍当日饲料"))return;
                }
                if(availableHay<animals*b.FeedDays) {
                    var actions=new List<(string Tool,object Args)>();BusinessMaterials(new(){["(O)178"]=Math.Max(0,animals*b.FeedDays-Facts.HayInSilo)},actions);
                    if(b.Tasks.Count>0)return;
                }
            }
            if(RunBusinessProduction())return;
            if(RunBusinessShipping())return;
            if(b.Expand&&Game1.timeOfDay<1600&&RunBusinessDevelopment())return;
            b.Reason="当前维护/生产候选已处理或等待原生生长、施工、供给；继续由模型安排有用途的采集、钓鱼、探索或休闲";
        }catch(Exception e) {
            b.Reason=e is InvalidOperationException?e.Message:e.GetType().Name;
            Data.Autoplay.Record("business_error",AgentJson.Encode(new{b.Activity,b.Reason}));businessAt=DateTime.UtcNow.AddSeconds(30);WakeAgent("business_review:"+b.Reason);
        }
    }
}
