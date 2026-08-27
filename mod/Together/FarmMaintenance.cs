namespace Together;

public sealed class FarmZone {
    public string Id {get;set;}="";
    public string Kind {get;set;}="reserve";
    public int X {get;set;}
    public int Y {get;set;}
    public int Width {get;set;}
    public int Height {get;set;}
    public bool AllowTrees {get;set;}
    public bool Contains(FarmCell p)=>p.X>=X&&p.Y>=Y&&p.X<X+Width&&p.Y<Y+Height;
}
public sealed class FarmCleanupOrder {
    public string Id {get;set;}="";
    public List<string> Scopes {get;set;}=new(){"roads","courtyard","fields","general"};
    public bool RemoveTrees {get;set;}
    public bool Recurring {get;set;}
    public string Status {get;set;}="active";
    public string Reason {get;set;}="";
    public int ReserveStamina {get;set;}=30;
    public int Until {get;set;}=1800;
    public int DailyLimit {get;set;}=30;
    public int Day {get;set;}=-1;
    public int CompletedToday {get;set;}
    public int Completed {get;set;}
    public int RetryAt {get;set;}
    public string TaskId {get;set;}="";
    public FarmCell? Patch {get;set;}
    public List<FarmCell> PendingPickup {get;set;}=new();
}
public sealed class FarmMaintenance {
    public int BudgetDay {get;set;}=-1;
    public int EnergyCommitted {get;set;}
    public int MinutesUsed {get;set;}
    public int LastMinute {get;set;}=-1;
    public bool WasWorking {get;set;}
    public bool Enabled {get;set;}=true;
    public List<FarmZone> Zones {get;set;}=new();
    public List<FarmCleanupOrder> Orders {get;set;}=new();
}
public static class FarmCleanupRules {
    public static bool ZoneKind(string kind)=>kind is "crop" or "production" or "woodland" or "pasture" or "reserve";
    public static bool Overlap(FarmZone a,FarmZone b)=>a.X<b.X+b.Width&&b.X<a.X+a.Width&&a.Y<b.Y+b.Height&&b.Y<a.Y+a.Height;
    public static bool Allowed(string kind,string zone,bool removeTrees,bool zoneAllowsTrees)=>kind is "weed" or "twig" or "stone"
        ?zone is not ("woodland" or "pasture" or "reserve")
        :kind is "tree" or "seedling"&&removeTrees&&zoneAllowsTrees&&zone is "crop" or "production";
    public static int Priority(string scope)=>scope switch{"roads"=>0,"courtyard"=>1,"fields"=>2,_=>3};
    public static void NewDay(FarmCleanupOrder order,int day) {
        if(order.Day==day)return;order.Day=day;order.CompletedToday=0;order.RetryAt=0;
        if(order.Recurring&&order.Status=="complete")order.Status="active";
    }
    public static int RemainingBudget(FarmCleanupOrder order)=>Math.Max(0,order.DailyLimit-order.CompletedToday);
}

// Conservative estimates, not claimed exact native stamina costs. The daily cap
// is shared by every order so new IDs/food/retries cannot refill the allowance.
public sealed record CleanupAllowance(int FarmEnergy,int ProductionReserve,int Reserve,int Available,string Reason);
public static class CleanupBudget {
    public static CleanupAllowance Calculate(int stamina,int maximum,int safety,int dry,int newPlots,int committed,int minutes) {
        int farm=Math.Max(0,dry)*2+Math.Max(0,newPlots)*4;
        int production=(int)Math.Ceiling(maximum*.20);
        int reserve=Math.Max(15,safety)+farm+production;
        int available=Math.Max(0,Math.Min(stamina-reserve,(int)(maximum*.35)-committed));
        string reason=minutes>=180?"cleanup_daily_time_cap":available==0?"cleanup_farm_or_daily_energy_reserve":"available";
        return new(farm,production,reserve,minutes>=180?0:available,reason);
    }
}
