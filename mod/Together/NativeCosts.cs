using System.Text.Json;
namespace Together;

public sealed record NativeCost(string Currency,int Amount,string Item,int Units);
public static class NativeCosts {
    public static IEnumerable<NativeCost> Read(JsonElement e) {
        string Text(string k)=>e.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
        int Number(string k,int fallback=0)=>e.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.Number&&v.TryGetInt32(out int n)?n:fallback;
        string kind=Text("kind");int cost=Math.Max(0,Number(kind=="native_vault_bundle"?"spent":"cost"));
        if(kind is "native_purchase" or "native_recipe_learned" or "native_tool_upgrade_started") {
            int currency=Number("currency",-1);if(currency>=0)yield return new(currency==0?"gold":"shop_currency:"+currency,cost,Text("item"),Number("units"));
            if(Text("trade_item").Length>0)yield return new("item:"+Text("trade_item"),Math.Max(0,Number("trade_count")),Text("item"),0);
        }else if(kind is "native_construction_started" or "native_animal_purchased" or "native_house_upgrade_started" or "native_joja_purchase" or "native_transport_departure" or "native_geode_opened" or "native_vault_bundle")yield return new("gold",cost,Text("item"),0);
        else if(kind=="native_island_upgrade")yield return new("walnuts",cost,"",0);
        else if(kind=="native_beach_service")yield return new("gold",Math.Max(0,Number("gold_paid")),"",0);
    }
    public static int UnscheduledDevelopment(int selected,int scheduledForSameIntent)=>Math.Max(0,selected-Math.Max(0,scheduledForSameIntent));
    public static int PurchaseCap(string tool,JsonElement args) {
        int Number(string key,int fallback=0)=>AgentNumbers.Read(args,key,fallback);
        if(tool is "player.buy" or "player.procure")return (int)Math.Clamp(Math.Min((long)Number("budget"),(long)Math.Max(0,Number("max_unit_price",Number("budget")))*Math.Max(0,Number("count",1))),0,int.MaxValue);
        return tool is "player.build" or "player.acquire_animal" or "player.buy_animal" or "player.upgrade_house" or "player.joja" or "player.transport" or "player.geodes" or "player.bundle" or "player.beach"?Math.Max(0,Number("budget")):0;
    }
}
