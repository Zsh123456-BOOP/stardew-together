using System.Text.Json;
using Together;
public static class OnDemandChecks {
    private static JsonElement J(object value)=>JsonSerializer.SerializeToElement(value,AgentJson.Options);
    public static void Run(Action<bool,string> check) {
        JsonElement Achievement(int id,int current,int target,bool earned=false)=>J(AchievementView.Summary(id,"native "+id,earned,J(new{known=true,current,target,condition_satisfied=current>=target})));
        var next=AchievementView.Next(new[]{Achievement(0,20000,15000,true),Achievement(1,20000,50000),Achievement(2,20000,250000),Achievement(24,1,10),Achievement(27,1,100),Achievement(6,1,1),Achievement(7,0,1),Achievement(11,1,4)});
        var ids=next.Select(r=>r.GetProperty("id").GetString()).ToArray();
        check(ids.Contains("achievement:1")&&!ids.Contains("achievement:2")&&ids.Contains("achievement:24")&&ids.Contains("achievement:27"),"achievement next tiers preserve independent income, fish variety and catch routes");
        check(ids.Contains("achievement:6")&&ids.Contains("achievement:7")&&!ids.Contains("achievement:11"),"friendship point thresholds remain separate and condition satisfaction is not awarded achievement");
        var unknown=J(AchievementView.Summary(99,"unknown",false,J(new{known=false})));
        check(!unknown.GetProperty("known").GetBoolean()&&!unknown.TryGetProperty("current",out _)&&AchievementView.Next(new[]{unknown}).Length==1,"unknown achievement is discoverable without a fabricated metric");
        var memo=new QueryFactMemo();var one=memo.Observe("context.read",J(new{section="assets"}),J(new{storage=1}));var two=memo.Observe("context.read",J(new{section="assets"}),J(new{storage=1}));var changed=memo.Observe("context.read",J(new{section="assets"}),J(new{storage=2}));
        check(!one.Repeated&&two.Repeated&&two.Fact.GetProperty("storage").GetInt32()==1&&!changed.Repeated&&changed.Revision!=two.Revision,"memo hits return actual facts and native changes invalidate the content version");
        check(ContextRevisions.Version(J(new{a=1,b=2}))==ContextRevisions.Version(J(new{b=2,a=1})),"canonical query arguments are independent of object property order");
        var farm=ToolSpecs.Work(J(new{active_actors=new[]{"player"}}));
        bool Has(ToolSpec s,string field)=>s.Parameters.GetProperty("properties").TryGetProperty(field,out _);
        check(Has(farm,"stock_target")&&Has(farm,"cleanup_id")&&Has(farm,"additional")&&Has(farm,"exact_quality")&&!Has(farm,"start_level"),"structured work contract retains formerly prose-only routine parameters and omits unrelated mining parameters");
        var mining=ToolSpecs.Work(J(new{active_actors=new[]{"player"},schedule=new{tasks=new[]{new{spec=new{tool="work.run",args=new{goal="mine_trip"}}}}}}));
        check(Has(mining,"start_level")&&Has(mining,"travel_budget")&&Has(mining,"keep_gold")&&mining.Profiles.Contains("mine"),"active task arguments retain specialized work profile after lookup expiry");
        var fish=ToolSpecs.Work(J(new{active_actors=new[]{"player"},work_profiles=new[]{"fish","production"}}));
        check(fish.Profiles.Contains("fish")&&!fish.Profiles.Contains("production"),"explicit lookup adds fishing and single-player context excludes companion-only production");
        object Call(string name,object args)=>new{id="test",type="function",function=new{name,arguments=AgentJson.Encode(args)}};
        JsonElement Choice(object call)=>J(new{finish_reason="tool_calls",message=new{content=(string?)null,tool_calls=new[]{call}}});
        bool Reject(ToolSpec s,object args){try{NativeToolProtocol.Decode(Choice(Call("work__run",args)),new[]{s});return false;}catch(InvalidOperationException){return true;}}
        check(Reject(farm,new{goal="mine_trip",target_level=10})&&Reject(farm,new{goal="wood",start_level=5}),"frozen contract rejects unloaded goals and hidden specialized parameters before any action");
        check(NativeToolProtocol.Decode(Choice(Call("work__run",new{goal="mine_trip",target_level=10,start_level=5,travel_budget=0})),new[]{mining}).calls.Count==1,"loaded mining arguments validate against the same schema sent to the API");
        var all=ToolSpecs.Work(J(new{active_actors=new[]{"player","partner"},work_profiles=ToolSpecs.WorkProfiles.Keys}));
        check(Reject(all,new{goal="plant",actor_id="partner",plan_id="p"})&&Reject(all,new{goal="process",actor_id="player"}),"player and companion-only jobs cannot cross actor boundaries");
        var diary=new ActivityDiary();
        object Bag(int count,int money)=>new{location="SeedShop",money,inventory=new{items=new[]{new{item=new{id="(O)472",quality=0,count}}}}};
        JsonElement Purchase(string id,int before,int after,int cost=60)=>J(new{command_id=id,skill="player.buy",status="succeeded",before=Bag(before,500),after=Bag(after,500-cost),effects=new[]{new{kind="native_purchase",item="(O)472",quality=0,units=3,cost=60,currency=0}}});
        diary.Native(Purchase("a",0,3),0,900);diary.Native(Purchase("b",3,6),0,910);diary.Native(Purchase("b",3,6),0,910);
        check(diary.Rows.Count==1&&diary.Rows[0].Count==6&&diary.Rows[0].Cost==120&&diary.Rows[0].Batches==2&&diary.Rows[0].SupportingEvidence.Count>0,"two real purchases keep their quantities, batches and supporting evidence while duplicate replay and derivative bag/cash rows disappear");
        diary.Native(Purchase("mixed",6,10,70),0,920);
        check(diary.Rows.Any(r=>r.Kind=="行动期间入包"&&r.Count==4)&&diary.Rows.Any(r=>r.Kind=="现金变化"&&r.Count==-70),"unexplained inventory or currency changes remain observations instead of guessed causal merges");
        var source=J(new{now=new{day=0,location="Farm"},prerequisites=new{development=new[]{new{id="craft:Chest",materials=new[]{new{item="wood",missing=48}}}}},operating_candidates=new[]{new{Args=new{entity="craft:Chest"},Requirements=new{materials=new[]{new{item="wood",missing=48}}}}},service_hours=new[]{new{location="SeedShop",opens=900},new{location="Farm",opens=600}},memory=new{daily_activity=new{today=new[]{new{Day=0,activity="购买",item="seed",count=6,cost=120,batches=2}},previous=new[]{new{count=9}}}}});
        var packed=ContextBudget.Pack(source,10000);var view=JsonSerializer.Deserialize<JsonElement>(packed.Json);
        check(view.GetProperty("prerequisites").GetProperty("facts").GetProperty("craft:Chest").GetProperty("materials")[0].GetProperty("missing").GetInt32()==48&&view.GetProperty("operating_candidates")[0].GetProperty("Requirements").GetProperty("fact_id").GetString()=="craft:Chest","candidate and selected goal share one material fact");
        check(view.GetProperty("service_hours").GetArrayLength()==1&&!view.GetProperty("memory").GetProperty("daily_activity").TryGetProperty("previous",out _)&&source.GetProperty("memory").GetProperty("daily_activity").GetProperty("previous").GetArrayLength()==1,"unrelated services and previous-day diary move to readback without modifying the frozen archive");
    }
}
