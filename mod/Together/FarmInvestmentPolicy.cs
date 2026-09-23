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
    public Dictionary<string,int> Purchased {get;set;}=new();
    public HashSet<string> Receipts {get;set;}=new();
    public int Remaining(string item)=>Math.Max(0,Approved.GetValueOrDefault(item)-Purchased.GetValueOrDefault(item));
    public void Select(int day,Dictionary<string,int> items) {
        if(day!=Day){Day=day;Purchased.Clear();Receipts.Clear();}
        // Reselection replaces only the unfulfilled allowance. Goods already
        // delivered remain accounted for even when selecting additional seeds.
        Approved=new(Purchased);
        foreach(var row in items)Approved[row.Key]=Purchased.GetValueOrDefault(row.Key)+row.Value;
    }
    public void Receive(int day,string receipt,string item,int units) {
        if(day!=Day||units<=0||!Approved.ContainsKey(item)||!Receipts.Add(receipt+":"+item))return;
        Purchased[item]=Purchased.GetValueOrDefault(item)+units;
    }
    public string? Validate(int day,string item,int count,string additionalReason)=>day==Day&&Approved.ContainsKey(item)&&count>Remaining(item)&&additionalReason.Trim().Length<3?"seed_purchase_exceeds_remaining_manifest:acknowledge_owned_seeds_with_additional_reason":null;
}

public static class ReinvestmentReview {
    public static bool Needed(int oldCash,int cash,int oldCrops,int crops,int oldSeeds,int seeds,int remainingBudget,int time)=>
        time<2600&&oldCash>=0&&(seeds>oldSeeds||crops<oldCrops||cash>oldCash&&remainingBudget>0);
}
