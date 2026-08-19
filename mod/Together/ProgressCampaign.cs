namespace Together;
public sealed class ProgressCampaign {
    public bool Enabled {get;set;}
    public List<ProgressPursuit> Targets {get;set;}=new();
}
public sealed class ProgressPursuit {
    public string Target {get;set;}="";
    public string State {get;set;}="pending";
    public string Reason {get;set;}="";
    public string ChildGoal {get;set;}="";
    public string Fingerprint {get;set;}="";
    public int Revision {get;set;}
}
