namespace Together;
public static class SeedSelectionPolicy {
    // A fresh explicit selection already authorizes its new quantities. An empty
    // list is a decline, irrespective of previous purchases; no second reason.
    public static string? Validate(bool currentQuote,bool consumed,string phase,string reason)=>!currentQuote||consumed?"seed_quote_expired_read_shop_again":phase is "planning" or "executing" or "start_planning"?"seed_selection_not_pending_do_not_replace_active_plan":string.IsNullOrWhiteSpace(reason)?"seed_selection_reason_required":null;
}
public sealed class FarmInvestmentPolicy {
    public SeedPurchaseManifest Purchase {get;set;}=new();
    public bool Enabled {get;set;}
    public bool Repeat {get;set;}
    public int BudgetPerDay {get;set;}=-1;
    public int KeepGold {get;set;}=0;
    public int Plots {get;set;}=96;
    public int ManualWaterLimit {get;set;}=-1;
    public string Priority {get;set;}="income";
    public string Shop {get;set;}="SeedShop";
    public string Location {get;set;}="SeedShop";
    public int Day {get;set;}=-1;
    public int SeedReviewDay {get;set;}=-1;
    public int SeedReviewCash {get;set;}=-1;
    public int ReviewedCash {get;set;}=-1;
    public int ReviewedCrops {get;set;}=-1;
    public int ReviewedSeeds {get;set;}=-1;
    public int Reviews {get;set;}
    public int ReservedToday {get;set;}
    public string CropLocation {get;set;}="Farm";
    public List<string> CompletedLocations {get;set;}=new();
    public bool OwnedSeedsPassDone {get;set;}
    public bool OwnedSeedsOnly {get;set;}
    public bool PurchaseRecoveryUsed {get;set;}
    public string Phase {get;set;}="idle";
    public string Error {get;set;}="";
    public string ServiceTask {get;set;}="";
    public string PlanId {get;set;}="";
    public List<string> Tasks {get;set;}=new();
}

// Authorization survives quote refreshes and interrupted work. Only native
// receipts consume it; queue submission is never evidence of a purchase.
public sealed class SeedPurchaseManifest {
    public int Day {get;set;}=-1;
    public Dictionary<string,int> Approved {get;set;}=new();
    public Dictionary<string,int> Planned {get;set;}=new();
    public Dictionary<string,int> Purchased {get;set;}=new();
    public bool PlanFinalized {get;set;}
    public string PlanReason {get;set;}="";
    public HashSet<string> Receipts {get;set;}=new();
    public int Allowance(string item)=>Math.Max(0,Approved.GetValueOrDefault(item)-Purchased.GetValueOrDefault(item));
    public int Remaining(string item)=>Math.Max(0,(PlanFinalized?Planned:Approved).GetValueOrDefault(item)-Purchased.GetValueOrDefault(item));
    public int NotScheduled(string item)=>PlanFinalized?Math.Max(0,Approved.GetValueOrDefault(item)-Math.Max(Planned.GetValueOrDefault(item),Purchased.GetValueOrDefault(item))):0;
    private void NewDay(int day){Day=day;Approved.Clear();Planned.Clear();Purchased.Clear();Receipts.Clear();PlanFinalized=false;PlanReason="";}
    public void Select(int day,Dictionary<string,int> items) {
        if(items.Any(p=>p.Value<0))throw new InvalidOperationException("seed_selection_negative_quantity");
        if(day!=Day)NewDay(day);
        Approved=new(Purchased);Planned.Clear();PlanFinalized=false;PlanReason="selection_upper_limits_only";
        foreach(var row in items)Approved[row.Key]=Purchased.GetValueOrDefault(row.Key)+row.Value;
    }
    public void ValidatePlan(int day,IReadOnlyDictionary<string,int> quantities) {
        if(day!=Day||quantities.Any(p=>p.Value<0||p.Value>Allowance(p.Key)))throw new InvalidOperationException("seed_plan_exceeds_selection");
    }
    public void FinalizePlan(int day,IReadOnlyDictionary<string,int> quantities,string reason) {
        ValidatePlan(day,quantities);
        // Validate the whole batch before mutating. An upper limit that the
        // planner did not use is not an interrupted purchase to retry.
        Planned=new(Purchased);
        foreach(var row in quantities)Planned[row.Key]=Purchased.GetValueOrDefault(row.Key)+row.Value;
        PlanFinalized=true;PlanReason=reason;
    }
    public void Close(int day,string reason) {if(day==Day)FinalizePlan(day,new Dictionary<string,int>(),reason);}
    public void Receive(int day,string receipt,string item,int units) {
        if(day<Day||units<=0||string.IsNullOrEmpty(item))return;
        if(day!=Day)NewDay(day);
        if(!Receipts.Add(receipt+":"+item))return;
        Purchased[item]=Purchased.GetValueOrDefault(item)+units;
        // Direct additional purchases enter accounting only after native goods
        // arrive. A rejected proposal never increases pending authorization.
        Approved[item]=Math.Max(Approved.GetValueOrDefault(item),Purchased[item]);
        if(PlanFinalized)Planned[item]=Math.Max(Planned.GetValueOrDefault(item),Purchased[item]);
    }
    public string? Validate(int day,string item,int count,string additionalReason)=>day==Day&&Approved.ContainsKey(item)&&count>Remaining(item)&&additionalReason.Trim().Length<3?"seed_purchase_exceeds_remaining_manifest:acknowledge_owned_seeds_with_additional_reason":null;
}

public static class ReinvestmentReview {
    public static bool Needed(int oldCash,int cash,int oldCrops,int crops,int oldSeeds,int seeds,int remainingBudget,int time)=>
        time<2600&&oldCash>=0&&(seeds>oldSeeds||crops<oldCrops||cash>oldCash&&remainingBudget>0);
}
