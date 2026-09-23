using System.Text.Json;
using System.Text.Json.Nodes;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private static int NativePurchaseSpent() {
        if(!Game1.player.modData.TryGetValue("stardewagent.together/economy",out var raw))return 0;
        using var doc=JsonDocument.Parse(raw);var row=doc.RootElement;
        return row.GetProperty("Day").GetInt32()==Game1.Date.TotalDays?row.GetProperty("Spent").GetInt32():0;
    }
    private static int NativeCommandSpent(string? command) {
        if(command==null||!Game1.player.modData.TryGetValue("stardewagent.together/economy",out var raw))return 0;
        using var doc=JsonDocument.Parse(raw);var r=doc.RootElement;
        return r.GetProperty("Day").GetInt32()==Game1.Date.TotalDays&&r.TryGetProperty("SpentByCommand",out var commands)&&commands.TryGetProperty(command,out var amount)?amount.GetInt32():0;
    }
    private int UnscheduledDevelopmentCash()=>Data.Business.PendingAsset.Length==0?0:NativeCosts.UnscheduledDevelopment(Data.Operating.DevelopmentCashHeld,Data.Business.Activity==Data.Business.PendingAsset?Data.Autoplay.Schedule.Tasks.Where(t=>Data.Business.Tasks.Contains(t.spec.id)&&!t.Terminal).GroupBy(t=>t.spec.intent_id).Sum(IntentCash):0);
    private int BusinessCashAvailable(bool ownDevelopment=false)=>AutonomyPolicy.Cash(Game1.player.Money,Data.Business.KeepGold,Data.Business.DailyBudget,NativePurchaseSpent(),PendingPurchaseCash(),ownDevelopment?0:UnscheduledDevelopmentCash());
    private void FinalizeCashReservation(ScheduledAgentTask task,JsonElement receipt) {
        int cap=NativeCosts.PurchaseCap(task.spec.tool,task.spec.args);if(cap==0)return;
        bool observed=task.command_id==null||receipt.TryGetProperty("after",out _)&&receipt.TryGetProperty("effects",out _);
        if(observed)Data.Business.UnverifiedCash.Remove(task.spec.id);
        else Data.Business.UnverifiedCash[task.spec.id]=Math.Max(0,cap-NativeCommandSpent(task.command_id));
    }
    private void RecordNativePurchases(PlayerAction action) {
        const string key="stardewagent.together/economy";
        var ledger=Game1.player.modData.TryGetValue(key,out var raw)?JsonNode.Parse(raw)!.AsObject():new JsonObject();
        NativeCosts.EnterDay(ledger,Game1.Date.TotalDays);
        ledger["SpentByCurrency"]??=new JsonObject();ledger["SpentByCommand"]??=new JsonObject();ledger["Purchased"]??=new JsonObject();ledger["PurchasedItems"]??=new JsonObject();ledger["NativeReceipts"]??=new JsonArray();ledger["Entries"]??=new JsonArray();
        var receipts=ledger["NativeReceipts"]!.AsArray();var items=ledger["PurchasedItems"]!.AsObject();var entries=ledger["Entries"]!.AsArray();bool changed=false;
        for(int n=0;n<action.effects.Count;n++) {
            var e=JsonSerializer.SerializeToElement(action.effects[n]);
            var costs=NativeCosts.Read(e).ToArray();if(costs.Length==0)continue;
            string id=action.command_id+":"+n;if(receipts.Any(v=>v?.GetValue<string>()==id))continue;
            foreach(var c in costs) {
                var currencies=ledger["SpentByCurrency"]!.AsObject();currencies[c.Currency]=(currencies[c.Currency]?.GetValue<int>()??0)+c.Amount;
                if(c.Currency=="gold") {ledger["Spent"]=(ledger["Spent"]?.GetValue<int>()??0)+c.Amount;var commands=ledger["SpentByCommand"]!.AsObject();commands[action.command_id]=(commands[action.command_id]?.GetValue<int>()??0)+c.Amount;}
                if(c.Units>0&&c.Item.Length>0)items[c.Item]=(items[c.Item]?.GetValue<int>()??0)+c.Units;
                if(c.Units>0&&c.Item.Length>0&&ItemRegistry.Create(c.Item).Category==-74)Data.FarmInvestment.Purchase.Receive(Game1.Date.TotalDays,id,c.Item,c.Units);
                entries.Add($"{Game1.Date.TotalDays}/{Game1.timeOfDay} 原生交易 {c.Item} ×{c.Units}，{c.Currency} 支出 {c.Amount}");
            }
            receipts.Add(id);changed=true;
        }
        if(changed){while(entries.Count>100)entries.RemoveAt(0);Game1.player.modData[key]=ledger.ToJsonString();Data.Autoplay.Record("purchase_ledger",ledger.ToJsonString());}
    }
}
