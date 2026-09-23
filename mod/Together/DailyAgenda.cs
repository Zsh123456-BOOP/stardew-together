namespace Together;

// Only commitments and measurements persist. Options are rebuilt from the current world.
public sealed class DailyAgenda {
    public int Day {get;set;}=-1;
    public int CompletedBatches {get;set;}
    public List<string> Priorities {get;set;}=new();
    public List<DailyResource> Resources {get;set;}=new();
    public List<string> History {get;set;}=new();
    public void EnterDay(int day) {
        if(day==Day)return;
        if(Day>=0)History.Add($"第{Day+1}天：实际完成{CompletedBatches}批动作；未完成目标继续以库存核验。");
        if(History.Count>7)History.RemoveRange(0,History.Count-7);
        Day=day;CompletedBatches=0;
    }
}
public sealed class DailyResource {
    public string Item {get;set;}="";
    public int Count {get;set;}
    public string Purpose {get;set;}="";
}
public static class DailyBudget {
    public const int EnergyReserve=0;
    public static int Minutes(int time)=>time/100*60+time%100;
    public static int WorkMinutes(int time,int returnReserve)=>Math.Max(0,25*60-Minutes(time)-returnReserve);
    public static bool Fits(int time,float stamina,int reserve,int duration,float energy)=>WorkMinutes(time,reserve)>=duration && (energy<=0?stamina>=0:stamina-energy>=EnergyReserve);
}
