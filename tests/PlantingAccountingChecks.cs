using System.Text.Json;
using System.Text.Json.Nodes;
using Together;

public static class PlantingAccountingChecks {
    public static void Run(Action<bool,string> check) {
        var dry=new LayoutCell(new(0,0),true,true,false,false,false,false,0);
        check(FarmLayout.PlantingEnergy(new[]{dry})==4,"new dry plot costs two till plus two water energy");
        check(FarmLayout.PlantingEnergy(new[]{dry with{Tilled=true,Watered=true}})==0,"prepared wet plot does not reserve energy again");
        check(FarmLayout.PlantingEnergy(new[]{dry with{Irrigated=true}})==4,"future sprinkler coverage does not imply watered on planting day");
        check(FarmLayout.PlantingEnergy(new[]{dry with{TillEnergy=0,WaterEnergy=0}})==0,"efficient native tools permit zero-energy cultivation");
        check(FarmLayout.PlantingEnergy(new[]{dry with{TillEnergy=1.3f,WaterEnergy=1.3f},dry with{TillEnergy=1.3f,WaterEnergy=1.3f}})==6,"skill-adjusted fractional swings round once for the batch");
        var grid=Enumerable.Range(0,14).Select(x=>dry with{Tile=new(x,0)}).ToList();
        var bed=FarmLayout.Choose(grid,new(0,0),Array.Empty<FarmCell>(),12,false,20,energyBudget:66);
        check(bed.Tiles.Count==12,"sixty-six energy no longer cuts twelve clear plots in half");
        var snapshot=new EconomySnapshot(0,1,500,500,0,12,20,"income",new(0,0),grid,new(),new(){new("seed",0,new("seed","shop","shop",0,20,99,1),35,-1,28,false,0,0,grid.ToDictionary(c=>c.Tile,_=>4),new())}){EnergyBudget=66};
        var planned=CropPortfolio.Plan(snapshot);
        check(planned.Plants.Count==12&&planned.FirstDayEnergy==48&&planned.Purchases.Sum(p=>p.Count)==12,"portfolio and layout share native first-day cultivation cost");
        var order=new SeedPurchaseManifest();order.Select(0,new(){{"potato",8},{"parsnip",5}});
        order.FinalizePlan(0,new Dictionary<string,int>{{"potato",1},{"parsnip",5}},"energy_limit");
        order.Receive(0,"p1","potato",1);order.Receive(0,"p2","parsnip",5);
        check(order.Remaining("potato")==0&&order.NotScheduled("potato")==7&&order.Purchased["potato"]==1,"reduced plan leaves seven unselected potatoes, not seven missing purchases");
        string before=JsonSerializer.Serialize(order);
        for(int i=0;i<3;i++)check(order.Validate(0,"parsnip",5,"explicit additional purpose")==null&&JsonSerializer.Serialize(order)==before,"failed cash check after additional approval leaves no phantom order");
        check(order.Validate(0,"potato",7,"")!=null,"unused selection cap cannot silently authorize a repeat trip");
        try{order.FinalizePlan(0,new Dictionary<string,int>{{"potato",1},{"parsnip",999}},"invalid");throw new Exception("expected invalid plan");}catch(InvalidOperationException){}
        check(JsonSerializer.Serialize(order)==before,"invalid mixed purchase plan is rejected atomically");
        order.Receive(0,"extra","potato",7);order=JsonSerializer.Deserialize<SeedPurchaseManifest>(JsonSerializer.Serialize(order))!;order.Receive(0,"extra","potato",7);
        check(order.Purchased["potato"]==8&&order.Remaining("potato")==0,"actual additional delivery survives reload and duplicate receipt without double accounting");
        order.Select(0,new(){{"parsnip",5}});order.FinalizePlan(0,new Dictionary<string,int>{{"parsnip",5}},"new batch");order.Receive(0,"partial","parsnip",2);
        check(order.Remaining("parsnip")==3,"partial native delivery retains only genuinely outstanding units");
        order.Close(0,"interrupted");check(order.Remaining("parsnip")==0&&order.Purchased["parsnip"]==7,"closing failed workflow removes pending purchase but preserves delivered goods");
        for(int day=1;day<=7;day++) {
            order.Select(day,new(){{"parsnip",day}});order.FinalizePlan(day,new Dictionary<string,int>{{"parsnip",day}},"daily");order.Receive(day,"receipt","parsnip",day);
            order=JsonSerializer.Deserialize<SeedPurchaseManifest>(JsonSerializer.Serialize(order))!;order.Receive(day,"receipt","parsnip",day);
            check(order.Purchased.Count==1&&order.Purchased["parsnip"]==day&&order.Remaining("parsnip")==0,$"day {day} uses today's purchases with persistent replay protection");
        }
        order.Receive(6,"late","parsnip",99);check(order.Day==7&&order.Purchased["parsnip"]==7,"late prior-day receipt cannot reset today's manifest");
        var ledger=JsonNode.Parse("{\"Day\":0,\"Spent\":500,\"SpentByCurrency\":{\"gold\":500},\"SpentByCommand\":{\"cmd\":500},\"Purchased\":{\"cmd\":10},\"PurchasedItems\":{\"seed\":10},\"NativeReceipts\":[\"old\"],\"Entries\":[{\"day\":0,\"spent\":500}]}")!.AsObject();
        NativeCosts.EnterDay(ledger,1);
        check(ledger["Spent"]!.GetValue<int>()==0&&new[]{"SpentByCurrency","SpentByCommand","Purchased","PurchasedItems"}.All(k=>ledger[k]!.AsObject().Count==0)&&ledger["NativeReceipts"]!.AsArray().Count==0&&ledger["Entries"]!.AsArray().Count==1,"daily native accounting clears all daily totals and retains dated historical evidence");
        ledger["Spent"]=20;NativeCosts.EnterDay(ledger,1);check(ledger["Spent"]!.GetValue<int>()==20,"same-day refresh does not erase real spending");
    }
}
