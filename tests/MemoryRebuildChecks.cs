using System.Text.Json;
using Together;
public static class MemoryRebuildChecks {
    public static void Replay(string path) {
        int count=0,failures=0;long before=0,after=0;var rejected=new List<object>();
        foreach(var line in File.ReadLines(path)) {
            using var row=JsonDocument.Parse(line);
            if(row.RootElement.GetProperty("kind").GetString()!="request")continue;
            using var body=JsonDocument.Parse(row.RootElement.GetProperty("payload").GetProperty("body").GetString()!);
            var messages=body.RootElement.GetProperty("messages");string system=messages[0].GetProperty("content").GetString()!,text=messages[1].GetProperty("content").GetString()!;
            try {using var context=JsonDocument.Parse(text);var result=ContextBudget.Pack(context.RootElement,30000-ContextBudget.Estimate(system)-256,24000-ContextBudget.Estimate(system)-256);before+=ContextBudget.Estimate(text);after+=result.EstimatedTokens;count++;}
            catch(InvalidOperationException e){failures++;using var c=JsonDocument.Parse(text);rejected.Add(new{error=e.Message,sections=c.RootElement.EnumerateObject().OrderByDescending(p=>p.Value.GetRawText().Length).Take(5).Select(p=>new{p.Name,chars=p.Value.GetRawText().Length}).ToArray(),recent=c.RootElement.TryGetProperty("recent",out var recent)?recent.GetRawText()[..Math.Min(recent.GetRawText().Length,180)]:""});}
        }
        Console.WriteLine(AgentJson.Encode(new{count,failures,rejected,before_estimated_tokens=before,after_estimated_tokens=after,reduction=before==0?0:1-after/(double)before,actual_token_savings="not measured; historical context packing replay only"}));
        if(failures>0)Environment.ExitCode=1;
    }
    public static void Run(Action<bool,string> check) {
        string reflection=AgentJson.Encode(new{plan="summary",speech="",calls=new[]{new{tool="memory.summary",args=new{summary="存货失败，待条件变化后再试。",evidence=new[]{"e:1"}}}}});
        check(MemoryReflection.Parse(reflection,6,new HashSet<string>{"e:1"}).Authority=="model_derived_not_native_state","model summary remains derived evidence, never native completion");
        bool invented=false;try{MemoryReflection.Parse(reflection,6,new HashSet<string>{"different:1"});}catch(InvalidOperationException){invented=true;}
        check(invented,"invented summary evidence is rejected");
        var raw=new ArchivedMemory("epoch-6:1",1,6,"player","action_result","{\"status\":\"failed\",\"goal\":\"store\",\"location\":\"Farm\",\"error\":\"capacity_all_candidates_infeasible\"}");
        var memory=MemoryProjector.Project(raw)!;
        check(memory.Evidence==raw.Id&&memory.Status=="failed"&&memory.Entities.Contains("Farm"),"memory retains native provenance, failure and entity IDs");
        check(MemoryProjector.Project(raw with{Kind="decision"})==null,"model intention cannot become factual memory");
        check(!MemoryRetriever.Rank(new[]{memory},"Farm",5).Any(),"future days are excluded from memory retrieval");
        check(MemoryRetriever.Rank(new[]{memory},"Farm",6).Single().Score>=200,"exact entity recall outranks generic recency");
        check(!MemoryRetriever.Rank(new[]{memory},"unrelated",6).Any(),"unrelated recent events are not injected as relevant memory");
        string root=Path.Combine(Path.GetTempPath(),"together-memory-v2-"+Guid.NewGuid().ToString("N"));
        try {
            var checkpoint=new MemoryCheckpoint();var archive=new MemoryArchive(root,"epoch",checkpoint);
            archive.Append(6,"player","action_result",raw.Text);
            var saved=JsonSerializer.Deserialize<MemoryCheckpoint>(AgentJson.Encode(checkpoint))!;
            archive.Append(7,"player","action_result",raw.Text);
            var restored=new MemoryArchive(root,"epoch",saved);
            using var recall=JsonDocument.Parse(AgentJson.Encode(restored.Recall("Farm",7)));
            check(recall.RootElement.GetProperty("entries").GetArrayLength()==1,"index survives save reload without exposing future events");
            restored.ForgetActor("player");
            check(JsonSerializer.SerializeToElement(restored.Recall("Farm",7)).GetProperty("entries").GetArrayLength()==0,"forgotten generation excluded from automatic recall");
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        var packed=ContextBudget.Pack(new{task_card=new{Goal="七天正常过夜，不得丢失目标",TrialTargetSleeps=7},inventory=new{gold=500},ui=new{token="native-menu-token"},memory=new{large=new string('甲',15000)},recent=new[]{new{data=new{tool="work.run",result=new{status="succeeded",evidence=new string('x',12000)}}}}},2400);
        check(packed.EstimatedTokens<=2400&&packed.Json.Contains("native-menu-token")&&packed.Json.Contains("TrialTargetSleeps"),"bounded context retains exact menu and task checkpoint");
        bool blocked=false;try{ContextBudget.Pack(new{task_card=new{Goal=new string('甲',2000)}},256);}catch(InvalidOperationException){blocked=true;}
        check(blocked,"essential state cannot be silently truncated to fit a budget");
        var queries=Enumerable.Range(0,6).Select(i=>new QueryResult{Tool="knowledge.search",Args=JsonSerializer.SerializeToElement(new{query="q"+i}),Result=JsonSerializer.SerializeToElement(new{answer="answer"+i})}).ToArray();
        var delivered=ContextBudget.Pack(new{pending_queries=queries,task_card=new{Goal="query then plant"},memory=new{large=new string('甲',8000)}},6000);
        check(queries.All(q=>delivered.Json.Contains(q.Id)),"all six undelivered results survive budget reduction");
        check(JsonSerializer.Deserialize<QueryResult[]>(AgentJson.Encode(queries))!.All(q=>!q.Delivered),"query receipts survive reload without invented delivery");
        var turn=AgentTurn.Parse("{\"plan\":\"query then decide\",\"calls\":[{\"tool\":\"player.buy\",\"args\":{},\"depends_on_query\":[\"quote\"]}]}");
        check(turn.calls[0].depends_on_query.Single()=="quote","query-dependent drafts remain explicit, not executable assumptions");
        var draft=JsonSerializer.SerializeToElement(new{call=turn.calls[0]});
        var proof=new QueryResult{Id="quote-result",Call="quote",Delivered=true};
        check(QueryResult.ResolvesDraft(draft,"player.buy",new[]{"quote-result"},new[]{proof})&&!QueryResult.ResolvesDraft(draft,"player.buy",new[]{"unrelated"},new[]{proof}),"resolving one purchase draft preserves unrelated purchase drafts");
        check(SeedPossibilities.Resolve("(O)770",0).SequenceEqual(new[]{"472","474","475"}),"spring mixed seeds match native remap without RNG");
        check(SeedPossibilities.Resolve("770",3).Length==10&&SeedPossibilities.Resolve("770",0,true).Contains("833"),"winter union and island mixed seed outcomes are explicit");
        var contamination=MemoryProjector.Project(raw with{Text="{\"tool\":\"player.social\",\"args\":{\"npc\":\"Willy\"},\"result\":{\"status\":\"failed\",\"alternatives\":[{\"location\":\"SeedShop\"}],\"evidence\":{\"location\":\"FishShop\"}}}"})!;
        check(contamination.Entities.Contains("Willy")&&!contamination.Entities.Contains("SeedShop")&&!contamination.Entities.Contains("FishShop"),"social failure identity excludes diagnostic alternatives and unrelated shops");
        var jelly=new StackKey("RiverJelly",0,"");var bag=new CapacitySnapshot(12,new[]{(jelly,2,999,false)});
        var candidates=new[]{new CapacityCandidate("store:Farm",5,0,true,"",new CapacityOp[]{new TakeOp(jelly,1)})};
        check(CapacityRelief.Choose(bag,candidates,0).Selected==null,"capacity relief still requires an entire free slot");
        check(CapacityRelief.Choose(bag,candidates,0,delivery:true).Selected=="store:Farm","ordinary storage permits partial stack while retaining food");
        check(DecisionBarrier.CanFollowFailure("player.sleep")&&!DecisionBarrier.CanFollowFailure("player.buy"),"independent bedtime survives optional storage failure without releasing purchase dependencies");
    }
}
