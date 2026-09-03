using System.Text.Json;
using Together;
static class OperatingRevisionChecks {
    public static void Run(Action<bool,string> check) {
        check(FishingContinuity.Deadline(3,3,1200,1200)=="time_reserve_reached","a running child cannot bypass its trip deadline");
        check(FishingContinuity.Deadline(3,4,1200,600)=="day_changed_replan","day change expires child ownership before polling");
        check(FishingContinuity.Deadline(3,3,1200,1150)==null,"deadline does not interrupt valid earlier work");
        check(FishingContinuity.PhaseLimit("fishing_reel_without_menu")<FishingContinuity.PhaseLimit("fishing_wait_bite"),"orphan reel fails promptly while ordinary bite wait has room");
        check(BusinessRetention.Sellable(12,12,40,0)==0&&BusinessRetention.Sellable(60,60,40,0)==20,"sale floors retain sap and allow only actual excess");
        check(BusinessRetention.Sellable(60,5,40,0)==5&&BusinessRetention.Sellable(60,60,40,55)==5,"sales honor both project reservations and current machine inputs");
        var allocation=ProductionAllocation.Split(60,20,15,30);
        check(allocation.Free==25&&allocation.Committed==20&&allocation.Operations==15,"independent project commitments and machine operations consume separate quantities");
        check(ProductionAllocation.Split(60,20,15,50).Free==10,"stock target is a total, not duplicated on top of commitments");
        check(ProductionAllocation.Split(10,20,15,0).Missing==25&&ProductionAllocation.Split(10,20,15,0).Free==0,"shortage never creates fictional sale stock");
        var saved=JsonSerializer.Deserialize<ProductionPolicy>(JsonSerializer.Serialize(new ProductionPolicy{Selected="machine:x",Reason="缓解原料积压",SaleItems=new(){"(O)92"},SurplusDay=2}))!;
        check(saved.Selected=="machine:x"&&saved.SaleItems.Contains("(O)92"),"investment intent and scoped surplus policy survive saving without storing stale inventory");
        var schedule=new AgentSchedule();
        schedule.Submit("day",0,new(){new(){id="sleep",tool="player.sleep",day=0},new(){id="partner",actor="npc",tool="work.run",args=JsonSerializer.SerializeToElement(new{actor_id="npc",goal="water"}),day=0}},0);
        var sleep=schedule.Tasks[0];schedule.InsertBefore(sleep,new(){new(){id="withdraw",tool="work.run",day=0},new(){id="ship",tool="player.ship_items",day=0}});
        check(schedule.Ready(0,1800).Select(t=>t.spec.id).SequenceEqual(new[]{"withdraw","partner"}),"closing shipment precedes sleep without blocking the other actor");
        schedule.Finish(schedule.Tasks[0],"succeeded",null,"{}");check(schedule.Ready(0,1800).Any(t=>t.spec.id=="ship")&&!schedule.Ready(0,1800).Contains(sleep),"shipping waits for withdrawal and sleep waits for shipping");
        schedule.Finish(schedule.Tasks[1],"failed","missing_item","{}");schedule.Ready(0,1800);check(sleep.state=="blocked","failed closing shipment blocks false successful sleep completion");
        var district=new FarmDistrict();district.Commit(new[]{new FarmCell(10,10),new FarmCell(11,10)});district.Warehouse=new(5,5);
        var restored=JsonSerializer.Deserialize<FarmDistrict>(JsonSerializer.Serialize(district))!;
        var cells=new[]{new LayoutCell(new(12,10),true,true,false,false,false,false,0),new LayoutCell(new(25,25),true,true,false,false,false,false,0),new LayoutCell(new(5,5),true,true,false,false,false,false,0)};
        var layout=restored.Constrain(cells,new(0,0));
        check(layout[0].Plantable&&!layout[1].Plantable&&!layout[2].Plantable,"saved field expands locally and excludes a remote tempting plot and its warehouse");
        check(restored.Field.Count==2&&restored.Revision==1,"planning does not mutate the persisted approved field");
        check(FarmDistrict.LargestCluster(new[]{new FarmCell(1,1),new(2,1),new(30,30)},new(0,0)).Count==2,"old scattered save adopts its largest existing field without deleting other crops");
        check(Together.Shared.CompanionLabor.OptionalAllowance(40,10,5,1100)==0&&Together.Shared.CompanionLabor.OptionalAllowance(40,10,5,1600)==20,"partner reserves actual chores and afternoon work before optional gathering");
        check(Together.Shared.CompanionLabor.Cost("deposit")==0&&Together.Shared.CompanionLabor.Cost("clear")==4,"transport is separate from charged physical labor");
        check(FailureKnowledge.Key("player","player.sleep","{\"reason\":\"one\"}")==FailureKnowledge.Key("player","player.sleep","{\"reason\":\"two\"}"),"bedtime rephrasing cannot bypass known failure");
        check(FailureKnowledge.Key("player","work.run","{\"goal\":\"wood\",\"count\":40}")==FailureKnowledge.Key("player","work.run","{\"count\":40,\"goal\":\"wood\"}"),"argument ordering cannot bypass known failure");
        check(FailureKnowledge.SelectionKey("player","work.run","{\"goal\":\"forage\",\"location\":\"Town\",\"count\":8}")==FailureKnowledge.SelectionKey("player","work.run","{\"goal\":\"forage\",\"location\":\"Town\",\"count\":3}"),"changing quantity cannot retry an unchanged empty collection area");
        check(FailureKnowledge.SelectionKey("player","work.run","{\"goal\":\"forage\",\"location\":\"Town\"}")!=FailureKnowledge.SelectionKey("player","work.run","{\"goal\":\"forage\",\"location\":\"Forest\"}"),"another collection area remains a valid alternative");
        var grid=(from x in Enumerable.Range(0,5) from y in Enumerable.Range(0,5) select new LayoutCell(new(x,y),x is >0 and <3&&y is >0 and <3,true,true,true,true,true,0)).ToList();
        var growth=grid.Where(c=>c.Plantable).ToDictionary(c=>c.Tile,c=>4);
        var shortCrop=new EconomySeed("short",0,new("short","shop","shop",0,20,99,1),35,-1,28,false,0,0,growth,new());
        var longCrop=new EconomySeed("long",0,new("long","shop","shop",0,20,99,1),150,3,28,false,0,0,growth.ToDictionary(p=>p.Key,p=>10),new());
        var cash=CropPortfolio.Plan(new(0,1,100,80,20,4,4,"cashflow",new(0,0),grid,new(),new(){shortCrop,longCrop}));
        check(cash.Plants.Count==4&&cash.Plants.All(p=>p.Seed=="short")&&cash.Spent==80,"cash-constrained opening chooses realizable short-cycle income instead of attractive unripe long crops");
    }
}
