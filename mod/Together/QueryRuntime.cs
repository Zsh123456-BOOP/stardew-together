using System.Text.Json;
using StardewValley;
namespace Together;

public sealed partial class ModEntry {
    private string[] queryRequestIds=Array.Empty<string>();
    private readonly QueryFactMemo queryFactMemo=new();
    private bool HasPendingQueries=>Data.Autoplay.Memory.Queries.Any(q=>!q.Delivered);
    private object PendingQueries()=>QueryResult.Pending(Data.Autoplay.Memory.Queries);
    private string CaptureObservation(string tool,JsonElement result,string kind="query",string call="",JsonElement args=default) {
        var q=new QueryResult{Tool=tool,Call=call,Kind=kind,Args=args.ValueKind==JsonValueKind.Undefined?JsonSerializer.SerializeToElement(new{}):args.Clone(),Result=result.Clone(),Day=Game1.Date.TotalDays,Time=Game1.timeOfDay};
        Data.Autoplay.Memory.Queries.Add(q);Data.Autoplay.Record("query_completed",AgentJson.Encode(q));IndexQueryEvidence(q.Id);WakeAgent("observation_ready:"+kind);return q.Id;
    }
    private void IndexQueryEvidence(string id) {string bucket=agentSaveEpoch+"-"+Game1.Date.TotalDays;if(memoryArchive!=null&&Data.Autoplay.Memory.Cursors.TryGetValue(bucket,out int cursor))Data.Autoplay.Memory.QueryEvidence[id]=bucket+":"+cursor;}
    private string? CaptureQuery(AgentCall call,JsonElement observed) {
        if(!QueryResult.IsRead(call.tool,call.args))return null;
        if(call.tool=="tools.lookup"&&observed.TryGetProperty("definitions",out var definitions))foreach(var p in definitions.EnumerateObject()) {
            Data.Autoplay.Memory.EquippedTools[p.Name]=Game1.Date.TotalDays;
            Data.Autoplay.Memory.EquippedToolsUntilDecision[p.Name]=Data.Autoplay.Decisions+3;
        }
        if(call.tool=="tools.lookup"&&observed.TryGetProperty("work_profiles",out var profiles)) {
            var memory=Data.Autoplay.Memory;if(memory.WorkProfileDay!=Game1.Date.TotalDays)memory.WorkProfilesUntilDecision.Clear();memory.WorkProfileDay=Game1.Date.TotalDays;
            foreach(var p in profiles.EnumerateArray())memory.WorkProfilesUntilDecision[p.GetString()!]=Data.Autoplay.Decisions+3;
        }
        var memo=queryFactMemo.Observe(call.tool,call.args,observed);
        Data.Autoplay.Record("query_fact_revision",AgentJson.Encode(new{tool=call.tool,args=call.args,revision=memo.Revision,repeated=memo.Repeated,day=Game1.Date.TotalDays,time=Game1.timeOfDay,full_fact_returned=true}));
        return CaptureObservation(call.tool,memo.Fact,"query",call.id,call.args);
    }
    private void AcknowledgeQueries() {
        foreach(var q in Data.Autoplay.Memory.Queries.Where(q=>queryRequestIds.Contains(q.Id)))q.Delivered=true;
        if(queryRequestIds.Length>0)Data.Autoplay.Record("query_results_delivered",AgentJson.Encode(new{result_ids=queryRequestIds,agentRequestDay,note="收到对应HTTP回复，证明请求已送达，不证明正确理解"}));
        var old=Data.Autoplay.Memory.Queries.Where(q=>q.Delivered).SkipLast(32).Select(q=>q.Id).ToHashSet();
        foreach(var q in Data.Autoplay.Memory.Queries.Where(q=>old.Contains(q.Id)&&!Data.Autoplay.Memory.QueryEvidence.ContainsKey(q.Id))) {Data.Autoplay.Record("query_archived",AgentJson.Encode(q));IndexQueryEvidence(q.Id);}
        Data.Autoplay.Memory.Queries.RemoveAll(q=>old.Contains(q.Id));queryRequestIds=Array.Empty<string>();
    }
    internal object ReadQueryResult(JsonElement args) {
        string id=AgentToolRegistry.Text(args,"id");int offset=Math.Max(0,AgentToolRegistry.Number(args,"offset",0));
        if(id.Length==0){var rows=Data.Autoplay.Memory.Queries.Where(q=>!q.Delivered).ToArray();return new{total=rows.Length,offset,results=rows.Skip(offset).Take(12).Select(q=>new{result_id=q.Id,q.Tool,q.Kind,q.Day,q.Time}),next_offset=offset+12<rows.Length?(int?)(offset+12):null};}
        var found=Data.Autoplay.Memory.Queries.FirstOrDefault(q=>q.Id==id);
        if(found==null&&Data.Autoplay.Memory.QueryEvidence.TryGetValue(id,out var evidence)&&memoryArchive!=null)found=memoryArchive.Query(evidence,id);
        return found?.Page(offset,AgentToolRegistry.Number(args,"count",12))??new{status="not_found",note="旧版未建索引的回执可用memory.search以result_id检索query_completed，再memory.evidence续读"};
    }
    internal object ReadContextSection(JsonElement args) {
        if(args.TryGetProperty("sections",out var batch)) {
            if(args.TryGetProperty("section",out _)||batch.ValueKind!=JsonValueKind.Array||batch.GetArrayLength() is <1 or >4)throw new InvalidOperationException("context_sections_1_to_4_or_single_section");
            var names=batch.EnumerateArray().Select(v=>v.GetString()??"").Distinct().ToArray();
            return names.ToDictionary(n=>n,n=>ReadContextSection(JsonSerializer.SerializeToElement(new{section=n})));
        }
        return AgentToolRegistry.Text(args,"section") switch {
        "planting_execution"=>PlantingExecutionFacts(),"assets"=>FacilityAssets(),"labor_budget"=>FarmLaborBudget(),"farm_cleanup"=>FarmMaintenanceSummary(),"service_hours"=>KnownServiceHours(),"inventory_plan"=>InventoryPlanning(),"companions"=>AgentCompanions(),
        "progression"=>DailyProgressDigest(true),"business"=>ReadBusiness(JsonSerializer.SerializeToElement(new{})),"day"=>AgentDay(),"schedule"=>AgentPlanRead(),
        "earlier_observation_summaries" or "recent"=>RecentAgentContext(),"memory"=>AgentMemoryContext(),"goals"=>GoalContext(),"operating_candidates"=>OperatingOpportunities(),
        "sleep_review"=>sleepReview??new{},"plan"=>new{Data.Autoplay.Plan},"task_card"=>TaskCard(),"prerequisites"=>TaskPrerequisites(),_=>throw new InvalidOperationException("unknown_context_section")
        };
    }
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
