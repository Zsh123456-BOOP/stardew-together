using System.Text.Json;
using Together;
public static class DecisionReviewChecks {
    private static JsonElement J(object value)=>JsonSerializer.SerializeToElement(value,AgentJson.Options);
    public static void Run(Action<bool,string> check) {
        var seed=new ReviewOption("inspect:seed-offers",0,30,"visit_quote_only");
        JsonElement Sleep(string because,string id="inspect:seed-offers")=>J(new{reason="作物照料完毕后决定收工",alternatives=new[]{new{id,because,detail="比较本次到店的成本与未来照料后决定暂缓",value=new{today="今天询价保留扩种机会",defer="推迟一天了解报价，优先当前照料"}}}});
        check(DecisionReviewPolicy.Sleep(Sleep("energy"),12,875,new[]{seed})=="decision_review:energy_contradiction:inspect:seed-offers","09:40 regression: stamina 12 cannot exclude zero-energy seed visit");
        check(DecisionReviewPolicy.Sleep(Sleep("time"),12,875,new[]{seed})=="decision_review:time_contradiction:inspect:seed-offers","09:40 regression: 875 remaining minutes cannot imply urgent return for a 30-minute visit");
        check(DecisionReviewPolicy.Sleep(Sleep("low_value"),12,875,new[]{seed})==null,"feasible shopping does not force buying or waiting until evening");
        check(DecisionReviewPolicy.Sleep(Sleep("defer"),12,875,new[]{seed})==null,"explicitly deferred opportunity permits normal early sleep");
        check(DecisionReviewPolicy.Sleep(Sleep("time"),12,20,new[]{seed})==null,"real insufficient time remains a valid reason");
        check(DecisionReviewPolicy.Sleep(Sleep("energy","water"),12,875,new[]{new ReviewOption("water",30,30,"care")})==null,"actual costly maintenance can be deferred for insufficient stamina");
        check(DecisionReviewPolicy.Sleep(Sleep("energy"),12,875,new[]{seed with{energy=null}})!.Contains("energy_unknown"),"unknown cost cannot be silently treated as infeasible");
        check(DecisionReviewPolicy.Sleep(J(new{reason="体力不足，回家睡觉"}),12,875,new[]{seed})=="decision_review:alternatives_required","unstructured reason cannot bypass review when alternatives exist");
        check(DecisionReviewPolicy.Sleep(J(new{reason="可用时间不足，正常收工"}),12,0,Array.Empty<ReviewOption>())==null,"no listed alternative requires no fabricated comparison");
        check(DecisionReviewPolicy.Sleep(Sleep("energy","closed-shop"),12,875,new[]{seed})!.Contains("changed_or_duplicate"),"stale or invented candidate IDs require fresh comparison");
        check(DecisionReviewPolicy.Sleep(J(new{reason="决定明日继续",alternatives=new[]{new{id=seed.id,because="defer",detail=""}}}),12,875,new[]{seed})!.Contains("detail_required"),"bare labels do not count as an explanation");
        check(DecisionReviewPolicy.Sleep(Sleep("unavailable"),12,875,new[]{seed})!.Contains("invalid_comparison"),"invented condition codes are rejected");
        var rows=new[]{new{id=seed.id,because="defer",detail="保留现金等待后续投资",value=new{today="今天了解可买种类与成本",defer="明日询价可能推迟成熟"}},new{id=seed.id,because="defer",detail="保留现金等待后续投资",value=new{today="今天了解可买种类与成本",defer="明日询价可能推迟成熟"}}};
        check(DecisionReviewPolicy.Sleep(J(new{reason="决定明日继续",alternatives=rows}),12,875,new[]{seed})!.Contains("duplicate"),"duplicate assessment cannot replace another candidate");
        check(DecisionReviewPolicy.Sleep(Sleep("defer"),12,875,new[]{seed,new ReviewOption("forage:Farm",0,10,"forage")})=="decision_review:compare_current_alternatives","selected alternatives cannot be silently skipped");
        check(DecisionReviewPolicy.Sleep(J(new{reason="决定明日继续",alternatives=rows.Concat(rows)}),12,875,new[]{seed})!.Contains("max_3"),"review size is bounded instead of expanding world catalogue");
        JsonElement Resource(int? reserve,string care="preserve",string tradeoff="")=>J(new{reserve_stamina=reserve,labor_review=new{purpose="补齐箱子材料缺口，达到目标后停止",followup="为已承诺作物照料留出体力",care,tradeoff}});
        check(DecisionReviewPolicy.Resource(J(new{}),50,0,30)!.Contains("purpose_and_reserve"),"autonomous collection requires declared purpose and labor allocation");
        check(DecisionReviewPolicy.Resource(Resource(null),50,0,30)!.Contains("explicit_stamina"),"omitted stamina reserve is not an implicit zero-energy choice");
        check(DecisionReviewPolicy.Resource(Resource(20),50,20,30)=="decision_review:reserve_below_pending_care","resource collection cannot claim preserved care with insufficient reserve");
        check(DecisionReviewPolicy.Resource(Resource(30),50,30,30)==null,"sufficient explicit reserve admits useful collection");
        check(DecisionReviewPolicy.Resource(Resource(0),50,0,0)==null,"explicit zero reserve remains possible when no care is pending");
        check(DecisionReviewPolicy.Resource(Resource(0,"defer"),50,0,30)!.Contains("tradeoff"),"deferred care requires an explicit tradeoff");
        check(DecisionReviewPolicy.Resource(Resource(0,"defer","推迟浇水会延迟成熟，优先紧急委托"),50,0,30)==null,"explicit care tradeoff is not overridden by a hidden fixed reserve");
        check(DecisionReviewPolicy.Resource(J(new{}),0,0,30)==null,"already met material target does not demand further work or review");
        var attempts=new DecisionReviewAttempts();
        check(!attempts.Reject("day0:progress1")&&!attempts.Reject("day0:progress1")&&attempts.Reject("day0:progress1"),"third same-key rejection reaches local recovery threshold");
        check(!attempts.Reject("day0:progress2")&&attempts.Count==1,"native progress resets the rejection budget");
        check(!attempts.Reject("day1:progress2")&&attempts.Count==1,"new day resets the rejection budget");
        attempts.Clear();check(!attempts.Reject("day1:progress2"),"corrected accepted decision or explicit restart clears rejection budget");
        var specs=ToolSpecs.Select(new Dictionary<string,string>{{"player.sleep","legacy"},{"agent.pause","legacy"},{"work.run","legacy"}},J(new{active_actors=new[]{"player"}}));
        var sleep=specs[0].Parameters.GetProperty("properties");
        check(sleep.GetProperty("defer").GetProperty("items").GetProperty("type").GetString()=="string"&&!sleep.TryGetProperty("alternatives",out _),"API exposes short acknowledgment instead of a prose cost examination");
        check(specs[0].Parameters.GetRawText()==specs[1].Parameters.GetRawText(),"agent.pause exposes the same close-day review contract");
        check(specs[2].Parameters.GetProperty("properties").GetProperty("labor_review").GetProperty("type").GetString()=="object"&&ToolSpecs.Contract().Contains("labor_review?:{purpose:string"),"work schema and discovery expose the structured resource review");
        var context=new{decision_review=new{sleep_alternatives=new[]{seed}},memory=new{verbose=new string('x',16000)},now=new{stamina=12,time=940}};
        var packed=JsonSerializer.Deserialize<JsonElement>(ContextBudget.Pack(context,6000,4000).Json);
        check(packed.GetProperty("decision_review").GetProperty("sleep_alternatives")[0].GetProperty("id").GetString()==seed.id,"context compression preserves required review facts");
    }
}
