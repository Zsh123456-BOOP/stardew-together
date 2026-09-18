namespace Together;
public sealed partial class ModEntry {
    private readonly ExecutionFailureWatch executionFailures=new();
    private void ObserveExecutionFailure(string attempt,string actor,string? cause,string source) {
        if(cause==null||!AutoplayRunning)return;
        int count=executionFailures.Observe(Data.Autoplay.RunId,attempt,actor,cause,Data.Autoplay.Capacity.Version);
        Data.Autoplay.Record("execution_failure_count",AgentJson.Encode(new{attempt_id=attempt,actor,root_cause=cause,capacity_version=Data.Autoplay.Capacity.Version,count,source}));
        if(count<3)return;
        Data.Autoplay.Record("execution_failure_stop",AgentJson.Encode(new{attempt_id=attempt,actor,root_cause=cause,count,source,note="第三次失败回执已保留；在发出下一调用之前同步暂停"}));
        PauseAutoplay("same_root_three_distinct_attempts:"+actor+":"+cause);
    }
}
