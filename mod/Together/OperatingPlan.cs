namespace Together;

// Shared, persisted policy. All quantities are targets, never granted inventory.
public sealed class OperatingPlan {
    public string Direction {get;set;}="balanced";
    public string Reason {get;set;}="先维护与现金周转，再按真实瓶颈投资";
    public int DevelopmentCashHeld {get;set;}
    public int PlayerWaterLimit {get;set;}=24;
    public int PartnerWaterLimit {get;set;}=24;
    public Dictionary<string,int> MaterialTargets {get;set;}=new();
    public Dictionary<string,int> RetryAfter {get;set;}=new();
    public string PartnerTask {get;set;}="";
    public string PartnerReason {get;set;}="尚未分工";
}
public static class OperatingMath {
    public static int CashForSeeds(int cash,int keep,int daily,int committed,int development)=>Math.Max(0,Math.Min(daily-committed,cash-keep-Math.Max(0,development)));
    public static int DeliveredDeficit(int required,int accessible)=>Math.Max(0,required-Math.Max(0,accessible));
    public static int GatherDeficit(int required,int accessible,int transit)=>Math.Max(0,required-Math.Max(0,accessible)-Math.Max(0,transit));
    public static int WaterCapacity(int configured,int stamina,int reserve,int partner)=>Math.Max(0,Math.Min(configured,Math.Max(0,stamina-reserve)/2))+Math.Max(0,partner);
}
