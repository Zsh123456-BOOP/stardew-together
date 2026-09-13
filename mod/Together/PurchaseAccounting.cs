using System.Text.Json;
using System.Text.Json.Nodes;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private void RecordNativePurchases(PlayerAction action) {
        const string key="stardewagent.together/economy";
        var ledger=Game1.player.modData.TryGetValue(key,out var raw)?JsonNode.Parse(raw)!.AsObject():new JsonObject();
        if(ledger["Day"]?.GetValue<int>()!=Game1.Date.TotalDays){ledger["Day"]=Game1.Date.TotalDays;ledger["Spent"]=0;ledger["NativeReceipts"]=new JsonArray();}
        ledger["Purchased"]??=new JsonObject();ledger["PurchasedItems"]??=new JsonObject();ledger["NativeReceipts"]??=new JsonArray();ledger["Entries"]??=new JsonArray();
        var receipts=ledger["NativeReceipts"]!.AsArray();var items=ledger["PurchasedItems"]!.AsObject();var entries=ledger["Entries"]!.AsArray();bool changed=false;
        for(int n=0;n<action.effects.Count;n++) {
            var e=JsonSerializer.SerializeToElement(action.effects[n]);
            string kind=AgentToolRegistry.Text(e,"kind");if(kind is not ("native_purchase" or "native_recipe_learned" or "native_tool_upgrade_started")||AgentToolRegistry.Number(e,"currency",-1)!=0)continue;
            string id=action.command_id+":"+n;if(receipts.Any(v=>v?.GetValue<string>()==id))continue;
            int cost=AgentToolRegistry.Number(e,"cost",0),units=AgentToolRegistry.Number(e,"units",0);string item=AgentToolRegistry.Text(e,"item");
            ledger["Spent"]=(ledger["Spent"]?.GetValue<int>()??0)+cost;items[item]=(items[item]?.GetValue<int>()??0)+units;
            receipts.Add(id);entries.Add($"{Game1.Date.TotalDays}/{Game1.timeOfDay} 玩家原生采购 {item} ×{units}，支出 {cost}");changed=true;
        }
        if(changed){while(entries.Count>100)entries.RemoveAt(0);Game1.player.modData[key]=ledger.ToJsonString();Data.Autoplay.Record("purchase_ledger",ledger.ToJsonString());}
    }
}
