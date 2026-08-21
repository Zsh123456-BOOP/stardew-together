namespace Together;
public sealed class ProgressCampaign {
    public bool Enabled {get;set;}
    public List<ProgressPursuit> Targets {get;set;}=new();
    public int BudgetPerDay {get;set;}
    public int KeepGold {get;set;}=500;
    public int BudgetDay {get;set;}=-1;
    public int ReservedGold {get;set;}
    public int NutsPerDay {get;set;}
    public int KeepNuts {get;set;}
    public int ReservedNuts {get;set;}
    public string Route {get;set;}="";
}
public sealed class ProgressPursuit {
    public string Target {get;set;}="";
    public string State {get;set;}="pending";
    public string Reason {get;set;}="";
    public string ChildGoal {get;set;}="";
    public string Fingerprint {get;set;}="";
    public int Revision {get;set;}
    public List<string> Tasks {get;set;}=new();
    public int AttemptsDay {get;set;}=-1;
    public int Attempts {get;set;}
    public bool CompletionObserved {get;set;}
}
