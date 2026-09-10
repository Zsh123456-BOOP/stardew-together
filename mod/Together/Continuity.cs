using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private void RecordInterrupted(Companion p,Job job,JsonElement receipt) {
        if(job.Index>=job.Steps.Count || !receipt.TryGetProperty("command_id",out var command))return;
        string id=command.GetString()??"";if(id=="" || job.PartialReceipts.Contains(id))return;
        if(!receipt.TryGetProperty("evidence",out var evidence) || !evidence.TryGetProperty("activity_seconds",out var seconds))return;
        string skill=job.Steps[job.Index].skill;if(skill is not ("fish" or "guard" or "rest"))return;
        double elapsed=seconds.GetDouble();if(elapsed<=0)return;
        int total=job.RemainingSeconds??(skill=="rest"?20:30);
        job.RemainingSeconds=Math.Max(1,total-(int)Math.Floor(elapsed));job.PartialReceipts.Add(id);
        string detail=$"{Decision.Labels[skill]}进行了 {(int)elapsed} 秒，安排还未结束";
        if(skill=="fish" && evidence.TryGetProperty("caught_items",out var items))detail+=$"，已经收到 {items.GetArrayLength()} 件渔获（可能含杂物）";
        p.Life.Experiences.Add(new(){Id=id+":partial",Day=Game1.Date.TotalDays,Minute=Minute,Summary=detail,Skill=skill});
    }
    private readonly Dictionary<string,double> frameStages=new();
    private readonly Dictionary<string,Queue<double>> measuredStages=new();
    private long performanceDayFrames;private double performanceDayTotal,performanceDayMax;
    private void ResetDayPerformance(){performanceDayFrames=0;performanceDayTotal=performanceDayMax=0;measuredFrames.Clear();measuredStages.Clear();}
    private static object TimingSummary(IEnumerable<double> samples) {
        var v=samples.OrderBy(x=>x).ToArray();return new{count=v.Length,mean_ms=v.Length==0?0:v.Average(),p95_ms=v.Length==0?0:v[(int)((v.Length-1)*.95)],p99_ms=v.Length==0?0:v[(int)((v.Length-1)*.99)],max_ms=v.Length==0?0:v[^1]};
    }
    private void FrameStage(string name,ref long start) {
        long now=System.Diagnostics.Stopwatch.GetTimestamp();double ms=(now-start)*1000.0/System.Diagnostics.Stopwatch.Frequency;start=now;
        if(!measuredStages.TryGetValue(name,out var samples))measuredStages[name]=samples=new();samples.Enqueue(ms);if(samples.Count>36000)samples.Dequeue();
        if(ms>=8)frameStages[name]=ms;
    }
    private void ProfileFrame(double milliseconds) {
        performanceDayFrames++;performanceDayTotal+=milliseconds;performanceDayMax=Math.Max(performanceDayMax,milliseconds);
        measuredFrames.Enqueue(milliseconds);if(measuredFrames.Count>36000)measuredFrames.Dequeue();
        if(milliseconds>=50&&DateTime.UtcNow>=nextSlowFrameLog) {
            nextSlowFrameLog=DateTime.UtcNow.AddSeconds(10);
            WriteBusinessLog("slow_frame",AgentJson.Encode(new{milliseconds,location=Game1.currentLocation?.NameOrUniqueName,action=playerExecutor.Current?.skill,phase=playerExecutor.Current?.phase,model_pending=agentPending!=null,investment=Data.FarmInvestment.Phase,stages=frameStages}));
        }
    }
    private DateTime nextSlowFrameLog;
    private readonly Queue<double> measuredFrames=new();
    private object Performance() {
        var values=measuredFrames.OrderBy(v=>v).ToArray();
        return new{day_samples=performanceDayFrames,day_mean_ms=performanceDayFrames==0?0:performanceDayTotal/performanceDayFrames,day_max_ms=performanceDayMax,percentile_scope="last up to 36000 updates of current day",samples=values.Length,mean_ms=values.Length==0?0:values.Average(),p95_ms=values.Length==0?0:values[(int)((values.Length-1)*.95)],max_ms=values.Length==0?0:values[^1],p99_ms=values.Length==0?0:values[(int)((values.Length-1)*.99)],frames_over_16ms=values.Count(v=>v>16.667),
            stage_timings=measuredStages.ToDictionary(k=>k.Key,k=>TimingSummary(k.Value)),
            scope="Together main-thread Update; adapter movement separately measured in bridge state; excludes vanilla baseline"};
    }
}
