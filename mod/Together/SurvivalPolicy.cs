namespace Together;

// Checkpointed controller state, never a substitute for native save/day evidence.
public sealed class SurvivalState {
    public string Mode {get;set;}="model";
    public string Reason {get;set;}="";
    public int Day {get;set;}=-1;
    public int ModelFailures {get;set;}
    public int SleepAttempts {get;set;}
    public int Resumes {get;set;}
    public bool AutoResume {get;set;}
    public bool NativePlayerOnly {get;set;}
    public int LastSavedDay {get;set;}=-1;
    public RoutinePolicy? RoutineBeforeFallback {get;set;}
    public HashSet<string> Abandoned {get;set;}=new();
    public bool NewDay(int day) {
        if(Day==day)return false;
        Day=day;Mode="model";Reason="";ModelFailures=0;SleepAttempts=0;Abandoned.Clear();return true;
    }
    public bool ModelFailed()=>++ModelFailures>=3;
    public static int GuardMinute(int returnMinutes)=>26*60-Math.Max(60,returnMinutes);
    public static bool NightGuard(int time,bool effectivePlan,int returnMinutes=60)=>DailyBudget.Minutes(time)>=GuardMinute(returnMinutes)&&!effectivePlan;
    public static bool Fatal(string code)=>code.Contains("conservation")||code.Contains("world_inconsistent")||code.Contains("day_transition_not_verified")||code.Contains("logging_failed")||code.Contains("save_failed")||code.Contains("actor_restore");
}
