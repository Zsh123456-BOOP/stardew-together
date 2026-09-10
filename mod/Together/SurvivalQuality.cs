namespace Together;

public sealed class QualityDay {
    public int Day {get;set;}
    public int? SleepTime {get;set;}
    public int ActualAwakeMinutes {get;set;}
    public Dictionary<string,double> TimeBreakdownMinutes {get;set;}=new();
    public Dictionary<string,double> WallDetailSeconds {get;set;}=new();
    public Dictionary<string,double> WallSecondsByState {get;set;}=new();
    public double? AwakeLaborRatio=>SleepTime.HasValue&&ActualAwakeMinutes>0?TimeBreakdownMinutes.GetValueOrDefault("labor")/ActualAwakeMinutes:null;
    public int EffectiveLaborMinutes {get;set;}
    public int AvailableMinutes {get;set;}=1200;
    public int ObservedAwakeMinutes {get;set;}
    public int VerifiedActionsDelta {get;set;}
    public Dictionary<string,int> MinutesByState {get;set;}=new();
    public int? DegradationTime {get;set;}
    public string DegradationReason {get;set;}="";
    public long CashStart {get;set;}
    public long AssetsStart {get;set;}
    public long CashEnd {get;set;}
    public long AssetsEnd {get;set;}
    public int InventoryTurnover {get;set;}
    public bool Finalized {get;set;}
    public double LaborRatio=>EffectiveLaborMinutes/(double)AvailableMinutes;
}
public sealed class SurvivalQuality {
    public List<QualityDay> Days {get;set;}=new();
    public HashSet<string> Receipts {get;set;}=new();
    public Dictionary<string,string> PlantedTiles {get;set;}=new();
    public Dictionary<string,int> Harvested {get;set;}=new();
    public Dictionary<string,int> PendingSale {get;set;}=new();
    public Dictionary<string,int> ShippingBaseline {get;set;}=new();
    public long EarnedBaseline {get;set;}
    public int ConfirmedSaleDay {get;set;}=-1;
    public List<string> CycleEvidence {get;set;}=new();
    public bool CycleCompleted {get;set;}
    public QualityDay Current(int day,long cash=0,long assets=0) {
        var row=Days.FirstOrDefault(d=>d.Day==day);if(row!=null)return row;
        row=new(){Day=day,CashStart=cash,AssetsStart=assets};Days.Add(row);return row;
    }
    public void Sample(int day,int minutes,string state) {
        var row=Current(day);if(row.Finalized||minutes<=0)return;
        row.ObservedAwakeMinutes+=minutes;row.MinutesByState[state]=row.MinutesByState.GetValueOrDefault(state)+minutes;
        if(state=="progressing_work")row.EffectiveLaborMinutes+=minutes;
    }
    public bool G1=>Days.All(d=>!d.DegradationTime.HasValue||d.DegradationTime<600||d.DegradationTime>1200);
    public bool G2=>!Days.Where(d=>d.Finalized).OrderBy(d=>d.Day).Zip(Days.Where(d=>d.Finalized).OrderBy(d=>d.Day).Skip(1)).Any(p=>p.Second.Day==p.First.Day+1&&p.First.VerifiedActionsDelta==0&&p.Second.VerifiedActionsDelta==0);
}
