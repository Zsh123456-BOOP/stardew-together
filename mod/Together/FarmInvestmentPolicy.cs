namespace Together;
public sealed class FarmInvestmentPolicy {
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

public static class ReinvestmentReview {
    public static bool Needed(int oldCash,int cash,int oldCrops,int crops,int oldSeeds,int seeds,int remainingBudget,int time)=>
        time<2600&&oldCash>=0&&(seeds>oldSeeds||crops<oldCrops||cash>oldCash&&remainingBudget>0);
}
