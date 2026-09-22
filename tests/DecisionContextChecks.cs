using System.Text.Json;
using Together;
public static class DecisionContextChecks {
    private static JsonElement J(object v)=>JsonSerializer.SerializeToElement(v,AgentJson.Options);
    public static void Replay(string path) {
        var input=JsonSerializer.Deserialize<JsonElement>(File.ReadAllText(path));
        var catalog=input.GetProperty("catalog").Deserialize<Dictionary<string,string>>()!;var rows=new List<object>();
        foreach(var row in input.GetProperty("requests").EnumerateArray()) {
            var context=row.GetProperty("context");var selected=AgentToolDiscovery.Select(catalog,context);
            int reserved=ContextBudget.Estimate(AgentJson.Encode(selected))+2200;
            try {
                var packed=ContextBudget.Pack(context,30000-reserved,Math.Max(256,16000-reserved));
                rows.Add(new{id=row.GetProperty("id"),before_context_chars=context.GetRawText().Length,after_context_chars=packed.Json.Length,tools=selected.Keys,tools_chars=AgentJson.Encode(selected).Length,estimated_context_tokens=packed.EstimatedTokens,status="packed"});
            }catch(Exception e){rows.Add(new{id=row.GetProperty("id"),status="failed",error=e.Message});}
        }
        Console.WriteLine(AgentJson.Encode(rows));
    }
    public static void Run(Action<bool,string> check) {
        var source=J(new{now=new{day=1,time=1000},inventory=new{items=Array.Empty<object>()},schedule=new{tasks=Array.Empty<object>()},goal="农场经营",task_card=new{Goal="农场经营"},pending_queries=new object[]{
            new{result_id="old-plan",kind="query",tool="context.read",args=new{section="plan"},result=new{Plan="6 tasks queued"}},
            new{result_id="old-schedule",kind="query",tool="plan.read",args=new{},result=new{tasks=new[]{new{state="running"}}}},
            new{result_id="failure",kind="outcome",tool="work.run",args=new{goal="wood",location="Farm"},result=new{status="failed",requested=20,completed=0,remaining=20,stop_reason="no_matching_targets"}}},plan="old narrative"});
        var packed=ContextBudget.Pack(source,10000);var view=JsonSerializer.Deserialize<JsonElement>(packed.Json);
        check(!packed.Json.Contains("6 tasks queued")&&!packed.Json.Contains("\"state\":\"running\"")&&view.GetProperty("pending_queries")[1].GetProperty("result").GetProperty("current_field").GetString()=="schedule","fresh empty queue supersedes old running and narrative snapshots");
        var failure=view.GetProperty("pending_queries")[2];check(failure.GetProperty("result").GetProperty("completed").GetInt32()==0&&failure.GetProperty("args").GetProperty("location").GetString()=="Farm"&&failure.GetProperty("result_id").GetString()=="failure","event identity, zero progress, remaining work and scoped arguments survive projection");
        check(source.GetProperty("pending_queries")[0].GetProperty("result").GetProperty("Plan").GetString()=="6 tasks queued","projection leaves archived raw source unchanged");
        var catalog=AgentToolDiscovery.CoreNames.Concat(AgentToolDiscovery.Groups.SelectMany(g=>g.Value)).Append("player.arcade").Append("knowledge.get").Distinct().ToDictionary(n=>n,n=>"complete-schema:"+n);
        var selected=AgentToolDiscovery.Select(catalog,J(new{now=new{day=1,location="Farm"},ui=new{type="none"},equipped_tools=new[]{"player.arcade"},operating_candidates=new object[]{new{Tool="goal.create",Args=new{entity="craft:Chest"}},new{Tool="work.run",Args=new{goal="plant"}},new{Tool="player.buy",Args=new{}}}}));
        check(selected.ContainsKey("goal.create")&&selected.ContainsKey("farm.plan")&&selected.ContainsKey("player.buy")&&selected.ContainsKey("player.arcade"),"multiple candidate domains and explicit lookup tools remain available together");
        check(selected.All(p=>p.Value==catalog[p.Key])&&selected.Keys.Take(AgentToolDiscovery.CoreNames.Length).SequenceEqual(AgentToolDiscovery.CoreNames),"complete schemas and stable prefix preserved without truncation");
        var minimal=AgentToolDiscovery.Select(catalog,J(new{now=new{day=2,location="Farm"},ui=new{type="none"}}));
        check(minimal.Count==AgentToolDiscovery.CoreNames.Length&&!minimal.ContainsKey("player.arcade"),"expired irrelevant groups leave the next day base view");
        var lookup=J(AgentToolDiscovery.Lookup(catalog,J(new{group="trade"})));
        check(lookup.GetProperty("definitions").EnumerateObject().Count()==AgentToolDiscovery.Groups["trade"].Length,"group lookup expands to whole executable tool contracts");
        var active=AgentToolDiscovery.Select(catalog,J(new{now=new{day=3},schedule=new{tasks=new[]{new{tool="player.arcade",state="running"}}}}));
        check(active.ContainsKey("player.arcade"),"active tasks retain tools after same-day lease expires");
        var menu=AgentToolDiscovery.Select(catalog,J(new{now=new{day=1},ui=new{type="NamingMenu",token="real"}}));
        check(menu.ContainsKey("menu.text")&&menu.ContainsKey("menu.choose"),"blocking native menu carries text and choice handlers");
    }
}
