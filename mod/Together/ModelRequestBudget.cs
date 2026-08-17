using System.Net.Http;
using System.Text;
using System.Text.Json;

namespace Together;

// Immutable per-day ledger identity: a request crossing midnight charges the
// day it started. Failed/aborted requests retain their reservation until usage
// is known. Reading an older save never rolls back the external cost ledger.
public sealed class ModelRequestBudget {
    public sealed class Ledger {
        public long Charged {get;set;}
        public long Reported {get;set;}
        public long Unconfirmed {get;set;}
        public int Requests {get;set;}
    }
    private static readonly object Gate=new();
    private static ModelRequestBudget? current;
    private readonly string path;
    private readonly long limit;
    private readonly int bytesLimit;
    private ModelRequestBudget(string path,long limit,int bytesLimit){this.path=path;this.limit=limit;this.bytesLimit=bytesLimit;}
    public static void Configure(string path,long limit,int bytesLimit) {
        lock(Gate)current=new(path,Math.Clamp(limit,10000,100000000),Math.Clamp(bytesLimit,16000,1000000));
    }
    public static string Display() {
        lock(Gate) {
            if(current==null)return "尚未调用模型";
            try{var l=current.Read();return $"今日 {l.Requests} 次 · 已用 {l.Reported:N0} Token · 待核销 {l.Unconfirmed:N0} · 预算余量 {Math.Max(0,current.limit-l.Charged):N0}";}
            catch{return "用量记录读取失败，已停止新请求";}
        }
    }
    public static object Status() {
        lock(Gate) {
            if(current==null)return new{configured=false};
            try{var l=current.Read();return new{configured=true,charged=l.Charged,reported_tokens=l.Reported,unconfirmed_reserved=l.Unconfirmed,requests=l.Requests,limit=current.limit,remaining=Math.Max(0,current.limit-l.Charged),request_bytes_limit=current.bytesLimit};}
            catch{return new{configured=true,error="usage_ledger_unreadable_requests_blocked"};}
        }
    }
    private Ledger Read()=>File.Exists(path)?JsonSerializer.Deserialize<Ledger>(File.ReadAllText(path))??throw new IOException("empty_usage_ledger"):new();
    private void Write(Ledger ledger) {
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);File.WriteAllText(path+".tmp",JsonSerializer.Serialize(ledger));File.Move(path+".tmp",path,true);
    }
    private void Reserve(long bound) {
        lock(Gate){var l=Read();if(bound>limit-l.Charged)throw new InvalidOperationException("daily_model_token_reservation_budget_exceeded");l.Charged+=bound;l.Unconfirmed+=bound;l.Requests++;Write(l);}
    }
    private void Reconcile(long bound,long reported) {
        lock(Gate){var l=Read();l.Charged=Math.Max(0,l.Charged-bound)+reported;l.Unconfirmed=Math.Max(0,l.Unconfirmed-bound);l.Reported+=reported;Write(l);}
    }
    public static async Task<HttpResponseMessage> SendAsync(HttpClient client,HttpRequestMessage request,CancellationToken cancellation=default) {
        ModelRequestBudget budget;lock(Gate)budget=current??throw new InvalidOperationException("model_budget_not_initialized");
        string body=request.Content==null?"":await request.Content.ReadAsStringAsync(cancellation);
        int bytes=Encoding.UTF8.GetByteCount(body);if(bytes>budget.bytesLimit)throw new InvalidOperationException("model_context_byte_budget_exceeded_use_smaller_queries");
        using var doc=JsonDocument.Parse(body);int output=doc.RootElement.TryGetProperty("max_tokens",out var max)?max.GetInt32():0;
        if(output<=0)throw new InvalidOperationException("explicit_model_output_limit_required");
        // UTF-8 bytes plus output cap and framing allowance is a deliberately
        // conservative reservation, not an exact tokenizer or currency estimate.
        long bound=(long)bytes+output+8192;budget.Reserve(bound);
        var response=await client.SendAsync(request,cancellation);
        string payload=await response.Content.ReadAsStringAsync(cancellation);
        try {
            using var parsed=JsonDocument.Parse(payload);
            if(parsed.RootElement.TryGetProperty("usage",out var usage)&&usage.TryGetProperty("total_tokens",out var total)&&total.TryGetInt64(out long tokens)&&tokens>=0)
                budget.Reconcile(bound,tokens);
        }catch(JsonException){/* Keep the reservation: no trustworthy usage. */}
        return response;
    }
}
