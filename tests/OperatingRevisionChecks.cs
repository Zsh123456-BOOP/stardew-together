using System.Text.Json;
using Together;
static class OperatingRevisionChecks {
    public static void Run(Action<bool,string> check) {
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
        var grid=(from x in Enumerable.Range(0,5) from y in Enumerable.Range(0,5) select new LayoutCell(new(x,y),x is >0 and <3&&y is >0 and <3,true,true,true,true,true,0)).ToList();
        var growth=grid.Where(c=>c.Plantable).ToDictionary(c=>c.Tile,c=>4);
        var shortCrop=new EconomySeed("short",0,new("short","shop","shop",0,20,99,1),35,-1,28,false,0,0,growth,new());
        var longCrop=new EconomySeed("long",0,new("long","shop","shop",0,20,99,1),150,3,28,false,0,0,growth.ToDictionary(p=>p.Key,p=>10),new());
        var cash=CropPortfolio.Plan(new(0,1,100,80,20,4,4,"cashflow",new(0,0),grid,new(),new(){shortCrop,longCrop}));
        check(cash.Plants.Count==4&&cash.Plants.All(p=>p.Seed=="short")&&cash.Spent==80,"cash-constrained opening chooses realizable short-cycle income instead of attractive unripe long crops");
    }
}
