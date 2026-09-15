using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private int PendingPurchaseCash()=>(int)Math.Min(int.MaxValue,Data.Autoplay.Schedule.Tasks
        .Where(t=>!t.Terminal).GroupBy(t=>t.spec.intent_id).Sum(g=>(long)IntentCash(g)));
    private object InvestmentObservation()=>new {
        automation_enabled=Data.FarmInvestment.Enabled,repeat=Data.FarmInvestment.Repeat,
        status=AutonomyPolicy.InvestmentState(Data.FarmInvestment.Phase,Data.FarmInvestment.Error),
        reason=Data.FarmInvestment.Error,
        actual_spent_today=NativePurchaseSpent(),available_cash=SeedAllowance(),pending_purchase_cash=PendingPurchaseCash(),
        daily_ceiling=Data.FarmInvestment.BudgetPerDay<0?(int?)null:Data.FarmInvestment.BudgetPerDay,
        keep_gold=Data.FarmInvestment.KeepGold,
        manual_care_ceiling=Data.FarmInvestment.ManualWaterLimit<0?(int?)null:Data.FarmInvestment.ManualWaterLimit,
        tasks=Data.FarmInvestment.Tasks,
        note="自动例行关闭不影响直接采购、制作或种植工具；空的额度表示无额外上限。未来收入不算现金。已有作物照料负担用于模型决策，不是固定株数限制。"
    };
}
