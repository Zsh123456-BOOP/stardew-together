using Together;
public static class SurvivalChecks {
    public static void Run(Action<bool,string> check) {
        var s=new SurvivalState();s.NewDay(2);
        check(!s.ModelFailed()&&!s.ModelFailed()&&s.ModelFailed(),"three unavailable model replies trigger fallback");
        s.Mode="sleep";check(!s.NewDay(2)&&s.Mode=="sleep","same-day reads do not clear committed sleep");
        check(s.NewDay(3)&&s.Mode=="model"&&s.ModelFailures==0,"native new day retries model");
        check(!SurvivalState.NightGuard(2150,false)&&SurvivalState.NightGuard(2200,false)&&!SurvivalState.NightGuard(2200,true),"night guard respects effective plans");
        check(SurvivalState.Fatal("day_transition_not_verified")&&SurvivalState.Fatal("inventory_conservation_failed")&&!SurvivalState.Fatal("path_stalled"),"world evidence failures remain fatal; navigation is recoverable");
        check(RecoveryPolicy.CanWait("known_failure_conditions_unchanged:target_abandoned_today"),"refusing an already abandoned target is not a new failed native attempt");
    }
}
