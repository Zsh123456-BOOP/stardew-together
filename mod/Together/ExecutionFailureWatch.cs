namespace Together;
// Mirrors the audit counter at the point of execution, before another invocation.
public sealed class ExecutionFailureWatch {
    private string run="";
    private readonly HashSet<string> seen=new();
    private readonly Dictionary<(string Actor,string Root,long Version),int> counts=new();
    public int Observe(string runId,string attempt,string actor,string root,long version) {
        if(run!=runId){run=runId;seen.Clear();counts.Clear();}
        if(string.IsNullOrEmpty(root)||string.IsNullOrEmpty(attempt)||!seen.Add(attempt))return 0;
        const string prefix="known_failure_conditions_unchanged:";
        if(root.StartsWith(prefix)){root=root[prefix.Length..];int evidence=root.IndexOf(":evidence=",StringComparison.Ordinal);if(evidence>=0)root=root[..evidence];}
        if(root.StartsWith("capacity:")||root.StartsWith("capacity_relief:"))root="capacity_all_candidates_infeasible";
        var key=(actor,root,version);return counts[key]=counts.GetValueOrDefault(key)+1;
    }
}
