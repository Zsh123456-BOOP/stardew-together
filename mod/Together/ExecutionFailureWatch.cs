namespace Together;
// Mirrors the audit counter at the point of execution, before another invocation.
public sealed class ExecutionFailureWatch {
    private static string RootCause(string reason) {
        const string prefix="known_failure_conditions_unchanged:";
        if(reason.StartsWith(prefix)){reason=reason[prefix.Length..];int end=reason.IndexOf(":evidence=",StringComparison.Ordinal);if(end>=0)reason=reason[..end];}
        return reason;
    }
    public static bool PurchaseInput(string reason)=>CorrectableInput(reason)&&RootCause(reason).StartsWith("seed_");
    public static bool CorrectableInput(string cause){string reason=RootCause(cause);return reason.StartsWith("seed_selection_")||reason.StartsWith("seed_reselection_")||reason.StartsWith("seed_quote_")||reason.StartsWith("seed_purchase_")||reason.StartsWith("material_quantity_conflict_")||reason.StartsWith("farm_plan_")||reason.StartsWith("decision_review:")||reason.StartsWith("parameter_")||reason.StartsWith("invalid_")||reason.StartsWith("plan_query_")||reason.StartsWith("active_plan_requires_");}
    public static string Repair(string cause){string reason=RootCause(cause);return reason.StartsWith("farm_plan_requires_owned")?"farm.plan只处理已持有未种种子；未购买时根据已有quote_token调用farm.select_seeds，采购与播种由同一流程执行。":reason.StartsWith("decision_review:")?"当前动作参数未通过检查；仅修正此动作。收工使用reason和defer候选ID数组；不需重复证明时间和体力。":reason.StartsWith("seed_selection_unaffordable")?"当前现金买不起所选种子；items=[]明确不买，随后处理现有种子或其他工作，不再为相同报价往返。":reason.StartsWith("seed_quote_")?"报价过期或已使用：从当前商店重新shop.read取得quote_token，再选种；不猜token。":reason.StartsWith("seed_selection_not_pending")?"原采购/播种链仍在执行，等待原任务结果；不要重复选种。":reason.StartsWith("seed_selection_not_feasible")?"items中的商品ID、数量和生长窗口不符合当前报价；按shop.read中的候选修改，或items=[]明确不买。":reason.StartsWith("seed_purchase_")?"核对purchase_status剩余量；确需追加可重新选种并在reason说明用途，直接buy/procure超额须additional_reason。":reason.StartsWith("material_quantity_conflict_")?"数量二选一：count表示新增份数；stock_target表示最终库存总数。删除另一个参数后提交。":"选种仅填quote_token、items、reason；不采购用items=[]，追加数量和用途统一写reason，不需第二个理由字段。";}
    private string run="";
    private readonly HashSet<string> seen=new();
    private readonly Dictionary<(string Actor,string Root,long Version),int> counts=new();
    public int Observe(string runId,string attempt,string actor,string root,long version) {
        if(run!=runId){run=runId;seen.Clear();counts.Clear();}
        if(string.IsNullOrEmpty(root)||string.IsNullOrEmpty(attempt)||!seen.Add(attempt))return 0;
        root=RootCause(root);
        if(root.StartsWith("capacity:")||root.StartsWith("capacity_relief:"))root="capacity_all_candidates_infeasible";
        var key=(actor,root,version);return counts[key]=counts.GetValueOrDefault(key)+1;
    }
}
