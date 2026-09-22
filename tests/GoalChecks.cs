using System.Text.Json;
using Together;

public static class GoalChecks {
    public static void Run(Action<bool,string> check) {
        var dedup=new SharedGoal{Entity="craft:Chest",Completion="placed",Count=1};
        check(dedup.SameRequest("craft:Chest",1,"placed",0,false)&&!dedup.SameRequest("craft:Chest",2,"placed",0,false),"same unfinished product resumes but changed quantity is not silently merged");
        dedup.Status="completed";check(!dedup.SameRequest("craft:Chest",1,"placed",0,false),"completed goal is not reused for a new product");
        check(OperationsPolicy.Outcome("work.run",JsonSerializer.SerializeToElement(new{status="running",completed=0})).Disposition=="in_progress","running work is not a failure");
        check(OperationsPolicy.Outcome("work.run",JsonSerializer.SerializeToElement(new{status="waiting"})).Disposition=="waiting_condition","waiting work has explicit non-failure disposition");

        check(!GoalPlanner.WorkSpeech("地下层，下午带5块回来","accept","Farm").Contains("5"),"material work cannot promise an invented delivery quantity or location");
        check(GoalPlanner.WorkSpeech("挖矿我喜欢，不过我先试一趟。","accept","Farm").Contains("喜欢"),"grounded personality survives work speech validation");
        var recipes=new Dictionary<string,GoalRecipe>{
            ["craft:device"]=new(){Id="craft:device",Item="device",Output=1,Known=false,Inputs=new(){new(){Item="bar",Count=2},new(){Item="wood",Count=10}}},
            ["process:bar"]=new(){Id="process:bar",Item="bar",Kind="process",Facility="furnace",Output=1,Known=true,Inputs=new(){new(){Item="ore",Count=5},new(){Item="coal",Count=1}}}
        };
        SharedGoal Goal()=>new(){Entity="craft:device",Item="device",Title="机器",Count=1};
        GoalLedger Ledger(params (string Item,int Count)[] items)=>new(items.Select(i=>new GoalStock{Item=i.Item,Count=i.Count}));
        var g=Goal();GoalPlanner.Rebuild(g,recipes,Ledger(("bar",1),("ore",3),("wood",5)),1,id=>id);
        check(g.Nodes[0].Status=="locked" && g.Nodes.Any(n=>n.Item=="ore" && n.Required==5 && n.Missing==2),"locked recipe still decomposes only missing intermediates into resources");
        check(g.Reserved.Single(n=>n.Item=="wood").Count==5 && g.Status=="active","available materials reserved, preparation is not completion");
        var same=JsonSerializer.Serialize(g.Nodes);GoalPlanner.Rebuild(g,recipes,Ledger(("bar",1),("ore",3),("wood",5)),1,id=>id);
        check(g.History.Count==1 && JsonSerializer.Serialize(g.Nodes)==same,"unchanged refresh is deterministic and does not spam history");
        recipes["craft:device"].Known=true;GoalPlanner.Rebuild(g,recipes,Ledger(("bar",2),("wood",10)),2,id=>id);
        check(g.Nodes[0].Status=="player_step" && g.Status=="active","materials ready requires real crafting, not a success flag");
        GoalPlanner.Rebuild(g,recipes,Ledger(("device",1)),2,id=>id);
        check(g.Status=="fulfilled" && g.Reserved.Count==0,"actual target stock fulfills and releases reservations");
        g=Goal();g.Completion="crafted";g.BaselineCrafts=5;GoalPlanner.Rebuild(g,recipes,Ledger(),2,id=>id,5);
        check(g.Status=="active","historical craft count does not fulfill a new goal");
        GoalPlanner.Rebuild(g,recipes,Ledger(),2,id=>id,6);check(g.Status=="fulfilled","new native craft count verifies a placed or moved result");
        var first=Goal();var second=Goal();var ledger=Ledger(("bar",2),("wood",10));
        GoalPlanner.Rebuild(first,recipes,ledger,3,id=>id);GoalPlanner.Rebuild(second,recipes,ledger,3,id=>id);
        check(first.Nodes[0].Status=="player_step" && second.Nodes.Any(n=>n.Item=="wood"&&n.Owned==0),"two goals cannot allocate the same stock twice");
        g=Goal();g.Status="paused";g.Reserved.Add(new(){Item="wood",Count=5});GoalPlanner.Rebuild(g,recipes,Ledger(),3,id=>id);
        check(g.Reserved.Count==0 && g.Status=="paused","paused wishes release stock and stay paused");
        g=Goal();g.Assignments["root/wood"]="player";GoalPlanner.Rebuild(g,recipes,Ledger(),3,id=>id);
        var restored=JsonSerializer.Deserialize<SharedGoal>(JsonSerializer.Serialize(g))!;GoalPlanner.Rebuild(restored,recipes,Ledger(("wood",4)),4,id=>id);
        check(restored.Nodes.Single(n=>n.Item=="wood").Owner=="player" && restored.Nodes.Single(n=>n.Item=="wood").Owned==4,"saved per-material assignments survive day changes and refresh");
        check(restored.History.Last().Day==4,"observed next-day progress recorded with day");
        g=Goal();GoalPlanner.Rebuild(g,recipes,Ledger(),3,id=>id,processing:Ledger(("bar",2)));
        check(g.Nodes.Any(n=>n.Item=="bar"&&n.InProgress==2&&n.Status=="processing") && !g.Nodes.Any(n=>n.Item=="ore"),"in-flight machine output prevents collecting inputs twice");
        check(g.Status=="active","processing output does not count as final ownership");
        g=Goal();g.DirectGather.Add("root/bar");GoalPlanner.Rebuild(g,recipes,Ledger(),3,id=>id);
        check(g.Nodes.Any(n=>n.Item=="bar" && n.Kind=="gather") && !g.Nodes.Any(n=>n.Item=="ore"),"player can choose direct acquisition instead of an unsuitable production chain");
        g=Goal();recipes["process:bar"].Known=false;GoalPlanner.Rebuild(g,recipes,Ledger(),3,id=>id);
        check(g.Nodes.Any(n=>n.Item=="bar" && n.Kind=="gather") && !g.Nodes.Any(n=>n.Item=="furnace"),"missing processing equipment does not silently add a whole construction project for a material");
        // Enabling expansion is a separate, persisted authority; existing callers keep direct acquisition.
        g=Goal();g.AllowNewFacilities=true;GoalPlanner.Rebuild(g,recipes,Ledger(),3,id=>id);
        check(g.Nodes.Any(n=>n.Item=="furnace")&&g.Nodes.Any(n=>n.Item=="ore"),"authorized enterprise can prepare missing processing equipment and raw inputs");
        var expanded=JsonSerializer.Deserialize<SharedGoal>(JsonSerializer.Serialize(g))!;
        check(expanded.AllowNewFacilities,"facility expansion policy survives save/load");

        recipes["process:bar"].Known=true;
        var circular=new Dictionary<string,GoalRecipe>{["craft:a"]=new(){Id="craft:a",Item="a",Known=true,Inputs=new(){new(){Item="b",Count=1}}},["craft:b"]=new(){Id="craft:b",Item="b",Known=true,Inputs=new(){new(){Item="a",Count=1}}}};
        g=new(){Entity="craft:a",Item="a"};GoalPlanner.Rebuild(g,circular,Ledger(),1,id=>id);
        check(g.Nodes.Count==3 && g.Nodes.Any(n=>n.Status=="blocked"),"cyclic mod recipes stop instead of infinite expansion");
        var wide=new Dictionary<string,GoalRecipe>{["craft:a"]=new(){Id="craft:a",Item="a",Known=true,Inputs=Enumerable.Range(0,90).Select(i=>new Requirement{Item="part"+i,Count=1}).ToList()}};
        foreach(int i in Enumerable.Range(0,90))wide["craft:part"+i]=new(){Id="craft:part"+i,Item="part"+i,Known=true,Inputs=Enumerable.Range(0,90).Select(j=>new Requirement{Item="leaf"+j,Count=1}).ToList()};
        g=new(){Entity="craft:a",Item="a"};GoalPlanner.Rebuild(g,wide,Ledger(),1,id=>id);
        check(g.Nodes.Count<=96 && g.Nodes.Any(n=>n.Status=="blocked"),"wide nested mod recipes obey the global node budget");
        wide["craft:a"].Inputs=new(){new(){Item="ore",Count=100001}};g=new(){Entity="craft:a",Item="a"};GoalPlanner.Rebuild(g,wide,Ledger(),1,id=>id);
        check(g.Nodes[0].Status=="blocked" && !g.Nodes.Any(n=>n.Required==100000),"oversized material requirements are blocked, never silently understated");
        ledger=new(new[]{new GoalStock{Item="fish",Category=-4,Count=3,Quality=2}});
        check(ledger.Take("(O)-4",2,2)==2 && ledger.Take("fish",2)==1,"category and exact requirements share the same stock ledger");
        g=Goal();g.Count=3;recipes["craft:device"].Output=2;GoalPlanner.Rebuild(g,recipes,Ledger(),1,id=>id);
        check(g.Nodes.Single(n=>n.Item=="wood").Required==20,"batch output rounds up ingredient requirements");
        var save=JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(new SaveData{SharedGoals=new(){g}}))!;
        check(save.SchemaVersion==6 && save.SharedGoals[0].Count==3,"new goal state persists under downgrade-protected save version");

