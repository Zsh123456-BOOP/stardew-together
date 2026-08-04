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
    private void ProfileFrame(double milliseconds) {
        measuredFrames.Enqueue(milliseconds);if(measuredFrames.Count>36000)measuredFrames.Dequeue();
    }
    private readonly Queue<double> measuredFrames=new();
    private object Performance() {
        var values=measuredFrames.OrderBy(v=>v).ToArray();
        return new{samples=values.Length,mean_ms=values.Length==0?0:values.Average(),p95_ms=values.Length==0?0:values[(int)((values.Length-1)*.95)],max_ms=values.Length==0?0:values[^1],
            scope="Together main-thread Update; adapter movement separately measured in bridge state; excludes vanilla baseline"};
    }
}
