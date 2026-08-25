namespace Together;

public sealed record ForecastPlanting(int Day,string Seed,int Count,int Cost);
public sealed record ForecastDay(int Day,long Gold,int ManualWater,int Planted,int Harvested,int ReservedYield,int PurchaseCost);
public sealed record SeasonProjection(int ThroughDay,int PlannedEndDay,long Gold,List<ForecastPlanting> Replantings,List<ForecastDay> Days,string StopReason,string Assumptions);

// Bounded beam search over detached facts. Tomorrow's shipping proceeds cannot
// finance today's seed purchases. Future shop quotes are conditional scenarios,
// never executable orders or additions to the real budget ledger.
public static class SeasonCashForecast {
    private sealed record Bed(FarmCell Tile,string Seed,int Harvest,bool Manual,bool TrellisAllowed);
    private sealed class State {
        public long Gold;
        public List<Bed> Beds=new();
        public Dictionary<string,int> Owned=new(),Stock=new(),Reserve=new();
        public Dictionary<int,long> Pending=new();
        public List<ForecastPlanting> Orders=new();
        public List<ForecastDay> Days=new();
        public int Spent,Harvested,Kept;
        public State Clone()=>new(){Gold=Gold,Beds=Beds.ToList(),Owned=new(Owned),Stock=new(Stock),Reserve=new(Reserve),Pending=new(Pending),Orders=Orders.ToList(),Days=Days.ToList(),Spent=Spent,Harvested=Harvested,Kept=Kept};
    }
    private static bool ShopDay(SeedQuote? quote,int date)=>quote is {Units:1}&&date<=28&&(quote.Shop=="SeedShop"?date%7!=3:quote.Shop is "Joja" or "Sandy");
    public static SeasonProjection Plan(EconomySnapshot snapshot,List<EconomyPlant> initial,int spent,CancellationToken cancellation=default) {
        if(spent<0||spent>snapshot.Budget||snapshot.Money-spent<snapshot.KeepGold)throw new InvalidOperationException("forecast_initial_budget_invalid");
        var clock=System.Diagnostics.Stopwatch.StartNew();var seeds=snapshot.Seeds.ToDictionary(s=>s.Seed);var grid=snapshot.Grid.ToDictionary(c=>c.Tile);int end=Math.Clamp(snapshot.Seeds.Select(s=>s.LastDay).DefaultIfEmpty(28).Max(),28,56);
        var start=new State{Gold=snapshot.Money-spent};
        foreach(var seed in snapshot.Seeds) {
            int planted=initial.Count(p=>p.Seed==seed.Seed),bought=Math.Max(0,planted-seed.Owned);
            start.Owned[seed.Seed]=Math.Max(0,seed.Owned-planted);start.Stock[seed.Seed]=Math.Max(0,(seed.Quote?.Stock??0)-bought);start.Reserve[seed.Seed]=seed.ReserveYield;
        }
        start.Beds=initial.Select(p=>new Bed(p.Tile,p.Seed,snapshot.Date+p.Growth,p.Manual,seeds[p.Seed].Trellis)).ToList();
        start.Days.Add(new(snapshot.Date,start.Gold,initial.Count(p=>p.Manual),initial.Count,0,0,spent));
        var beam=new List<State>{start};int through=snapshot.Date;string stop="season_scenario_complete";
        for(int day=snapshot.Date+1;day<=end+1;day++) {
            cancellation.ThrowIfCancellationRequested();if(clock.ElapsedMilliseconds>800){stop="forecast_time_budget_partial";break;}
            var candidates=new List<State>();
            foreach(var state in beam) {
                var next=state.Clone();next.Gold+=next.Pending.GetValueOrDefault(day);next.Pending.Remove(day);next.Spent=next.Harvested=next.Kept=0;
                for(int i=0;i<next.Beds.Count;i++) {
                    var bed=next.Beds[i];if(bed.Seed.Length==0||bed.Harvest!=day)continue;var seed=seeds[bed.Seed];next.Harvested++;
                    if(next.Reserve.GetValueOrDefault(seed.Seed)>0){next.Reserve[seed.Seed]--;next.Kept++;}
                    else next.Pending[day+1]=next.Pending.GetValueOrDefault(day+1)+seed.SalePrice;
                    next.Beds[i]=seed.Regrow>0&&day+seed.Regrow<=Math.Min(end,seed.LastDay)?bed with{Harvest=day+seed.Regrow}:bed with{Seed="",Harvest=0,Manual=false};
                }
                // Three deterministic portfolio orderings and two capacity levels
                // leave room for keeping cash, cheaper reinvestment and variety.
                candidates.Add(next);
                if(day>28)continue; // no unobserved next-season seed/fertilizer offers
                foreach(var order in new[]{0,1,2})foreach(double fraction in new[]{1.0,0.5}) {
                    var branch=next.Clone();var options=snapshot.Seeds.Where(s=>s.Growth.Count>0).OrderByDescending(s=>order switch {
                        0=>(double)(s.SalePrice-(s.Quote?.Price??0))/Math.Max(1,s.Growth.Values.Min()),
                        1=>-(double)(s.Quote?.Price??int.MaxValue),
                        _=>s.ReserveYield>0?100000d:s.NeedForCollection>0?10000d:s.SalePrice
                    }).ThenBy(s=>s.Seed,StringComparer.Ordinal);
                    int limit=Math.Max(1,(int)Math.Ceiling(branch.Beds.Count(b=>b.Seed.Length==0)*fraction)),added=0;
                    foreach(var seed in options) {
                        int quantity=0,cost=0;
                        for(int i=0;i<branch.Beds.Count&&added<limit;i++) {
                            var bed=branch.Beds[i];if(bed.Seed.Length>0||seed.Trellis&&!bed.TrellisAllowed||!seed.Growth.TryGetValue(bed.Tile,out int growth)||day+growth>Math.Min(end,seed.LastDay))continue;
                            bool manual=!grid[bed.Tile].Irrigated&&!seed.Irrigated.Contains(bed.Tile);
                            if(manual&&branch.Beds.Count(b=>b.Seed.Length>0&&b.Manual)>=snapshot.ManualLimit)continue;
                            bool owned=branch.Owned.GetValueOrDefault(seed.Seed)>0;
                            int price=owned?0:seed.Quote?.Price??int.MaxValue;
                            if(!owned&&(!ShopDay(seed.Quote,day)||branch.Stock.GetValueOrDefault(seed.Seed)<=0))continue;
                            if(price<0||price>snapshot.Budget-branch.Spent||branch.Gold-price<snapshot.KeepGold)continue;
                            if(owned)branch.Owned[seed.Seed]--;else branch.Stock[seed.Seed]--;
                            branch.Gold-=price;branch.Spent+=price;cost+=price;quantity++;added++;
                            branch.Beds[i]=bed with{Seed=seed.Seed,Harvest=day+growth,Manual=manual};
                        }
                        if(quantity>0)branch.Orders.Add(new(day,seed.Seed,quantity,cost));
                    }
                    if(added>0)candidates.Add(branch);
                }
            }
            double Value(State state)=>state.Gold+state.Pending.Where(k=>k.Key<=end+1).Sum(k=>k.Value)+state.Beds.Where(b=>b.Seed.Length>0).Sum(b=>{
                var seed=seeds[b.Seed];if(b.Harvest>end)return 0d;int harvests=seed.Regrow>0?1+(Math.Min(end,seed.LastDay)-b.Harvest)/seed.Regrow:1;return Math.Max(0,harvests)*seed.SalePrice*.85;
            });
            beam=candidates.OrderByDescending(Value).ThenBy(s=>s.Orders.Count).DistinctBy(s=>s.Gold+":"+string.Join(';',s.Beds.Select(b=>b.Seed+"@"+b.Harvest))+":"+string.Join(';',s.Owned.OrderBy(k=>k.Key).Select(k=>k.Key+"="+k.Value))+":"+string.Join(';',s.Stock.OrderBy(k=>k.Key).Select(k=>k.Key+"="+k.Value))+":"+string.Join(';',s.Pending.OrderBy(k=>k.Key).Select(k=>k.Key+"="+k.Value))+":"+string.Join(';',s.Reserve.OrderBy(k=>k.Key).Select(k=>k.Key+"="+k.Value))).Take(12).ToList();
            foreach(var state in beam)state.Days.Add(new(day,state.Gold,state.Beds.Count(b=>b.Seed.Length>0&&b.Manual),state.Orders.Where(o=>o.Day==day).Sum(o=>o.Count),state.Harvested,state.Kept,state.Spent));
            through=day;
        }
        var best=beam.OrderByDescending(s=>s.Gold+s.Pending.Where(p=>p.Key<=end+1).Sum(p=>p.Value)).First();
        return new(through,end+1,best.Gold,best.Orders,best.Days,stop,"条件预测：仅已选地块、当前季已观察种子报价与库存上限；未来价格/供货/营业需重新现场核验。保守每次一份基础品质，维护按日完成、预留产物不出货、次日入账；不把未来收入用于实际采购。不预测跨季新报价或肥料续效，跨季仅保留原作物复收；不含加工收益。有限宽度搜索，不保证全局最优。");
    }
}
