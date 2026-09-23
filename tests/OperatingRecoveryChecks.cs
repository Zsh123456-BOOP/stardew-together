using System.Text.Json;
using Together;
public static class OperatingRecoveryChecks {
    private static JsonElement J(object value)=>JsonSerializer.SerializeToElement(value,AgentJson.Options);
    public static void Run(Action<bool,string> check) {
        var purchase=new SeedPurchaseManifest();purchase.Select(0,new(){{"(O)472",9}});purchase.Receive(0,"native-nine","(O)472",9);
        check(SeedSelectionPolicy.Validate(true,false,"done","已有24株，当前不追加采购")==null,"13:00 regression: a fresh empty selection can decline after earlier purchases");
        purchase.Select(0,new());
        check(purchase.Purchased["(O)472"]==9&&purchase.Remaining("(O)472")==0,"declining cancels unfulfilled allowance without deleting delivered items");
        check(SeedSelectionPolicy.Validate(true,false,"done","追加一批防风草，已核对现有库存和照料")==null,"new explicit seed selection needs only its single reason");
        purchase.Select(0,new(){{"(O)472",6}});
        check(purchase.Remaining("(O)472")==6&&purchase.Purchased["(O)472"]==9,"fresh additional selection authorizes six new units without rebuying the prior nine");
        check(SeedSelectionPolicy.Validate(true,true,"done","重复刚才的选择")!.StartsWith("seed_quote_expired"),"consumed selection quote rejects replay even after the original work finishes");
        check(SeedSelectionPolicy.Validate(true,false,"executing","改动执行中的采购")!.Contains("not_pending"),"fresh quote cannot replace active purchase work");
        check(SeedSelectionPolicy.Validate(false,false,"done","使用旧报价")!.StartsWith("seed_quote_expired"),"expired quote still requires native refresh");
        var failureWatch=new ExecutionFailureWatch();int failures=0;
        for(int n=0;n<3;n++)failures=failureWatch.Observe("trial",n.ToString(),"player","seed_selection_reason_required",2);
        check(failures==3&&ExecutionFailureWatch.CorrectableInput("seed_selection_reason_required")&&!ExecutionFailureWatch.CorrectableInput("quality_evidence_failed"),"three selection-input errors are recoverable while native evidence failures remain fatal");
        check(ExecutionFailureWatch.PurchaseInput("known_failure_conditions_unchanged:seed_quote_expired_read_shop_again:evidence=abc"),"unchanged-condition wrapper retains local purchase recovery instead of a frozen menu");
        check(ExecutionFailureWatch.Repair("seed_selection_reason_required").Contains("items=[]"),"selection error response provides an explicit decline path");
        check(WorkQuantity.Resolve(53,50,8,55,true).Error=="material_quantity_conflict_use_count_or_stock_target","08:20 regression: contradictory increment and total do not silently become two units");
        check(WorkQuantity.Resolve(53,50,0,55,true).Missing==2&&WorkQuantity.Resolve(53,50,8,null,true).Target==61,"total 55 requests two extra units; incremental eight requests stock 61");
        var receipt=J(ExecutionContract.Receipt(new{command_id="work:wood",goal="wood",status="succeeded",completed=3,gained=52,requested=49},"epoch",1));
        check(receipt.GetProperty("quantity_units").GetProperty("completed").GetString()!.Contains("处数")&&receipt.GetProperty("actual_progress").GetProperty("gained").GetInt32()==52,"resource receipts distinguish three targets from fifty-two item units");
        check(OverlayText.ResourceProgress("木材",2,2,55)=="最近采集：新增2份木材，处理2处；背包现有55份木材","overlay shows native item gain, target count and actual bag stock with units");
        var seedKey=new StackKey("seed",0,"ordinary");var singleSlot=new CapacitySnapshot(1,Array.Empty<(StackKey,int,int,bool)>());
        check(CapacityPlan.MaxPut(singleSlot,seedKey,999)==999&&singleSlot.FreeSlots==1,"one free bag slot holds a stack of 999 seeds and capacity simulation does not mutate inventory");
        var partlyFilled=new CapacitySnapshot(1,new[]{(seedKey,990,999,false)});
        check(CapacityPlan.MaxPut(partlyFilled,seedKey,999)==9&&CapacityPlan.MaxPut(partlyFilled,new("other",0,"ordinary"),999)==0,"native-compatible stack space counts without inventing slots for another seed type");
        var care=J(OperatingDecisionPolicy.Labor(68,270,0,0,24,1,0));
        check(care.GetProperty("today_pending_conservative").GetInt32()==0&&care.GetProperty("next_dry_day_water_estimate").GetDouble()==48&&care.GetProperty("care_note").GetString()!.Contains("重新浇水"),"watered today still exposes forty-eight energy of next-dry-day care");
        // Replay the failing day's relevant transitions, without inventing game
        // outcomes: closed shop -> authorized order -> partial native delivery
        // -> save/reload -> remaining purchase -> planting-versus-sleep review.
        var morning=J(new{now=new{day=0,time=740,location="Farm"},service_hours=new[]{new{location="SeedShop",opens=900,closes=2100,can_enter_now=false,recheck_at=900,note=new string('x',1000)}}});
        var view=JsonSerializer.Deserialize<JsonElement>(ContextBudget.Pack(morning,4000,512).Json);
        var shop=view.GetProperty("service_hours")[0];
        check(shop.GetProperty("opens").GetInt32()==900&&shop.GetProperty("recheck_at").GetInt32()==900&&!shop.GetProperty("can_enter_now").GetBoolean(),"07:40 closed shop keeps its 09:00 opening event under context compression");
        check(!shop.TryGetProperty("note",out _),"future shop facts do not retain verbose service documentation");
        check(OperatingDecisionPolicy.ShopObservation("available",800,900,2100,false)=="before_opening","shop read before opening identifies time prerequisite");
        check(OperatingDecisionPolicy.ShopObservation("available",910,900,2100,false)=="not_at_shop","shop open does not imply player has reached shop");
        check(OperatingDecisionPolicy.ShopObservation("available",940,900,2100,true)=="shop_menu_not_open","being inside shop does not imply native shop menu is open");
        check(OperatingDecisionPolicy.ShopObservation("closed_weekday",940,900,2100,true)=="closed_today_or_window_ended","closed-today service cannot be presented as waiting for today's opening");
        var order=new SeedPurchaseManifest();order.Select(0,new(){{"(O)472",10},{"(O)475",4}});
        check(order.Remaining("(O)472")==10&&order.Remaining("(O)475")==4,"selection and queue submission do not count as delivered seeds");
        order.Receive(0,"purchase-parsnip:0","(O)472",10);
        order=JsonSerializer.Deserialize<SeedPurchaseManifest>(JsonSerializer.Serialize(order))!;
        order.Receive(0,"purchase-parsnip:0","(O)472",10);
        check(order.Purchased["(O)472"]==10&&order.Remaining("(O)472")==0&&order.Remaining("(O)475")==4,"partial purchase receipt survives reload and replay without erasing remaining potato order");
        check(order.Validate(0,"(O)472",10,"")!=null,"repeat purchase after interrupted planting is rejected before debit");
        check(order.Validate(0,"(O)475",4,"")==null,"interrupted order permits only genuinely undelivered quantity");
        check(order.Validate(0,"(O)472",5,"原有10份用于原田块，新增5份用于新田块且已重算照料") ==null,"explicit additional investment is still possible");
        order.Receive(0,"purchase-potato:0","(O)475",4);
        check(order.Remaining("(O)475")==0&&order.Purchased.Values.Sum()==14,"both native deliveries close purchasing while unplanted inventory remains a separate fact");
        order.Receive(1,"next-day","(O)472",5);
        check(order.Purchased["(O)472"]==10&&order.Validate(1,"(O)472",5,"")==null,"yesterday's manifest does not constrain unrelated next-day purchases");
        order.Select(0,new(){{"(O)472",5}});order.Receive(0,"purchase-parsnip:0","(O)472",10);
        check(order.Purchased["(O)472"]==10&&order.Approved["(O)472"]==15&&order.Remaining("(O)472")==5&&order.Purchased["(O)475"]==4,"explicit same-day reselection preserves prior deliveries and their replay protection");
        var owned=new ReviewOption("plan:owned-seeds:(O)472",0,1,"plan_owned_seeds;purchase_cost=0",10);
        var alternatives=DecisionReviewPolicy.Select(new[]{new ReviewOption("inspect:seed-offers",0,30,"quote"),new ReviewOption("select:seeds",0,1,"select"),new ReviewOption("infrastructure:storage",0,20,"storage"),owned});
        check(alternatives.Length==3&&alternatives[0]==owned&&alternatives.Count(o=>o.id is "inspect:seed-offers" or "select:seeds")==1,"owned-seed planning precedes quote/selection and duplicate investment stages do not crowd it out");
        JsonElement Sleep(object value)=>J(new{reason="比较未来收益后决定安排",alternatives=new[]{new{id=owned.id,because="defer",detail="明确比较播种和推迟一天",value}}});
        check(DecisionReviewPolicy.Sleep(J(new{reason="没有即时收入所以睡觉",alternatives=new[]{new{id=owned.id,because="low_value",detail="播种不会立即回款"}}}),202,600,new[]{owned})!.Contains("future_value"),"11:00 regression: no immediate cash alone does not pass sleep review");
        check(DecisionReviewPolicy.Sleep(Sleep(new{today="今天种可以更早成熟",defer="明日种会推迟收入",owned_seeds=10,additional_seed_cost=200,growth_tradeoff="延迟一天开始生长"}),202,600,new[]{owned})!.Contains("no_new_purchase"),"owned seeds cannot be rejected on a fictitious repeat purchase cost");
        check(DecisionReviewPolicy.Sleep(Sleep(new{today="今天种可以更早成熟",defer="明日种会推迟收入",owned_seeds=0,additional_seed_cost=0,growth_tradeoff="延迟一天开始生长"}),202,600,new[]{owned})!.Contains("count_changed"),"sleep review must acknowledge the real unplanted seed count");
        check(DecisionReviewPolicy.Sleep(Sleep(new{today="今天种可以更早成熟",defer="明日种会推迟收入",owned_seeds=10,additional_seed_cost=0}),202,600,new[]{owned})!.Contains("growth_tradeoff"),"deferred planting requires an explicit growth consequence");
        check(DecisionReviewPolicy.Sleep(Sleep(new{today="今天种可提前成熟但增加照料需求",defer="保留种子待调整灌溉规划，接受收入推迟",owned_seeds=10,additional_seed_cost=0,growth_tradeoff="推迟播种浇水会推迟生长，愿意承担此代价"}),202,600,new[]{owned})==null,"informed deferral remains a model choice without a mandatory bedtime or planting cap");
        var specs=ToolSpecs.Select(new Dictionary<string,string>{{"player.sleep",""},{"agent.pause",""}},J(new{}));
        var fields=specs[0].Parameters.GetProperty("properties").GetProperty("alternatives").GetProperty("items").GetProperty("properties").GetProperty("value").GetProperty("properties");
        check(fields.TryGetProperty("growth_tradeoff",out _)&&fields.TryGetProperty("additional_seed_cost",out _),"API contract exposes the same future-value facts required at execution");
        var manifestView=JsonSerializer.Deserialize<JsonElement>(ContextBudget.Pack(new{now=new{day=0},purchase_status=new{approved=14,purchased=14,unplanted_owned=14},memory=new{large=new string('x',16000)}},2000,512).Json);
        check(manifestView.GetProperty("purchase_status").GetProperty("unplanted_owned").GetInt32()==14,"compression retains delivered-versus-unplanted distinction");
    }
}
