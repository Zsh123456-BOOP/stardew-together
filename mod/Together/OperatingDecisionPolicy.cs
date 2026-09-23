using System.Text.Json;
namespace Together;
public static class OperatingDecisionPolicy {
    public static bool InvestmentOwnsPlayer(bool enabled,string phase)=>enabled&&phase is "start_planning" or "planning" or "executing" or "observing_shop";
    // A completed/declined investment is reconsidered after new income or a new
    // day, not merely because planting freed a bag slot or changed crop counts.
    public static bool SeedReviewUseful(int day,int cash,int reviewedDay,int reviewedCash,bool quotesKnown,IEnumerable<(int Price,int Stock,int Harvests)> offers)=>
        cash>=0&&(reviewedDay!=day||cash>reviewedCash)&&(!quotesKnown?cash>0:offers.Any(o=>o.Stock>0&&o.Harvests>0&&o.Price<=cash));
    public static JsonElement ResourceDefaults(JsonElement args,int pendingCare) {
        var values=args.Deserialize<Dictionary<string,JsonElement>>()!;
        if(!values.ContainsKey("reserve_stamina"))values["reserve_stamina"]=JsonSerializer.SerializeToElement(Math.Clamp(pendingCare,0,270));
        if(!values.ContainsKey("labor_review"))values["labor_review"]=JsonSerializer.SerializeToElement(new{purpose=AgentPurpose(args),followup="按当前待办照料预留体力，达到数量或体力边界即停止",care="preserve"});
        return JsonSerializer.SerializeToElement(values);
    }
    private static string AgentPurpose(JsonElement args)=>args.TryGetProperty("purpose",out var p)&&p.ValueKind==JsonValueKind.String&&!string.IsNullOrWhiteSpace(p.GetString())?p.GetString()!:"按本次明确的材料数量或项目缺口备料";

    public static string ShopObservation(string window,int time,int open,int close,bool atShop)=>window!="available"||time>=close?"closed_today_or_window_ended":time<open?"before_opening":!atShop?"not_at_shop":"shop_menu_not_open";
    public static bool CanCompletePurchaseVisit(JsonElement args,string[] locations) {
        int Number(string key)=>args.TryGetProperty(key,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out int n)?n:-1;
        return locations.Distinct().Count()==1&&Number("count")>0&&Number("budget")>=0&&Number("max_unit_price")>=0&&Number("keep_gold")>=0&&
            (!args.TryGetProperty("currency",out var currency)||currency.GetInt32()==0)&&
            (!args.TryGetProperty("trade_item",out var trade)||string.IsNullOrEmpty(trade.GetString()));
    }
    public static bool ReuseStorage(int deployed,bool acceptsCargo,bool additional)=>deployed>0&&acceptsCargo&&!additional;
    public static object Labor(float stamina,int maximum,int todayPending,int dry,int manualTomorrow,int ownedSeeds,int farming)=>new {
        stamina,maximum_stamina=maximum,today_pending_conservative=todayPending,today_uncommitted_estimate=Math.Max(0,stamina-todayPending),dry_crops=dry,
        next_dry_day_manual_crops=manualTomorrow,next_dry_day_water_estimate=Math.Round(manualTomorrow*Math.Max(0,2-.1*farming),1),
        care_note="today_pending仅表示今天此刻；下个晴天需重新浇水，不能用今天已浇水推断明天免浇。",unplanted_seeds=ownedSeeds,new_plot_base_till_and_water=Math.Round(2*Math.Max(0,2-.1*farming),1),
        forecast="体力为单格未蓄力基础估算，未计清障/路程；下个晴天假设现有作物留田，洒水器覆盖已扣除，不预言天气、收获或次晨满体力。没有额外隐藏预留或株数上限。"
    };
}
