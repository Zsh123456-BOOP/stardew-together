namespace Together;

public sealed class CapacityConstraint {
    public string Actor {get;set;}="player";
    public string RootCause {get;set;}="";
    public long CapacityVersion {get;set;}
    public string[] ExcludedCandidates {get;set;}=Array.Empty<string>();
    public string Operation {get;set;}="";
    public int RequiredSlots {get;set;}
    public bool MatchesRelief(string operation,int required,int free,int storable)=>Operation==operation&&(operation!="store"||required>free&&required>=RequiredSlots&&RequiredSlots>0||operation=="store"&&required==0&&RequiredSlots==0&&storable>0);
    public int Day {get;set;}
    public int Time {get;set;}
}
public sealed class CapacityState {
    public long Version {get;set;}
    public string Fingerprint {get;set;}="";
    public HashSet<string> ApprovedUses {get;set;}=new();
    public List<CapacityConstraint> Constraints {get;set;}=new();
    public bool Observe(string facts) {
        if(Fingerprint==facts)return false;
        Fingerprint=facts;Version++;Constraints.RemoveAll(c=>c.CapacityVersion!=Version);return true;
    }
    public void Block(string actor,string root,int day,int time,string[]? excluded=null) {
        Constraints.RemoveAll(c=>c.Actor==actor&&c.RootCause==root);
        Constraints.Add(new(){Actor=actor,RootCause=root,CapacityVersion=Version,Day=day,Time=time,ExcludedCandidates=excluded??Array.Empty<string>()});
    }
    public bool Blocked(string actor)=>Constraints.Any(c=>c.Actor==actor&&c.CapacityVersion==Version);
    public static bool IsConstraint(string? code)=>IsCapacity(code)||code?.StartsWith("loadout_shape_unsupported:")==true;
    public static bool IsCapacity(string? code)=>code!=null&&code.StartsWith("capacity_");
}
