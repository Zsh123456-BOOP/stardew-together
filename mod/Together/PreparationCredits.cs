namespace Together;
// Detached predicted outputs. Never a source for actual stock, capacity or consumption.
public sealed class PreparationCredits<T> {
    private readonly List<(T Item,int Count)> credits=new();
    public void Produce(T item,int count){if(count>0)credits.Add((item,count));}
    public int Require(int count,Func<T,bool> accepts) {
        int left=count;
        for(int i=0;i<credits.Count&&left>0;i++)if(accepts(credits[i].Item)) {
            int used=Math.Min(left,credits[i].Count);left-=used;credits[i]=(credits[i].Item,credits[i].Count-used);
        }
        return left;
    }
}
