namespace Together;

public sealed record SeedRequest(string Item,int Count,int Price,int Stock);
public static class AutonomyPolicy {
    // -1 is absence of a model-imposed daily ceiling, not negative cash.
    public static int Cash(int cash,int keep,int daily,int spent,int pending,int development=0)=>
        Math.Max(0,Math.Min(Math.Max(0,cash-Math.Max(0,keep)-Math.Max(0,pending)-Math.Max(0,development)),daily<0?int.MaxValue:Math.Max(0,daily-spent-pending)));
    // Preserve the model's item order; shrinking is explicit in the receipt.
    public static Dictionary<string,int> Seeds(IEnumerable<SeedRequest> requests,int allowance) {
        var result=new Dictionary<string,int>();
        foreach(var row in requests) {
            if(row.Count<1||row.Price<0||row.Stock<0||result.ContainsKey(row.Item))throw new InvalidOperationException("invalid_seed_request");
            int n=Math.Min(row.Count,row.Stock);if(row.Price>0)n=Math.Min(n,allowance/row.Price);
            result[row.Item]=n;allowance-=n*row.Price;
        }
        return result;
    }
    public static string InvestmentState(string phase,string error)=>phase switch {
        "done" when error.Length>0=>"deferred",
        "done"=>"completed",
        "blocked"=>"blocked",
        "awaiting_selection"=>"awaiting_model_choice",
        "idle"=>"idle",
        _=>"executing"
    };
}
