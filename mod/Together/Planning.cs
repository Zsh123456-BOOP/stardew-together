namespace Together;

// Saved permissions are separate from a model's proposed plan. A reply cannot grant access.
public sealed class FarmPolicy {
    public bool Enabled {get;set;}=true;
    public bool FeedAnimals {get;set;}=true;
    public int DailyBudget {get;set;}
    public int KeepGold {get;set;}=500;
    public List<PlantingArea> Areas {get;set;}=new();
    public List<ShoppingOrder> Shopping {get;set;}=new();
}
public sealed class PlantingArea {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Location {get;set;}="Farm";
    public int X {get;set;}
    public int Y {get;set;}
    public int Width {get;set;}=3;
    public int Height {get;set;}=3;
    public string Seed {get;set;}="(O)472";
    public bool Enabled {get;set;}=true;
}
public sealed class ShoppingOrder {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Shop {get;set;}="SeedShop";
    public string Item {get;set;}="(O)472";
    public int Count {get;set;}=9;
    public int MaxUnitPrice {get;set;}=100;
    public bool Enabled {get;set;}=true;
}
public sealed class PlanNode {
    public string Id {get;set;}="";
    public string Title {get;set;}="";
    public string Owner {get;set;}="together";
    public string Status {get;set;}="ready";
    public string Skill {get;set;}="";
    public string Location {get;set;}="";
    public string Reason {get;set;}="";
    public int Count {get;set;}
    public int Deadline {get;set;}=-1;
    public List<string> DependsOn {get;set;}=new();
}
public sealed class ProgressGoal {
    public string Id {get;set;}="";
    public string Title {get;set;}="";
    public string Kind {get;set;}="";
    public bool Complete {get;set;}
    public int Deadline {get;set;}=-1;
    public int Gold {get;set;}
    public string PlayerStep {get;set;}="";
    public List<Requirement> Needs {get;set;}=new();
}