        var alternatives=new Dictionary<string,GoalRecipe>{
            ["costly"]=new(){Id="costly",Item="device",Known=true,Inputs=new(){new(){Item="unavailable",Count=80}}},
            ["available"]=new(){Id="available",Item="device",Known=true,Inputs=new(){new(){Item="material",Count=3}}}
        };
        g=new(){Entity="device",Item="device"};GoalPlanner.Rebuild(g,alternatives,Ledger(("material",3)),1,x=>x);
        check(g.Nodes[0].Recipe=="available"&&g.Nodes[0].Status=="player_step","route choice uses available resources instead of alphabetical recipe order");
        var renamed=alternatives.ToDictionary(p=>"renamed:"+p.Key,p=>new GoalRecipe{Id="renamed:"+p.Key,Item="new-device",Known=true,Inputs=p.Value.Inputs.Select(i=>new Requirement{Item="new-"+i.Item,Count=i.Count}).ToList()});
        var transformed=new SharedGoal{Entity="new-device",Item="new-device"};GoalPlanner.Rebuild(transformed,renamed,Ledger(("new-material",3)),1,x=>x);
        check(transformed.Nodes[0].Recipe=="renamed:"+g.Nodes[0].Recipe&&transformed.Nodes.Select(n=>n.Required).SequenceEqual(g.Nodes.Select(n=>n.Required)),"renaming all recipe/material identities preserves planning decisions");
        g=new(){Entity="available",Item="device",Completion="placed"};GoalPlanner.Rebuild(g,alternatives,Ledger(("device",1)),1,x=>x);
        check(g.Status=="active"&&g.Nodes[0].Kind=="place"&&g.Nodes[0].Status=="player_step","carrying a facility cannot satisfy a placed goal");
        GoalPlanner.Rebuild(g,alternatives,Ledger(),1,x=>x,placed:1);
        check(g.Status=="fulfilled","observed placement satisfies its own predicate without fake inventory");
        var shared=new Dictionary<string,GoalRecipe>{
            ["final"]=new(){Id="final",Item="final",Known=true,Inputs=new(){new(){Item="left",Count=1},new(){Item="right",Count=1}}},
            ["left"]=new(){Id="left",Item="left",Known=true,Inputs=new(){new(){Item="intermediate",Count=1}}},
            ["right"]=new(){Id="right",Item="right",Known=true,Inputs=new(){new(){Item="intermediate",Count=1}}},
            ["batch"]=new(){Id="batch",Item="intermediate",Output=2,Known=true,Inputs=new(){new(){Item="raw",Count=3}}}
        };
        g=new(){Entity="final",Item="final"};GoalPlanner.Rebuild(g,shared,Ledger(("raw",3)),1,x=>x);
        check(g.Nodes.Count(n=>n.Item=="raw")==1&&g.Nodes.Any(n=>n.Planned==1&&n.Status=="planned")&&g.Status=="active","shared intermediate surplus is a dependency, not duplicate gathering or fictional ownership");
        var copy=Ledger(("material",3));GoalRoutes.Select(alternatives.Values,alternatives,copy,1,false);
        check(copy.Take("material",3)==3,"alternative evaluation cannot spend the authoritative planning ledger");
    }
}
