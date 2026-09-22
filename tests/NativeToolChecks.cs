using System.Text.Json;
using Together;
public static class NativeToolChecks {
    private static JsonElement J(object value)=>JsonSerializer.SerializeToElement(value,AgentJson.Options);
    public static void Run(Action<bool,string> check) {
        var catalog=new Dictionary<string,string>{{"work.run","{goal:water|wood,count?:int,additional?:bool}: 原生劳动"},{"context.read","{section:string}: 只读"},{"plan.submit","{tasks:[{id:string,args:{},after:[string]}]}: 排队"}};
        object Call(string id,string name,string args)=>new{id,type="function",function=new{name,arguments=args}};
        JsonElement Choice(params object[] calls)=>J(new{finish_reason="tool_calls",message=new{role="assistant",content=(string?)null,tool_calls=calls}});
        var choice=Choice(Call("a","context__read","{\"section\":\"assets\"}"),Call("b","work__run","{\"goal\":\"wood\",\"count\":3,\"_depends_on_query\":[\"a\"],\"_plan\":\"先核验仓储\"}"));
        var turn=NativeToolProtocol.Decode(choice,catalog);
        check(turn.calls[1].tool=="work.run"&&turn.calls[1].id=="b"&&turn.calls[1].depends_on_query.SequenceEqual(new[]{"a"})&&!turn.calls[1].args.TryGetProperty("_plan",out _)&&turn.plan=="先核验仓储","native protocol preserves IDs/dependencies and separates metadata from executor arguments");
        bool Reject(JsonElement c){try{NativeToolProtocol.Decode(c,catalog);return false;}catch{return true;}}
        check(Reject(Choice(Call("a","work__run","{\"count\":\"3\"}"))),"native typed numeric arguments reject strings before execution");
        check(Reject(Choice(Call("a","work__run","{}"),Call("a","context__read","{}"))),"duplicate native call IDs rejected as a whole batch");
        check(Reject(Choice(Call("a","unknown__tool","{}"))),"native tools outside current definitions rejected");
        check(Reject(Choice(Call("a","work__run","{} trailing")))&&Reject(J(new{finish_reason="length",message=choice.GetProperty("message")})),"malformed and truncated native responses never execute partial calls");
        var definitions=J(NativeToolProtocol.Definitions(catalog));
        check(definitions[2].GetProperty("function").GetProperty("parameters").GetProperty("properties").GetProperty("tasks").GetProperty("items").GetProperty("properties").GetProperty("args").GetProperty("type").GetString()=="object","nested task objects retained in native parameter schema");
        check(Reject(Choice(Call("a","work__run","{\"goal\":\"social\"}")))&&Reject(Choice(Call("a","context__read","{}"))),"native enums and required fields reject unsupported goal and missing query before dispatch");
        check(Reject(Choice(Call("a","plan__submit","{\"tasks\":[{\"id\":\"x\",\"args\":{\"goal\":\"social\"},\"after\":[],\"tool\":\"work.run\"}]}"))),"nested plan actions receive the same loaded-tool and argument validation");
        var questTools=AgentToolDiscovery.Select(new Dictionary<string,string>{{"player.social","native social contract"},{"progress.read","read contract"}},J(new{progression=new{active_quests=new[]{new{social=new{tool="player.social",quest_id="9"}}}}}));
        check(questTools.ContainsKey("player.social")&&!questTools.ContainsKey("progress.read"),"native quest action is discoverable without adding redundant progress reads");
        var exchange=new NativeToolExchange();exchange.Begin(choice.GetProperty("message").GetRawText());exchange.Record("a",new{status="observed",result_id="q1"});
        check(exchange.Messages().Length==0,"partial exchange never sends unmatched API tool calls");
        exchange.CancelPending("query_draft_not_executed");exchange=J(exchange).Deserialize<NativeToolExchange>()!;
        var messages=J(exchange.Messages());check(messages.GetArrayLength()==3&&messages[2].GetProperty("tool_call_id").GetString()=="b"&&messages[2].GetProperty("content").GetString()!.Contains("not_executed"),"checkpoint preserves native feedback IDs and cancelled calls remain explicitly unexecuted");
        exchange.Begin(AgentJson.Encode(new{role="assistant",content=(string?)null,tool_calls=new[]{Call("q","work__run","{}")}}));exchange.Record("q",new{status="queued",task_id="actual-task"});
        check(J(exchange.Messages())[1].GetProperty("content").GetString()!.Contains("queued")&&!AgentJson.Encode(exchange.Messages()).Contains("succeeded"),"queued acknowledgement never turns into completed native work");
        exchange.Reject(choice.GetProperty("message").GetRawText(),"native_tool_not_loaded:farm__select_seeds");
        var rejected=J(exchange.Messages());
        check(rejected.GetArrayLength()==3&&rejected[1].GetProperty("tool_call_id").GetString()=="a"&&rejected[2].GetProperty("content").GetString()!.Contains("batch_rejected")&&!AgentJson.Encode(rejected).Contains("actual-task"),"rejected native batch replaces stale successful feedback and marks every call unexecuted");
        exchange.Reject(Choice(Call("same","work__run","{}"),Call("same","work__run","{}")).GetProperty("message").GetRawText(),"duplicate");
        check(exchange.Messages().Length==0,"duplicate-ID rejected envelope is not replayed to the model API");
        string rejection="";try{NativeToolProtocol.Decode(Choice(Call("x","work__run","{\"goal\":\"social\"}")),catalog);}catch(Exception e){rejection=e.Message;}
        check(rejection.Contains("work.run.goal:allowed=")&&rejection.Contains("water"),"enum rejection identifies the invalid field and actionable permitted values");
        var limits=J(new{count=10,budget=200,max_unit_price=20,keep_gold=100});
        check(OperatingDecisionPolicy.CanCompletePurchaseVisit(limits,new[]{"SeedShop"})&&!OperatingDecisionPolicy.CanCompletePurchaseVisit(limits,new[]{"SeedShop","Other"})&&!OperatingDecisionPolicy.CanCompletePurchaseVisit(J(new{count=10,budget=200}),new[]{"SeedShop"}),"purchase preflight requires unique native seller and explicit quantity, price and reserves");
        check(!OperatingDecisionPolicy.CanCompletePurchaseVisit(J(new{count=10,budget=200,max_unit_price=20,keep_gold=100,currency=4}),new[]{"SeedShop"}),"purchase preflight does not reinterpret non-gold currencies");
        check(OperatingDecisionPolicy.ReuseStorage(1,true,false)&&!OperatingDecisionPolicy.ReuseStorage(1,true,true)&&!OperatingDecisionPolicy.ReuseStorage(1,false,false),"storage reuses available capacity while preserving explicit expansion and real shortfall");
        var labor=J(OperatingDecisionPolicy.Labor(10,270,0,0,35,20,0));
        check(labor.GetProperty("next_dry_day_water_estimate").GetDouble()==70&&labor.GetProperty("today_uncommitted_estimate").GetDouble()==10&&labor.GetProperty("new_plot_base_till_and_water").GetDouble()==4,"next dry day care and new plot cost remain visible without hidden reserve or planting cap");
        var observedExchange=new NativeToolExchange();observedExchange.Begin(choice.GetProperty("message").GetRawText());observedExchange.Record("a",new{result_id="query-one",result=new{storage_count=1}});observedExchange.CancelPending("draft");
        var feedbackView=JsonSerializer.Deserialize<JsonElement>(ContextBudget.Pack(new{now=new{day=0},native_tool_exchange=observedExchange.Messages(),pending_queries=new[]{new{result_id="query-one",call_id="a",tool="context.read",kind="query",args=new{section="assets"},result=new{storage_count=1}}}},8000).Json);
        check(feedbackView.GetProperty("pending_queries")[0].GetProperty("result").GetProperty("tool_call_id").GetString()=="a"&&AgentJson.Encode(observedExchange.Messages()).Contains("storage_count"),"actual query observation is delivered once in native feedback with a traceable outbox reference");
        var packed=ContextBudget.Pack(new{now=new{day=0},assets=new{storage=new[]{new{count=1}}},labor_budget=labor,native_tool_exchange=messages,operating_candidates=Array.Empty<object>(),pending_queries=new[]{new{result_id="first",tool="context.read",kind="query",args=new{section="operating_candidates"},result=Array.Empty<object>()}}},8000);
        var view=JsonSerializer.Deserialize<JsonElement>(packed.Json);
        check(!view.TryGetProperty("native_tool_exchange",out _)&&view.GetProperty("assets").GetProperty("storage").GetArrayLength()==1&&view.GetProperty("pending_queries")[0].GetProperty("result").GetProperty("current_field").GetString()=="operating_candidates","native history is not duplicated in user snapshot; current assets and identical query reference survive");
    }
}
