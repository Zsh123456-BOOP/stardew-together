namespace Together;
public sealed record ProcessingLane(string Id,string Seed,int Count,int Days,double Margin,int ReadyDay,int MaxBatches);
public sealed record ProcessingEstimate(double AdditionalMargin,int ProcessedUnits,int LastPaymentDay,string Assumptions);
public static class ProcessingForecast {
    // One timeline per real machine ID. Alternatives never multiply capacity.
    // Only initial crops and observed fuel are valued, not hypothetical machines.
    public static ProcessingEstimate Evaluate(EconomySnapshot snapshot,IReadOnlyList<EconomyPlant> plants) {
        var events=new List<(int Day,string Seed)>();
        foreach(var plant in plants) {
            var seed=snapshot.Seeds.First(s=>s.Seed==plant.Seed);
            for(int day=snapshot.Date+plant.Growth;day<=Math.Min(28,seed.LastDay);day+=seed.Regrow>0?seed.Regrow:1000)events.Add((day,plant.Seed));
        }
        var reserves=snapshot.Seeds.ToDictionary(s=>s.Seed,s=>s.ReserveYield);
        var ready=snapshot.Processing.GroupBy(l=>l.Id).ToDictionary(g=>g.Key,g=>g.Max(l=>l.ReadyDay));
        var used=new Dictionary<string,int>();var stocks=new Dictionary<string,int>();double margin=0;int units=0,last=snapshot.Date;
        for(int day=snapshot.Date;day<=28;day++) {
            foreach(var e in events.Where(e=>e.Day==day)) {
                if(reserves.GetValueOrDefault(e.Seed)>0){reserves[e.Seed]--;continue;}
                stocks[e.Seed]=stocks.GetValueOrDefault(e.Seed)+1;
            }
            foreach(var lane in snapshot.Processing.OrderByDescending(l=>l.Margin/Math.Max(1,l.Days)).ThenBy(l=>l.Id,StringComparer.Ordinal)) {
                if(ready[lane.Id]>day||stocks.GetValueOrDefault(lane.Seed)<lane.Count||used.GetValueOrDefault(lane.Id)>=lane.MaxBatches||day+lane.Days+1>29)continue;
                stocks[lane.Seed]-=lane.Count;used[lane.Id]=used.GetValueOrDefault(lane.Id)+1;ready[lane.Id]=day+lane.Days;
                margin+=lane.Margin;units+=lane.Count;last=Math.Max(last,day+lane.Days+1);
            }
        }
        return new(margin,units,last,"仅已安装设备、当前可用燃料与本轮种植的条件增值估计；不提前入账，不保证随机产量，不计未建机器。");
    }
}
