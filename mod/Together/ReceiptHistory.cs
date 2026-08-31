namespace Together;

// Dictionary enumeration is not insertion order after removals: recycled slots
// can put the newest action first. Retain receipts by explicit creation order.
public sealed class ReceiptHistory<T> where T:class {
    private readonly Dictionary<string,T> values=new();
    private readonly Queue<string> order=new();
    private readonly int capacity;
    public ReceiptHistory(int capacity){this.capacity=Math.Max(1,capacity);}
    public int Count=>values.Count;
    public void Add(string id,T value) {
        if(!values.ContainsKey(id))order.Enqueue(id);
        values[id]=value;
        while(order.Count>capacity)values.Remove(order.Dequeue());
    }
    public bool TryGetValue(string id,out T value)=>values.TryGetValue(id,out value!);
    public void Clear(){values.Clear();order.Clear();}
}
