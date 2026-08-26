using System.Text.Json;
using Together;
using StardewCropCalculatorLibrary;

public static class AutonomyDevelopmentChecks {
    public static void Run(Action<bool,string> check) {
        check(ResourceRules.Clump(148)?.Output=="(O)390"&&ResourceRules.Clump(622)?.Output=="(O)386","quarry boulder is stone and meteorite is iridium");
        check(ResourceRules.Nodes["751"]=="(O)378"&&ResourceRules.Clump(600)?.Output=="(O)709","resource selection resolves real node outputs");
        var qualityOwnedGoal=new SharedGoal{Item="crop",Entity="crop",Count=2,MinimumQuality=2};
        GoalPlanner.Rebuild(qualityOwnedGoal,new Dictionary<string,GoalRecipe>(),new(new[]{new GoalStock{Item="crop",Count=20,Quality=0},new GoalStock{Item="crop",Count=1,Quality=2}}),1,id=>id);
        check(qualityOwnedGoal.Status=="active"&&qualityOwnedGoal.Nodes[0].Quality==2&&qualityOwnedGoal.Nodes[0].Owned==1,"high-quality owned goal cannot be satisfied by ordinary crop stock");
        check(ToolLocationContract.Bind("player.buy","Farm","player.service","","SeedShop")=="","native shop purchase does not retain the departure farm as a map precondition");
        check(ToolLocationContract.Bind("player.move","Farm","player.travel","","Town")=="Town","local tile operation following known travel binds the destination");
        check(ToolLocationContract.Bind("player.beach","Farm",null,"","")=="","semantic beach interaction owns its travel and native preconditions");
        bool rejectedUnknown=false;try{ToolLocationContract.Bind("player.move","Farm","player.social","","");}catch(InvalidOperationException){rejectedUnknown=true;}
        check(rejectedUnknown,"coordinates following dynamically moving NPC require an explicit map");
        var failures=new FailureKnowledge();
        failures.Record("k","player","work.run","empty","state1","task1",1,600);
        check(failures.Block("k","state1",1,610)!=null,"repeat failure blocked only under unchanged conditions");
        check(failures.Block("k","state2",1,610)==null&&failures.Block("k","state1",2,610)==null&&failures.Block("k","state1",1,621)==null,"failure evidence expires on state change, next day, or retry window");
        failures.Success("k");check(failures.Entries.Count==0,"successful execution retires obsolete failure advice");
        var recipes=new Dictionary<string,GoalRecipe>{["craft:batch"]=new(){Id="craft:batch",Item="output",Known=true,Output=5,Inputs=new(){new(){Item="wood",Count=1}}}};
        var goal=new SharedGoal{Entity="craft:batch",Item="output",Count=5,BaselineCrafts=10,Completion="crafted"};
        GoalPlanner.Rebuild(goal,recipes,new(new[]{new GoalStock{Item="output",Count=99}}),1,id=>id,10);
        check(goal.Status=="active","bought output does not satisfy a native crafting goal");
        GoalPlanner.Rebuild(goal,recipes,new(Array.Empty<GoalStock>()),1,id=>id,11);
        check(goal.Status=="active","native output count must not be multiplied by recipe batch size twice");
        GoalPlanner.Rebuild(goal,recipes,new(Array.Empty<GoalStock>()),1,id=>id,15);
        check(goal.Status=="fulfilled","native production delta satisfies explicit crafted goal");
        check(RecoveryPolicy.CanWait("energy_reserve_reached")&&RecoveryPolicy.CanWait("mine_time_reserve_return"),"expected supply/time limits preserve the plan for changed conditions");
        check(!RecoveryPolicy.CanWait("native_shipment_conservation_failed")&&!RecoveryPolicy.CanWait("cancelled")&&!RecoveryPolicy.CanWait("NullReferenceException"),"unknown errors, broken conservation and cancellation cannot silently retry");
        check(AgentSchedule.Queueable("player.find_lost_item"),"native lost-item skill is reachable from persistent plans");
        var ownedGoal=new SharedGoal{Entity="craft:batch",Item="output",Count=5,Completion="owned"};
        GoalPlanner.Rebuild(ownedGoal,recipes,new(Array.Empty<GoalStock>()),1,id=>id,99);
        check(ownedGoal.Status=="active","historic craft counter cannot satisfy an owned inventory goal");
        check(new Crop("seed",4,-1,20,35).HarvestDays(1,28).SequenceEqual(new[]{5}),"single-harvest schedule terminates without backward regrow loop");
        var batch=new PlantBatch(new Crop("bean",10,3,60,40),1,1,28);
        check(new PlantBatch(batch).NumDays==28,"calendar clone preserves harvest horizon");
        check(CropGrowth.Stages(new[]{1,2,2,3},.25f,false,false).Sum()==6,"growth reduction preserves initial phase and native phase rounding");
        check(CropGrowth.Stages(new[]{10,10,10},.9f,false,false).Sum()==21,"native growth speed reduction is capped at three phase passes");
        check(CropGrowth.SeasonEnd(0,27,new HashSet<int>{0,1},false)==56,"cross-season crop keeps legal continuous growth window");
        check(CropGrowth.SeasonEnd(0,27,new HashSet<int>{0,2},false)==28,"non-contiguous seasons cannot bridge a forbidden season");
        var cells=Enumerable.Range(0,5).Select(x=>new LayoutCell(new(x,0),true,true,false,false,true,false,0)).ToArray();
        var layout=FarmLayout.Choose(cells,new(0,0),new[]{new FarmCell(4,0)},4,true,4);
        check(layout.Tiles.Count==0,"trellis cannot block the only path to an exit");
        check(!FarmLayout.KeepsAccess(cells,new(0,0),new[]{new FarmCell(4,0)},new[]{new FarmCell(2,0)},Array.Empty<FarmCell>()),"building footprint cannot sever the only exit corridor");
        check(FarmLayout.KeepsAccess(cells,new(0,0),new[]{new FarmCell(3,0)},new[]{new FarmCell(4,0)},new[]{new FarmCell(3,0)}),"building at dead end keeps its entrance and existing access reachable");
        var seedQuote=new SeedQuote("seed","shop","shop-map",1,10,99,1);
        var economySeed=new EconomySeed("seed",0,seedQuote,30,-1,28,false,1,1,cells.ToDictionary(c=>c.Tile,c=>4),new());
        var economy=new EconomySnapshot(1,1,100,30,80,5,2,"income",new(0,0),cells.ToList(),new(){new(4,0)},new(){economySeed});
        var portfolio=CropPortfolio.Plan(economy);
        check(portfolio.Spent<=20&&portfolio.Manual<=2&&portfolio.Plants.Count<=2,"economic planting respects wallet reserve, purchase budget and daily care capacity");
        var projection=portfolio.Reinvestment!;
        check(projection.Days.All(d=>d.Gold>=80&&d.ManualWater<=2&&d.PurchaseCost<=30),"multi-cycle cash forecast respects every day's gold reserve, care limit and purchase allowance");
        check(projection.Replantings.All(o=>o.Day%7!=3&&o.Day<=28),"future SeedShop purchases avoid Wednesdays and unobserved next-season offers");
        var blockedCash=new EconomySnapshot(1,1,10,10,0,1,1,"income",new(0,0),cells.ToList(),new(){new(4,0)},new(){economySeed with{ReserveYield=0}});
        var reinvest=SeasonCashForecast.Plan(blockedCash,new(){new("seed",new(0,0),4,true,10)},10);
        check(reinvest.Replantings.All(o=>o.Day>=6),"cannot spend a first harvest's shipping proceeds on harvest day");
        var calendar=new GameStateCalendar(28,5,100);CalendarCashFlow.Apply(calendar,1,new Crop("seed",4,-1,10,30),1,28);
        check(calendar.GameStates[5].Wallet==90&&calendar.GameStates[6].Wallet==120,"crop proceeds become spendable only the day after harvest");
        var water=FarmLayout.WaterDistances(cells,new[]{new FarmCell(5,0)});
        check(water[new(0,0)]==4,"water access is measured over walkable route");
        string root=Path.Combine(Path.GetTempPath(),"together-memory-check-"+Guid.NewGuid().ToString("N"));
        try {
            var checkpoint=new MemoryCheckpoint();var archive=new MemoryArchive(root,"epoch1",checkpoint);
            archive.Append(1,"Abigail","experience","今天钓鱼");
            var saved=JsonSerializer.Deserialize<MemoryCheckpoint>(JsonSerializer.Serialize(checkpoint))!;
            archive.Append(1,"Abigail","experience","未来才得到的物品");
            var past=new MemoryArchive(root,"epoch2",saved);
            using(var entries=JsonDocument.Parse(AgentJson.Encode(past.Read("","Abigail",20))))check(entries.RootElement.GetProperty("entries").GetArrayLength()==1,"reload checkpoint cannot recall future appended events");
            archive.ForgetActor("Abigail");
            using(var entries=JsonDocument.Parse(AgentJson.Encode(archive.Read("","Abigail",20))))check(entries.RootElement.GetProperty("entries").GetArrayLength()==0,"forgotten companion archive stays out of retrieval");
            string blocked=root+"-file";File.WriteAllText(blocked,"occupied");
            try {
                var pending=new MemoryCheckpoint();new MemoryArchive(blocked,"epoch3",pending).Append(1,"autoplay","event",new string('甲',9000));
                check(pending.Pending.Count==1,"archive I/O failure preserves full pending event in checkpoint");
                var recovered=new MemoryArchive(root,"epoch4",pending);recovered.Flush();
                using var evidence=JsonDocument.Parse(AgentJson.Encode(recovered.Evidence("epoch3-1:1",8000)));
                check(pending.Pending.Count==0&&evidence.RootElement.GetProperty("text").GetString()!.Length==1000,"retry archive and page complete long evidence");
            }finally{File.Delete(blocked);}
        }finally{if(Directory.Exists(root))Directory.Delete(root,true);}
        var qualityRecipe=new GoalRecipe{Id="craft:quality",Item="(O)900",Known=true,Inputs=new(){new(){Item="(O)24",Count=1,Quality=2}}};
        var qualityGoal=new SharedGoal{Entity="craft:quality",Item="(O)900"};
        GoalPlanner.Rebuild(qualityGoal,new Dictionary<string,GoalRecipe>{{qualityRecipe.Id,qualityRecipe}},new GoalLedger(new[]{new GoalStock{Item="(O)24",Count=10,Quality=0}}),1,id=>id);
        check(qualityGoal.Nodes[0].Status!="player_step"&&qualityGoal.Nodes.Any(n=>n.Item=="(O)24"&&n.Quality==2&&n.Owned==0),"recipe ingredient quality survives dependency expansion and low-quality stock cannot satisfy it");
        var protectedStock=new[]{new GoalStock{Item="(O)24",Quality=2,Category=-75,Count=10}};
        var overlapping=new[]{new Requirement{Item="(O)24",Count=5},new Requirement{Item="(O)24",Quality=2,Count=5}};
        check(!ReservationAllocation.Preserves(protectedStock,overlapping,new[]{new GoalStock{Item="(O)24",Quality=2,Category=-75,Count=1}}),"normal and gold reservations cannot both spend the same physical unit");
        var substitutes=new[]{new GoalStock{Item="(O)24",Category=-75,Count=1},new GoalStock{Item="(O)188",Category=-75,Count=1}};
        var demands=new[]{new Requirement{Item="-75",Count=1},new Requirement{Item="(O)24",Count=1}};
        check(ReservationAllocation.Allocate(substitutes,demands).SequenceEqual(new[]{1,1}),"category reservation reroutes to preserve the specifically requested crop");
        check(ReservationAllocation.Preserves(substitutes,new[]{new Requirement{Item="(O)24",Count=10}},new[]{new GoalStock{Item="(O)188",Category=-75,Count=1}}),"already missing materials do not block spending unrelated surplus");
        var flow=Together.Shared.ResourceFlow.Allocate(new[]{new Together.Shared.ResourceFlow.Stock("(O)24",-75,2,2),new Together.Shared.ResourceFlow.Stock("(O)188",-75,0,1)},
            new[]{new Together.Shared.ResourceFlow.Demand("-75",0,1),new Together.Shared.ResourceFlow.Demand("(O)24",2,2)});
        check(flow.StockUsed.SequenceEqual(new[]{2,1})&&flow.DemandFilled.SequenceEqual(new[]{1,2}),"shared player and companion allocator reserves each physical quality unit once");
        var context=ContextCompression.Pack(new{recent=new object[]{new{error="unresolved",detail=new string('x',1000)},new{okay="old",detail=new string('y',1000)},new{okay="new"},new{okay="latest"}},schedule=new{active="must_survive"}},500);
        check(context.Contains("unresolved")&&context.Contains("must_survive")&&!context.Contains(new string('y',1000)),"context pressure drops old successes while retaining unresolved errors and active plan");
    }
}
