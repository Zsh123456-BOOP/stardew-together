using Together;
using System.Text.Json;
internal static class BusinessChecks {
    internal static void Run(Action<bool,string> check) {
        check(BusinessMath.Spendable(500,200,1000,0)==300,"working capital is protected even when daily allowance is large");
        check(BusinessMath.Spendable(10000,200,1000,800)==200,"seed and capital reservations share remaining daily allowance");
        check(BusinessMath.Spendable(100,200,1000,0)==0,"insufficient actual cash never uses predicted shipping receipts");
        check(BusinessMath.Settle(1000,800,40)==240,"unused procurement allowance becomes available for reinvestment after verified receipts");
        var cheap=new BusinessOption("small","machine","a",100,100,100,0,1,"",Array.Empty<string>());
        var large=new BusinessOption("large","building","b",1000,3000,200,0,1,"",Array.Empty<string>());
        var unavailable=cheap with{Id="locked",Gaps=new[]{"missing_native_unlock"},DailyMargin=9999};
        var choices=BusinessMath.Rank(new[]{large,unavailable,cheap},5000,500,10000,0).ToArray();
        check(choices.Length==2&&choices[0].Id=="small","compare capital plus material opportunity cost and exclude unavailable choices");
        check(!BusinessMath.Rank(new[]{cheap with{Existing=1}},500,0,500,0).Any(),"already sufficient capacity does not trigger duplicate investment");
        check(BusinessMath.ProcessingReserve(100,5,2)==10,"raw stock reserves are bounded by actual processing capacity");
        var saved=new FarmBusiness{Enabled=true,ChildGoal="existing-material-goal",ReservedToday=500,Tasks=new(){"paid-order"},Day=3};
        var restored=JsonSerializer.Deserialize<FarmBusiness>(JsonSerializer.Serialize(saved))!;
        check(restored.ReservedToday==500&&restored.Tasks.Single()=="paid-order"&&restored.ChildGoal==saved.ChildGoal,"reload preserves committed budget and pending work for reconciliation");
        check(AgentSchedule.Queueable("player.procure")&&AgentSchedule.Queueable("player.acquire_animal"),"high-level procurement participates in normal persistent queue");
    }
}
