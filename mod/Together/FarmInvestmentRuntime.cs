using System.Text.Json;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class ModEntry {
    private DateTime nextCropExpansionCheck;
    internal object ConfigureFarmInvestment(JsonElement args) {
        var p=Data.FarmInvestment;
        int budget=AgentToolRegistry.Number(args,"budget_per_day",p.BudgetPerDay),keep=AgentToolRegistry.Number(args,"keep_gold",p.KeepGold),plots=AgentToolRegistry.Number(args,"plots",p.Plots),water=AgentToolRegistry.Number(args,"max_daily_manual_water",p.ManualWaterLimit);
        string priority=AgentToolRegistry.Text(args,"priority",p.Priority),shop=AgentToolRegistry.Text(args,"shop",p.Shop),location=AgentToolRegistry.Text(args,"location",p.Location);
        if(budget is <0 or >10000000||keep is <0 or >10000000||plots is <1 or >96||water is <0 or >96||priority is not ("income" or "cashflow" or "collection" or "low_labor")||Game1.getLocationFromName(location)==null||shop.Length is <1 or >120)throw new InvalidOperationException("invalid_farm_investment_policy");
        if(args.TryGetProperty("enabled",out var rawEnabled)&&rawEnabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("investment_enabled_must_be_boolean");
        p.BudgetPerDay=budget;p.KeepGold=keep;p.Plots=plots;p.ManualWaterLimit=water;p.Priority=priority;p.Shop=shop;p.Location=location;
        if(args.TryGetProperty("enabled",out var enabled)) {
            if(enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("investment_enabled_must_be_boolean");
            p.Enabled=enabled.GetBoolean();
            if(p.Enabled&&p.Phase=="blocked"){p.Phase="idle";p.Error="";p.ServiceTask="";p.PlanId="";p.Tasks.Clear();}
        }
        return new{policy=p,note="每天维护后依据实际现金与营业现场报价重新规划一批投资；不花预计出货收入、不超过每日预留额度；关闭不取消已排原生任务。"};
    }
    private void TickFarmInvestment() {
        var p=Data.FarmInvestment;
        if(!AutoplayRunning||!p.Enabled||Game1.eventUp||Game1.fadeToBlack||Game1.locationRequest!=null)return;
        if(p.Day!=Game1.Date.TotalDays){p.Day=Game1.Date.TotalDays;p.PurchaseRecoveryUsed=false;p.OwnedSeedsPassDone=false;p.OwnedSeedsOnly=false;p.ReservedToday=0;p.Phase="idle";p.Error="";p.ServiceTask="";p.PlanId="";p.Tasks.Clear();p.CompletedLocations.Clear();p.CropLocation="Farm";}
        if(p.Phase=="done"&&p.OwnedSeedsOnly){p.OwnedSeedsOnly=false;p.Phase="idle";p.Tasks.Clear();}
        if(p.Phase=="done") {
            if(DateTime.UtcNow<nextCropExpansionCheck)return;nextCropExpansionCheck=DateTime.UtcNow.AddSeconds(10);
            if(!p.CompletedLocations.Contains(p.CropLocation))p.CompletedLocations.Add(p.CropLocation);
            var next=MaterialLocations(l=>l.IsGreenhouse&&!p.CompletedLocations.Contains(l.NameOrUniqueName)).FirstOrDefault();
            if(next==null||Game1.timeOfDay>=1500)return;p.CropLocation=next.NameOrUniqueName;p.Phase="start_planning";
        }
        if(p.Phase=="blocked")return;
        try {
            if(p.Phase=="executing") {
                var tasks=p.Tasks.Select(id=>Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==id)).ToArray();
                if(!p.PurchaseRecoveryUsed&&tasks.Any(t=>t?.state=="failed"&&t.spec.tool=="player.buy")&&!tasks.Any(t=>t?.state=="running")&&!playerExecutor.Busy&&!WorkActorBusy("player")) {
                    if(Game1.activeClickableMenu is ShopMenu shop) {if(shop.heldItem!=null||!shop.readyToClose())throw new InvalidOperationException("purchase_recovery_requires_receiving_held_item");shop.exitThisMenu();}
                    if(Game1.activeClickableMenu!=null)return;
                    Data.Autoplay.Schedule.CancelPending(tasks.Where(t=>t!=null&&!t.Terminal).Select(t=>t!.spec.id));
                    p.PurchaseRecoveryUsed=true;p.OwnedSeedsOnly=true;p.OwnedSeedsPassDone=true;p.Phase="start_planning";p.Tasks.Clear();
                    // Keep the original spending reservation. Replan only goods
                    // already delivered, without re-buying the failed manifest.
                    Data.Autoplay.Record("farm_purchase_partial_recovery","现场采购部分失败；保留原预算预留，按真实已到货种子重新规划。");return;
                }
                if(tasks.Any(t=>t==null||t.state is "failed" or "blocked" or "cancelled" or "needs_review"))throw new InvalidOperationException("farm_investment_task_interrupted_read_plan_before_retry");
                if(tasks.All(t=>t!.state=="succeeded")){p.Phase="done";Data.Autoplay.Record("farm_investment_complete",AgentJson.Encode(new{p.Day,p.ReservedToday,p.Tasks}));WakeAgent("farm_investment_complete");}return;
            }
            if(p.Phase=="observing_shop") {
                var task=Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==p.ServiceTask);
                if(task?.state=="failed"&&task.error is "native_service_did_not_open_or_wrong_shop" or "native_service_unavailable_check_hours_and_owner") {
                    p.Error="shop_unavailable_use_owned_seeds_only";p.Phase="start_planning";
                }else {
                    if(task==null||task.state is "failed" or "blocked" or "cancelled" or "needs_review")throw new InvalidOperationException("farm_shop_visit_failed_or_interrupted:"+task?.error);
                    if(task.state!="succeeded")return;
                    if(Game1.activeClickableMenu is not ShopMenu menu||menu.ShopId!=p.Shop||menu.heldItem!=null)throw new InvalidOperationException("farm_observed_shop_changed");
                    ObserveShop(JsonSerializer.SerializeToElement(new{}));if(!menu.readyToClose())return;menu.exitThisMenu();p.Phase="start_planning";
                }
            }
            if(playerExecutor.Busy||WorkActorBusy("player")||Game1.activeClickableMenu!=null||Game1.player.UsingTool||!Game1.player.CanMove||Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.actor=="player"&&!t.Terminal))return;
            if(p.Phase=="planning") {
                if(!economyJobs.TryGetValue(p.PlanId,out var job)||job.Epoch!=agentSaveEpoch||job.Snapshot.Day!=Game1.Date.TotalDays)throw new InvalidOperationException("farm_investment_snapshot_lost_replan");
                if(!job.Task.IsCompleted)return;
                var result=job.Task.GetAwaiter().GetResult();if(result.Plants.Count==0){p.Phase="done";p.Error=result.StopReason;return;}
                if(result.FirstDayEnergy>AvailablePlantingEnergy()){p.Phase="start_planning";p.PlanId="";return;}
                if(result.Spent>p.BudgetPerDay-p.ReservedToday)throw new InvalidOperationException("farm_investment_budget_changed");
                if(Data.Autoplay.Schedule.Tasks.Count+24>180)Data.Autoplay.Schedule.Archive();
                // Reserve before queueing. Failed/interrupted purchases cannot reset
                // the allowance and duplicate spending later on the same game day.
                p.ReservedToday+=result.Spent;
                var queued=JsonSerializer.SerializeToElement(ExecuteFarmEconomy(JsonSerializer.SerializeToElement(new{plan_id=p.PlanId})));
                if(!queued.TryGetProperty("tasks",out var taskIds))throw new InvalidOperationException("farm_investment_queue_not_created");
                p.Tasks=taskIds.EnumerateArray().Select(t=>t.GetString()!).ToList();p.Phase="executing";
                Data.Autoplay.Record("farm_investment_plan",AgentJson.Encode(new{p.Day,p.PlanId,p.ReservedToday,p.Tasks,result.Plants}));return;
            }
            if(Game1.timeOfDay>=1500){p.Phase="done";p.Error="investment_window_closed_revisit_tomorrow";return;}
            if(p.Phase=="idle") {
                bool ownedSeeds=Game1.player.Items.Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer())).Any(i=>i?.Category==-74);
                if(!p.OwnedSeedsPassDone&&ownedSeeds) {p.OwnedSeedsPassDone=true;p.OwnedSeedsOnly=true;p.Phase="start_planning";}
                else {
                if(Game1.timeOfDay<900)return;
                if(p.BudgetPerDay>p.ReservedToday&&Game1.player.Money>p.KeepGold) {
                    if(Data.Autoplay.Schedule.Tasks.Count>180)Data.Autoplay.Schedule.Archive();
                    string id="farm-quote-"+Guid.NewGuid().ToString("N");
                    Data.Autoplay.Schedule.Submit(id,Data.Autoplay.Schedule.Revision,new(){new(){id=id,tool="player.service",args=JsonSerializer.SerializeToElement(new{location=p.Location,service="shop",shop=p.Shop}),day=p.Day,deadline=1700,purpose="现场读取今日种子报价，按持续预算重新投资"}},p.Day);
                    p.ServiceTask=id;p.Phase="observing_shop";return;
                }
                p.Phase="start_planning";
                }
            }
            if(p.Phase=="start_planning") {
                var response=JsonSerializer.SerializeToElement(PlanFarmEconomy(JsonSerializer.SerializeToElement(new{budget=p.OwnedSeedsOnly||p.Error=="shop_unavailable_use_owned_seeds_only"?0:Data.Business.Enabled?OperatingMath.CashForSeeds(Game1.player.Money,Data.Business.KeepGold,Data.Business.DailyBudget,Data.Business.ReservedToday+p.ReservedToday,Data.Operating.DevelopmentCashHeld):Math.Max(0,p.BudgetPerDay-p.ReservedToday),keep_gold=p.KeepGold+(Data.Business.Enabled?Data.Operating.DevelopmentCashHeld:0),plots=p.Plots,max_daily_manual_water=p.ManualWaterLimit,priority=p.Priority,location=p.CropLocation})));
                p.PlanId=response.GetProperty("plan_id").GetString()!;p.Phase="planning";
            }
        }catch(Exception error){p.Phase="blocked";p.Error=error is InvalidOperationException?error.Message:error.GetType().Name;Data.Autoplay.Record("farm_investment_blocked",p.Error);WakeAgent("farm_investment_blocked");}
    }
}
