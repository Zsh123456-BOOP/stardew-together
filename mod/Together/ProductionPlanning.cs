namespace Together;
// Decisions persist; availability is always recomputed from native inventories.
public sealed class ProductionPolicy {
    public string Selected {get;set;}="maintain";
    public string Reason {get;set;}="先完成已有农务和现金周转，未选择的新投资不占料";
    public int ChosenDay {get;set;}=-1;
    public int SurplusDay {get;set;}=-1;
    public HashSet<string> SaleItems {get;set;}=new();
}
public sealed record MaterialAllocation(int Owned,int Committed,int Operations,int Free,int Missing);
public static class ProductionAllocation {
    public static MaterialAllocation Split(int owned,int committed,int operations,int totalTarget) {
        owned=Math.Max(0,owned);committed=Math.Max(0,committed);operations=Math.Max(0,operations);
        int needed=Math.Max(Math.Max(0,totalTarget),committed+operations);
        return new(owned,Math.Min(owned,committed),Math.Min(Math.Max(0,owned-committed),Math.Max(operations,needed-committed)),Math.Max(0,owned-needed),Math.Max(0,needed-owned));
    }
}
