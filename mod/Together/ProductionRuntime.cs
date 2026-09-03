using System.Text.Json;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private Dictionary<string,int> InvestmentNeeds(BusinessOption option) {
        if(option.Kind=="building"&&DataLoader.Buildings(Game1.content).TryGetValue(option.Item,out var building))return (building.BuildMaterials??new()).ToDictionary(m=>ItemRegistry.QualifyItemId(m.ItemId)??m.ItemId,m=>m.Amount);
        if(option.Kind!="machine")return new();
        var goal=new SharedGoal{Item=option.Item,Entity=option.Item,Count=1,AllowNewFacilities=true};
        GoalPlanner.Rebuild(goal,goalRecipes,new GoalLedger(Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category})),Game1.Date.TotalDays,id=>id);
        return goal.Nodes.Where(n=>n.Kind=="gather").GroupBy(n=>n.Item).ToDictionary(g=>g.Key,g=>g.Sum(n=>n.Required));
    }
    private object ProductionOption(BusinessOption option) {
        var needs=InvestmentNeeds(option);
        bool funded=option.Cash<=BusinessMath.Spendable(Game1.player.Money,Data.Business.KeepGold,Data.Business.DailyBudget,Data.Business.ReservedToday+Data.FarmInvestment.ReservedToday);
        return new{id=option.Id,item=option.Item,kind=option.Kind,status=option.Gaps.Length>0?"locked":!funded?"needs_cash":needs.Any(m=>AccessibleStock(m.Key)<m.Value)?"needs_materials":"ready",cash=option.Cash,material_value=option.MaterialValue,estimated_daily_value=option.DailyMargin,estimated_payback=BusinessMath.Payback(option),needs=needs.Select(n=>new{item=n.Key,count=n.Value,available=AccessibleStock(n.Key),missing=Math.Max(0,n.Value-TeamStock(n.Key))}),gaps=option.Gaps,reason=option.Reason,uncertainty="估值不是到账收入；加工按当前一周原料供给，灌溉按节省劳动估值，未知物流/天气风险不假称精确预测"};
    }
    private object ProductionSummary() {
        var options=BusinessDevelopmentOptions().DistinctBy(o=>o.Id).OrderBy(o=>o.Id==Data.Operating.Production.Selected?0:1).ThenBy(BusinessMath.Payback).Take(4).Select(ProductionOption).ToArray();
        return new{decision=Data.Operating.Production,candidates=options,baseline=new{id="maintain",reason="保持现有生产、种植与现金周转，不新增投资"},note="用farm.production select选择一项投资；make建立其他配方目标；uses按材料分页查询全部已解析配方。候选不是全部能力，未选方案不预留。"};
    }
    internal object ProductionTool(JsonElement args) {
        RefreshFacts(true);ReadGoalRecipes();UpdateOperatingTargets();
        string action=AgentToolRegistry.Text(args,"action","read"),item=AgentToolRegistry.Text(args,"item"),reason=AgentToolRegistry.Text(args,"reason");
        if(action=="read")return new{summary=ProductionSummary(),materials=Facts.Stock.Select(s=>s.Item).Distinct().Take(60).Select(id=>new{item=id,allocation=AllocateMaterial(id),sale_allowed=ApprovedMaterialSale(id)}),projects=Data.SharedGoals.Where(g=>g.Status=="active").Select(g=>new{g.Id,g.Title,g.Count,g.Reserved,g.AutoBlockedReason})};
        if(action=="uses") {
            if(ItemRegistry.GetDataOrErrorItem(item).IsErrorItem)throw new InvalidOperationException("native_item_id_required");
            int offset=AgentToolRegistry.Number(args,"offset",0);if(offset<0)throw new InvalidOperationException("offset_must_be_nonnegative");
            var recipes=goalRecipes.Values.Where(r=>r.Inputs.Any(i=>i.Item==item||i.Item==ItemRegistry.Create(item).Category.ToString()||i.Item=="(O)"+ItemRegistry.Create(item).Category)).OrderByDescending(r=>r.Known).ThenBy(r=>r.Id,StringComparer.Ordinal).ToArray();
            return new{item,allocation=AllocateMaterial(item),total=recipes.Length,offset,recipes=recipes.Skip(offset).Take(12).Select(r=>new{r.Id,r.Name,r.Item,r.Output,r.Kind,status=!r.Known?"locked":r.Inputs.Any(i=>AccessibleStock(i.Item)<i.Count)?"needs_materials":"ready",r.Unlock,r.Facility,r.Inputs,r.Source}),note="只列数据中已解析的生产用途；锁定/模组回调/特殊机制不是已支持能力。没有批准生产计划就不自动占料。"};
        }
        if(reason.Length<4||reason.Length>500)throw new InvalidOperationException("production_decision_requires_reason_4_to_500_chars");
        if(action=="surplus") {
            if(ItemRegistry.GetDataOrErrorItem(item).IsErrorItem)throw new InvalidOperationException("native_item_id_required");
            var p=Data.Operating.Production;if(p.SurplusDay!=Game1.Date.TotalDays){p.SaleItems.Clear();p.SurplusDay=Game1.Date.TotalDays;}
            bool allow=args.TryGetProperty("allow",out var flag)&&flag.GetBoolean();if(allow)p.SaleItems.Add(item);else p.SaleItems.Remove(item);
            Data.Autoplay.Record("production_surplus_decision",AgentJson.Encode(new{item,allow,reason,allocation=AllocateMaterial(item)}));
            return new{item,allow,allocation=AllocateMaterial(item),note="只授权今日动态余量；项目、生产与任务预留仍保护，晚间集中原生出货，不会立即跑箱子"};
        }
        if(action=="make") {
            string recipeId=AgentToolRegistry.Text(args,"recipe");
            if(!goalRecipes.TryGetValue(recipeId,out var recipe)||!recipe.Known)throw new InvalidOperationException("recipe_locked_or_unknown_query_uses_and_unlock_first");
            var goal=(SharedGoal)AgentGoalCreate(JsonSerializer.SerializeToElement(new{request_id=AgentToolRegistry.Text(args,"request_id"),entity=recipeId,count=AgentToolRegistry.Number(args,"count",1),completion=recipe.Kind=="craft"?"crafted":recipe.Kind=="cook"?"cooked":"owned",allow_new_facilities=true}));
            AgentGoalRun(JsonSerializer.SerializeToElement(new{id=goal.Id}));Data.Autoplay.Record("production_recipe_decision",AgentJson.Encode(new{goal.Id,recipeId,reason}));return goal;
        }
        if(action!="select")throw new InvalidOperationException("production_action_requires_read_uses_select_make_surplus");
        string id=AgentToolRegistry.Text(args,"id");var policy=Data.Operating.Production;
        if(id==policy.Selected)return ProductionSummary();
        var option=id=="maintain"?null:BusinessDevelopmentOptions().FirstOrDefault(o=>o.Id==id)??throw new InvalidOperationException("production_candidate_changed_read_again");
        if(option!=null&&(option.Gaps.Length>0||option.Cash>BusinessMath.Spendable(Game1.player.Money,Data.Business.KeepGold,Data.Business.DailyBudget,Data.Business.ReservedToday+Data.FarmInvestment.ReservedToday)))throw new InvalidOperationException("production_choice_not_funded_or_unlocked");
        var child=Data.SharedGoals.FirstOrDefault(g=>g.Id==Data.Business.ChildGoal&&g.Status=="active");
        if(Data.Autoplay.Schedule.Tasks.Any(t=>t.state=="running"&&(Data.Business.Tasks.Contains(t.spec.id)||child!=null&&t.spec.goal_id==child.Id)))throw new InvalidOperationException("finish_active_production_step_before_changing_investment");
        Data.Autoplay.Schedule.CancelPending(Data.Business.Tasks);
        if(child!=null){AgentGoalRun(JsonSerializer.SerializeToElement(new{id=child.Id,mode="pause"}));child.Status="cancelled";child.Reserved.Clear();child.History.Add(new(){Day=Game1.Date.TotalDays,Text="经营取舍改变，释放未执行预留；已获得物资保留"});}
        Data.Business.Tasks.Clear();Data.Business.ChildGoal="";Data.Business.PendingAsset=id=="maintain"?"":id;
        policy.Selected=id;policy.Reason=reason;policy.ChosenDay=Game1.Date.TotalDays;
        UpdateOperatingTargets();Data.Autoplay.Record("production_investment_decision",AgentJson.Encode(new{policy,targets=Data.Operating.MaterialTargets}));return ProductionSummary();
    }
}
