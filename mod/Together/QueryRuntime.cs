using System.Text.Json;
using StardewValley;
namespace Together;

public sealed partial class ModEntry {
    private string[] queryRequestIds=Array.Empty<string>();
    private bool HasPendingQueries=>Data.Autoplay.Memory.Queries.Any(q=>!q.Delivered);
    private object PendingQueries()=>Data.Autoplay.Memory.Queries.Where(q=>!q.Delivered).Select(q=>new{result_id=q.Id,call_id=q.Call,tool=q.Tool,args=q.Args,observed_day=q.Day,result=q.Result.GetRawText().Length<=1800?(object)q.Result:q.Page(),delivery="pending",note="历史查询结果；交易仍核验实时资金/库存/菜单"}).ToArray();
    private string? CaptureQuery(AgentCall call,JsonElement observed) {
        if(!QueryResult.IsRead(call.tool))return null;
        var q=new QueryResult{Call=call.id,Tool=call.tool,Args=call.args.Clone(),Result=observed.Clone(),Day=Game1.Date.TotalDays};
        Data.Autoplay.Memory.Queries.Add(q);
        Data.Autoplay.Record("query_completed",AgentJson.Encode(q));WakeAgent("query_results_ready");return q.Id;
    }
    private void AcknowledgeQueries() {
        foreach(var q in Data.Autoplay.Memory.Queries.Where(q=>queryRequestIds.Contains(q.Id)))q.Delivered=true;
        if(queryRequestIds.Length>0)Data.Autoplay.Record("query_results_delivered",AgentJson.Encode(new{result_ids=queryRequestIds,agentRequestDay,note="收到对应HTTP回复，证明请求已送达，不证明正确理解"}));
        var old=Data.Autoplay.Memory.Queries.Where(q=>q.Delivered).SkipLast(32).Select(q=>q.Id).ToHashSet();
        Data.Autoplay.Memory.Queries.RemoveAll(q=>old.Contains(q.Id));queryRequestIds=Array.Empty<string>();
    }
    internal object ReadQueryResult(JsonElement args)=>Data.Autoplay.Memory.Queries.FirstOrDefault(q=>q.Id==AgentToolRegistry.Text(args,"id"))?.Page(AgentToolRegistry.Number(args,"offset",0),AgentToolRegistry.Number(args,"count",12))??new{status="not_found",note="回执不在当前会话窗口，memory.search可查原始query_completed归档"};
    internal object SearchPlanningKnowledge(JsonElement args) {
        string exact=AgentToolRegistry.Text(args,"id"),kind=AgentToolRegistry.Text(args,"kind","all");
        var queries=args.TryGetProperty("queries",out var batch)&&batch.ValueKind==JsonValueKind.Array?batch.EnumerateArray().Select(v=>v.GetString()??"").ToArray():new[]{AgentToolRegistry.Text(args,"query")};
        if(queries.Length is <1 or >6)throw new InvalidOperationException("knowledge_queries_count_1_to_6");
        int limit=Math.Clamp(AgentToolRegistry.Number(args,"limit",5),1,8),offset=Math.Max(0,AgentToolRegistry.Number(args,"offset",0));
        return new{status=Knowledge.Ready?"observed":"loading",Knowledge.Revision,scope=Data.Knowledge.DiscoveredOnly?"discovered":"full",purpose=AgentToolRegistry.Text(args,"purpose"),results=queries.Distinct().Select(query=>{
            var hits=exact.Length>0?Knowledge.Search(exact,kind,1).Where(h=>h.Entry.Id==exact).ToArray():Knowledge.Search(query,kind,offset+limit+1).ToArray();
            if(hits.Length>0&&hits[0].Score>=650)hits=hits.Where(h=>h.Score>=650).ToArray();
            var page=hits.Skip(offset).Take(limit).ToArray();
            return new{query,status=!Knowledge.Ready?"loading":page.Length==0?"not_found":"observed",matches=page.Select(h=>new{h.Entry.Id,h.Entry.Name,h.Entry.Kind,h.Entry.Description,h.Entry.Source,h.Score,h.Reason,read=new{tool="knowledge.get",args=new{id=h.Entry.Id}}}),next_offset=hits.Length>offset+limit?(int?)(offset+limit):null,note="规则资料，不是原生商店报价或已完成经历"};
        }).ToArray()};
    }
}
