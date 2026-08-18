using StardewCropCalculatorLibrary;

namespace Together;
public sealed record SeedQuote(string Seed,string Shop,string Location,int Day,int Price,int Stock,int Units);
public sealed record EconomySeed(string Seed,int Owned,SeedQuote? Quote,int SalePrice,int Regrow,int LastDay,bool Trellis,int ReserveYield,int NeedForCollection,Dictionary<FarmCell,int> Growth,HashSet<FarmCell> Irrigated);
public sealed record EconomySnapshot(int Day,int Date,int Money,int Budget,int KeepGold,int Limit,int ManualLimit,string Priority,FarmCell Start,List<LayoutCell> Grid,List<FarmCell> Anchors,List<EconomySeed> Seeds);
public sealed record EconomyPlant(string Seed,FarmCell Tile,int Growth,bool Manual,int PurchaseCost);
public sealed record EconomyPurchase(string Seed,string Shop,string Location,int Count,int UnitPrice);
public sealed record EconomyResult(List<EconomyPlant> Plants,List<EconomyPurchase> Purchases,int Spent,int Manual,object Calendar,string StopReason);

// A bounded, greedy portfolio over detached native facts. It prioritizes feasible
// work and reserves rather than claiming a globally optimal farming strategy.
public static class CropPortfolio {
    public static EconomyResult Plan(EconomySnapshot s,CancellationToken cancellation=default) {
        var watch=System.Diagnostics.Stopwatch.StartNew();var grid=s.Grid.ToList();var anchors=s.Anchors.ToList();
        var plants=new List<EconomyPlant>();var used=new Dictionary<string,int>();var purchased=new Dictionary<string,int>();
        int spent=0,manual=0;string stop="requested_land_limit";
        while(plants.Count<s.Limit) {
            cancellation.ThrowIfCancellationRequested();if(watch.ElapsedMilliseconds>3000){stop="planning_time_budget_partial";break;}
            (EconomySeed Seed,FarmCell Tile,int Growth,bool Manual,int Cost,double Score)? best=null;
            foreach(var seed in s.Seeds) {
                if(used.Count>=5&&!used.ContainsKey(seed.Seed))continue;
                int n=used.GetValueOrDefault(seed.Seed);bool buy=n>=seed.Owned;
                if(buy&&(seed.Quote==null||seed.Quote.Units!=1||purchased.GetValueOrDefault(seed.Seed)>=seed.Quote.Stock))continue;
                int price=buy?seed.Quote!.Price:0;if(price<0||price>s.Budget-spent||price>s.Money-s.KeepGold-spent)continue;
                var candidates=grid.Select(c=>c with{Plantable=c.Plantable&&seed.Growth.TryGetValue(c.Tile,out int days)&&s.Date+days<=seed.LastDay,Irrigated=c.Irrigated||seed.Irrigated.Contains(c.Tile)}).ToList();
                var layout=FarmLayout.Choose(candidates,s.Start,anchors,1,seed.Trellis,s.ManualLimit-manual);if(layout.Tiles.Count==0)continue;
                var at=layout.Tiles[0];int growth=seed.Growth[at];bool water=!candidates.First(c=>c.Tile==at).Irrigated;
                var crop=new Crop(seed.Seed,growth,seed.Regrow,price,seed.SalePrice);int yields=crop.NumHarvests(s.Date,seed.LastDay);
                int reserved=Math.Min(yields,Math.Max(0,seed.ReserveYield-plants.Where(p=>p.Seed==seed.Seed).Sum(p=>new Crop(seed.Seed,p.Growth,seed.Regrow,0,seed.SalePrice).NumHarvests(s.Date,seed.LastDay))));
                double profit=(yields-reserved)*seed.SalePrice-price;
                double score=s.Priority=="collection"&&n<seed.NeedForCollection?100000+profit:s.Priority=="low_labor"?profit/Math.Max(1,water?seed.LastDay-s.Date:1):profit;
                if(reserved>0)score+=10000;
                if(score<=0&&seed.NeedForCollection<=n)continue;
                if(best==null||score>best.Value.Score)best=(seed,at,growth,water,price,score);
            }
            if(best==null){stop="budget_season_supply_space_or_labor_limit";break;}
            var choice=best.Value;plants.Add(new(choice.Seed.Seed,choice.Tile,choice.Growth,choice.Manual,choice.Cost));
            int old=used.GetValueOrDefault(choice.Seed.Seed);used[choice.Seed.Seed]=old+1;if(old>=choice.Seed.Owned)purchased[choice.Seed.Seed]=purchased.GetValueOrDefault(choice.Seed.Seed)+1;
            spent+=choice.Cost;if(choice.Manual)manual++;
            grid=grid.Select(c=>c.Tile==choice.Tile?c with{Plantable=false,Passable=!choice.Seed.Trellis&&c.Passable}:c).ToList();
            if(choice.Seed.Trellis)foreach(var neighbour in new[]{new FarmCell(choice.Tile.X-1,choice.Tile.Y),new FarmCell(choice.Tile.X+1,choice.Tile.Y),new FarmCell(choice.Tile.X,choice.Tile.Y-1),new FarmCell(choice.Tile.X,choice.Tile.Y+1)})if(grid.Any(c=>c.Tile==neighbour&&c.Passable))anchors.Add(neighbour);
        }
        var purchases=purchased.Select(p=>{var quote=s.Seeds.First(x=>x.Seed==p.Key).Quote!;return new EconomyPurchase(p.Key,quote.Shop,quote.Location,p.Value,quote.Price);}).ToList();
        int end=Math.Max(28,s.Seeds.Select(x=>x.LastDay).DefaultIfEmpty(28).Max());
        var calendar=new GameStateCalendar(end,s.Limit,s.Money);
        foreach(var group in plants.GroupBy(p=>new{p.Seed,p.Growth,p.PurchaseCost})) {
            var seed=s.Seeds.First(x=>x.Seed==group.Key.Seed);
            CalendarCashFlow.Apply(calendar,group.Count(),new Crop(seed.Seed,group.Key.Growth,seed.Regrow,group.Key.PurchaseCost,seed.SalePrice),s.Date,seed.LastDay);
        }
        // Retained quest/bundle yields are unavailable for sale. Deduct their
        // base-price proceeds from every projected wallet after the relevant harvest.
        foreach(var seed in s.Seeds) {
            int keep=seed.ReserveYield;
            foreach(int harvest in plants.Where(p=>p.Seed==seed.Seed).SelectMany(p=>new Crop(seed.Seed,p.Growth,seed.Regrow,0,seed.SalePrice).HarvestDays(s.Date,seed.LastDay)).OrderBy(x=>x)) {
                if(keep--<=0)break;for(int day=harvest+1;day<=end+1;day++)calendar.GameStates[day].Wallet-=seed.SalePrice;
            }
        }
        return new(plants,purchases,spent,manual,calendar.GameStates.Where(x=>x.Key>=s.Date&&(x.Key==s.Date||x.Key==end+1||x.Value.DayOfInterest)).Select(x=>new{day=x.Key,projected_gold=x.Value.Wallet,free_plots=x.Value.FreeTiles}).ToArray(),stop);
    }
}
