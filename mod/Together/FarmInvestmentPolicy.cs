namespace Together;
public sealed class FarmInvestmentPolicy {
    public bool Enabled {get;set;}
    public int BudgetPerDay {get;set;}=0;
    public int KeepGold {get;set;}=500;
    public int Plots {get;set;}=24;
    public int ManualWaterLimit {get;set;}=24;
    public string Priority {get;set;}="income";
    public string Shop {get;set;}="SeedShop";
    public string Location {get;set;}="SeedShop";
    public int Day {get;set;}=-1;
    public int ReservedToday {get;set;}
    public string Phase {get;set;}="idle";
    public string Error {get;set;}="";
    public string ServiceTask {get;set;}="";
    public string PlanId {get;set;}="";
    public List<string> Tasks {get;set;}=new();
}
