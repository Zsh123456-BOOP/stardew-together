using System.Text.Json;
namespace Together;
public static class OperatingDecisionPolicy {
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
        unplanted_seeds=ownedSeeds,new_plot_base_till_and_water=Math.Round(2*Math.Max(0,2-.1*farming),1),
        forecast="体力为单格未蓄力基础估算，未计清障/路程；下个晴天假设现有作物留田，洒水器覆盖已扣除，不预言天气、收获或次晨满体力。没有额外隐藏预留或株数上限。"
    };
}
