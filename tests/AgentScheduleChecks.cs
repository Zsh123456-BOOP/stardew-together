using System.Text.Json;
using Together;

public static class AgentScheduleChecks {
    private static AgentTaskSpec Player(string id,params string[] deps)=>new(){id=id,actor="player",tool="player.move",args=JsonSerializer.SerializeToElement(new{x=1,y=2}),after=deps.ToList()};
    private static AgentTaskSpec Npc(string id)=>new(){id=id,actor="npc:Abigail",tool="companion.assign",args=JsonSerializer.SerializeToElement(new{actor_id="npc:Abigail",skill="follow"})};
    public static void Run(Action<bool,string> check) {
        check(AgentPollingPolicy.Defer(true,false,new[]{"plan.submit","world.read","action.status"}),"busy actors do not trigger paid polling loops for redundant state reads");
        check(!AgentPollingPolicy.Defer(false,false,new[]{"world.read"})&&!AgentPollingPolicy.Defer(true,true,new[]{"action.status"})&&!AgentPollingPolicy.Defer(true,false,new[]{"knowledge.get"}),"idle actors, tool errors and new knowledge still wake model planning");
        var guarded=new AgentSchedule{Prepare=t=>{if(t.id=="denied")throw new InvalidOperationException("known_failure_conditions_unchanged:door_closed");}};
        PlanStepRejected? rejection=null;try{guarded.Submit("blocked",0,new(){Player("independent"),Player("denied")},0);}catch(PlanStepRejected e){rejection=e;}
        check(rejection?.TaskId=="denied"&&rejection.Message.Contains("door_closed")&&guarded.Tasks.Count==0&&guarded.Revision==0,"rejected step preserves original cause and atomic submission, no unexecuted task claimed queued");
        check(guarded.Submit("independent",0,new(){Player("independent")},0)&&guarded.Ready(0,600).Count==1,"independent work can be resubmitted without deleting real prerequisite dependencies");
        var spatial=new AgentSchedule();spatial.Submit("spatial",0,new(){Player("far"),Player("near"),Player("dependent","far")},0);
        var selection=spatial.Ready(0,600,ready=>ready.OrderBy(t=>t.spec.id=="near"?0:1));
        check(selection.Count==1&&selection[0].spec.id=="near","spatial policy sees all dependency-ready candidates before one-per-actor selection");
        var semantic=new AgentSchedule();
        var work=new AgentTaskSpec{id="wood",actor="player",tool="work.run",args=JsonSerializer.SerializeToElement(new{goal="wood",count=12})};
        var npcWork=new AgentTaskSpec{id="stone",actor="npc:Abigail",tool="work.run",args=JsonSerializer.SerializeToElement(new{actor_id="npc:Abigail",goal="stone",count=3})};
        semantic.Submit("semantic",0,new(){work,npcWork},3);
        check(semantic.Ready(3,600).Count==2,"semantic jobs occupy independent actor lanes without coordinates");
        bool mismatch=false;try{semantic.Submit("mismatch",semantic.Revision,new(){new(){id="bad-work",actor="npc:Other",tool="work.run",args=JsonSerializer.SerializeToElement(new{actor_id="player",goal="water"})}},3);}catch(InvalidOperationException){mismatch=true;}
        check(mismatch,"semantic job cannot claim a different actor's scheduling lane");
        var inherit=new AgentSchedule();var inherited=new AgentTaskSpec{id="inherit",actor="npc:Abigail",tool="work.run",args=JsonSerializer.SerializeToElement(new{goal="water"})};
        inherit.Submit("inherit",0,new(){inherited},3);
        check(inherit.Tasks.Single().spec.args.GetProperty("actor_id").GetString()=="npc:Abigail"&&!inherited.args.TryGetProperty("actor_id",out _),"outer actor propagates to executor without mutating submitted fingerprint");
        check(!inherit.Submit("inherit",0,new(){inherited},3),"normalized actor remains idempotent on retransmission");
        var quoted=JsonSerializer.Deserialize<AgentTaskSpec>("{\"not_before\":\"900\",\"deadline\":\"2000\"}")!;
        check(quoted.not_before==900&&quoted.deadline==2000,"unambiguous quoted schedule times normalize to integers");
        check(WorkCapabilities.Validate("npc:Abigail","fish")?.Contains("supported=")==true&&WorkCapabilities.Validate("npc:Abigail","water")==null,"advertised companion semantic capabilities agree with rejected player-only actions");
        var args=JsonSerializer.SerializeToElement(new{expected_revision=4});
        check(AgentSchedule.RebaseOwnTurn(args,4,6).GetProperty("expected_revision").GetInt32()==6&&args.GetProperty("expected_revision").GetInt32()==4,"same-turn owned edits update later submission revision without mutating original request");
        check(AgentSchedule.RebaseOwnTurn(args,5,6).GetProperty("expected_revision").GetInt32()==4,"preexisting stale observation is not rebased across external changes");
        var q=new AgentSchedule();var input=new List<AgentTaskSpec>{Player("p1"),Player("p2","p1"),Npc("n1")};
        check(q.Submit("plan1",0,input,3)&&!q.Submit("plan1",0,input,3)&&q.Tasks.Count==3,"plan submission retries are idempotent even with an old revision");
        check(input.All(t=>t.day==-1),"submitting a plan does not mutate the model request fingerprint");
        var ready=q.Ready(3,600);check(ready.Select(t=>t.spec.id).SequenceEqual(new[]{"p1","n1"}),"one ready task per actor, two actors start independently");
        q.Started(ready[0],"player:one");q.Started(ready[1],"npc:one");q.Finish(ready[0],"succeeded",null,"{\"status\":\"succeeded\"}");
        check(q.Ready(3,620).Single().spec.id=="p2"&&q.Tasks.Last().state=="running","player successor starts while companion remains busy");
        var p2=q.Ready(3,620).Single();q.Started(p2,"player:two");q.Finish(p2,"failed","no_path","{}");
        q.Submit("dependents",1,new(){Player("p3","p2")},3);
        check(q.Ready(3,630).Count==0&&q.Tasks.Last().state=="blocked"&&q.Tasks[2].state=="running","failed prerequisite blocks only dependent work, not other actors");
        int count=q.Tasks.Count;
        bool rejected=false;try{q.Submit("bad",q.Revision,new(){Player("x","missing")},3);}catch(InvalidOperationException){rejected=true;}
        check(rejected&&q.Tasks.Count==count,"invalid dependency submission is atomic");
        var cycle=new AgentSchedule();rejected=false;try{cycle.Submit("cycle",0,new(){Player("a","b"),Player("b","a")},3);}catch(InvalidOperationException){rejected=true;}
        check(rejected&&cycle.Tasks.Count==0,"explicit business dependency cycles are rejected before dispatch");
        rejected=false;try{q.Submit("old_revision",0,new(){Player("new")},3);}catch(InvalidOperationException){rejected=true;}
        check(rejected,"stale structural plan revision cannot overwrite newer scheduling");
        var timed=new AgentSchedule();var future=Player("later");future.not_before=900;future.deadline=1000;timed.Submit("time",0,new(){future},3);
        check(timed.Ready(3,800).Count==0&&timed.Ready(3,900).Count==1,"planned time windows are respected at native time");
        check(timed.Ready(4,600).Count==0&&timed.Tasks[0].state=="blocked","yesterday's coordinates are not silently replayed after a day change");
        var bypass=new AgentSchedule();var shop=Player("shop");shop.not_before=900;
        bypass.Submit("purchase",0,new(){shop},3);bypass.Submit("chores",1,new(){Player("water")},3);
        check(bypass.Ready(3,600).Single().spec.id=="water","future purchase does not block independent morning care");
        bypass.Finish(bypass.Tasks[0],"failed","shop_closed","{}");
        check(bypass.Ready(3,700).Single().spec.id=="water","shop failure cannot cancel unrelated care on the same actor");
        var ordered=new AgentSchedule();ordered.Submit("transaction",0,new(){Player("open"),Player("buy")},3,ordered:true);
        check(ordered.Tasks[1].spec.after.SequenceEqual(new[]{"open"}),"native transaction batches explicitly retain business order");
        var longReceipt=JsonSerializer.Serialize(new{status="succeeded",before=new{money=500},detail=new string('x',12000),after=new{money=400}});
        ordered.Finish(ordered.Tasks[0],"succeeded",null,longReceipt);
        check(JsonDocument.Parse(ordered.Tasks[0].receipt!).RootElement.GetProperty("after").GetProperty("money").GetInt32()==400,"long native receipt retains structured settlement evidence");
        var partial=OperationsPolicy.Outcome("work.run",JsonSerializer.SerializeToElement(new{status="failed",stop_reason="fishing_trip_time_or_attempt_budget",requested=5,gained=3}));
        check(partial.Disposition=="partial"&&partial.Remaining==2&&partial.BusinessProgress,"deadline retains partial catches and remaining goal without blacklisting location");
        check(!OperationsPolicy.Outcome("player.travel",JsonSerializer.SerializeToElement(new{status="succeeded",completed=1})).BusinessProgress,"travel alone cannot clear a stalled business goal");
        check(OperationsPolicy.CapacityReady(1,OperationsPolicy.RequiredFreeSlots("fish"))&&OperationsPolicy.RequiredFreeSlots("fish")==0,"trip planning does not reserve two unused slots; actual catches validate native capacity");
        var constraints=new OperationsState();constraints.Observe(new(){Subject="SeedShop",Condition="door-closed",Day=3,RetryTime=900,Reason="before_open",Evidence="visit1"});
        check(constraints.Blocking("SeedShop","door-closed",3,800)!=null&&constraints.Blocking("SeedShop","door-closed",3,900)==null,"service constraint waits for its opening window rather than arbitrary retry intervals");
        check(constraints.Blocking("SeedShop","owner-now-ready",3,800)==null,"relevant native condition changes release the constraint");
        q.Suspend();var restored=JsonSerializer.Deserialize<AgentSchedule>(JsonSerializer.Serialize(q))!;
        check(restored.Tasks[2].state=="needs_review"&&restored.Ready(3,900).Count==0,"interrupted running work survives serialization without automatic replay");
        var nativeRestored=Newtonsoft.Json.JsonConvert.DeserializeObject<AgentSchedule>(Newtonsoft.Json.JsonConvert.SerializeObject(q))!;
        check(nativeRestored.Tasks[0].spec.args.GetProperty("x").GetInt32()==1 && nativeRestored.Tasks[2].spec.args.GetProperty("actor_id").GetString()=="npc:Abigail","SMAPI Newtonsoft save round trip preserves structured action arguments");
        var legacy=Newtonsoft.Json.JsonConvert.DeserializeObject<AgentTaskSpec>("{\"args\":{\"ValueKind\":1}}")!;
        check(legacy.args.ValueKind==JsonValueKind.Object && !legacy.args.EnumerateObject().Any(),"legacy malformed argument metadata is readable without invented actions");
        restored.CancelPending(new[]{"n1"});check(restored.Tasks[2].state=="cancelled","model can discard interrupted work after reading actual state");
        var archived=new AgentSchedule();archived.Submit("archive",0,new(){Player("a"),Player("b","a")},3);archived.Finish(archived.Tasks[0],"succeeded",null,"{}");
        check(archived.Archive()==0,"completed prerequisite evidence is retained while downstream work needs it");
        archived.Finish(archived.Tasks[1],"succeeded",null,"{}");check(archived.Archive()==2,"terminal plans can be archived without unbounded save growth");
    }
}
