namespace Together;
// Count at the execution boundary, not in a periodically sampled observer.
public sealed class MenuEffectWatch {
    private readonly Dictionary<(string Token,string Id),int> failures=new();
    public int Observe(string token,string id,bool changed) {
        var key=(token,id);
        if(changed){failures.Clear();return 0;}
        int count=failures.GetValueOrDefault(key)+1;failures[key]=count;return count;
    }
}
