namespace Together;

// Persist policies and receipts, never an assumed balance or predicted harvest.
public sealed class FarmBusiness {
    public bool Enabled {get;set;}
    public bool Expand {get;set;}=true;
    public int DailyBudget {get;set;}=1000;
    public int KeepGold {get;set;}=200;
    public int MaxAnimals {get;set;}=12;
    public int MaxMachines {get;set;}=32;
    public int FeedDays {get;set;}=7;
    public int Day {get;set;}=-1;
    public int ReservedToday {get;set;}
    public int ActiveReservation {get;set;}
    public string Activity {get;set;}="";
    public string Reason {get;set;}="";
    public string ChildGoal {get;set;}="";
    public string PendingAsset {get;set;}="";
    public List<string> Tasks {get;set;}=new();
    public Dictionary<string,int> RetryAfter {get;set;}=new();
    public int Revision {get;set;}
    public int OpeningCash {get;set;}
    public long OpeningEarned {get;set;}
}
public sealed record BusinessOption(string Id,string Kind,string Item,int Cash,double MaterialValue,double DailyMargin,int Existing,int Wanted,string Reason,string[] Gaps);
public static class BusinessMath {
    public static int Settle(int reserved,int allowance,int actualSpent)=>Math.Max(0,reserved-Math.Max(0,allowance-Math.Max(0,actualSpent)));
    public static int Spendable(int cash,int keep,int budget,int committed)=>Math.Max(0,Math.Min(cash-keep,budget-committed));
    public static double Payback(BusinessOption o)=>o.DailyMargin>0?(o.Cash+o.MaterialValue)/o.DailyMargin:double.PositiveInfinity;
    public static IEnumerable<BusinessOption> Rank(IEnumerable<BusinessOption> options,int cash,int keep,int budget,int committed)=>options
        .Where(o=>o.Wanted>o.Existing&&o.Gaps.Length==0&&o.DailyMargin>0&&o.Cash<=Spendable(cash,keep,budget,committed))
        .OrderBy(Payback).ThenByDescending(o=>o.DailyMargin).ThenBy(o=>o.Id,StringComparer.Ordinal);
    public static int ProcessingReserve(int stock,int perBatch,int machines,int batches=1)=>Math.Min(Math.Max(0,stock),Math.Max(0,perBatch)*Math.Max(0,machines)*Math.Clamp(batches,1,3));
}
