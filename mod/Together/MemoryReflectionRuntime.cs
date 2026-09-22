using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private Task<ModelReply>? reflectionRequest;
    private string reflectionEpoch="";
    private int reflectionDay=-1,reflectionRequestedDay=-1;
    private HashSet<string> reflectionEvidence=new();
    private void TickMemoryReflection() {
        if(reflectionRequest is {IsCompleted:true}) {
            var task=reflectionRequest;reflectionRequest=null;
            try {
                var reply=task.GetAwaiter().GetResult();Data.Tokens+=reply.Tokens;RecordUsage();RecordAgentUsage(reply);
                if(reflectionEpoch!=agentSaveEpoch||reflectionDay>=Game1.Date.TotalDays)return;
                var summary=MemoryReflection.Parse(reply.Json,reflectionDay,reflectionEvidence);
                foreach(string id in summary.Evidence)memoryArchive?.Evidence(id);
                Data.Autoplay.Memory.Reflections.RemoveAll(r=>r.Day==summary.Day);
                Data.Autoplay.Memory.Reflections.Add(summary);
                if(Data.Autoplay.Memory.Reflections.Count>28)Data.Autoplay.Memory.Reflections.RemoveAt(0);
                memoryArchive?.Remember(summary.Day,"autoplay","reflection","reflection:"+summary.Day,summary);
                Data.Autoplay.Record("memory_reflection_verified_sources",AgentJson.Encode(summary));
            }catch(Exception e){Data.Autoplay.Record("memory_reflection_rejected",AgentJson.Encode(new{error=e.Message,fallback="deterministic_diary"}));}
        }
        if(!Settings.EnableMemoryReflections||!AutoplayRunning||reflectionRequest!=null||Game1.Date.TotalDays==reflectionRequestedDay)return;
        reflectionRequestedDay=Game1.Date.TotalDays;int day=Game1.Date.TotalDays-1;
        var evidence=Data.Autoplay.Memory.Index.Where(d=>d.Day==day&&d.Kind=="action_result").GroupBy(d=>(d.Tool,d.Status)).SelectMany(g=>new[]{g.First(),g.Last()}).DistinctBy(d=>d.Evidence).Take(24).ToArray();
        if(evidence.Length==0||Data.Autoplay.Memory.Reflections.Any(r=>r.Day==day)||Data.Calls>=Math.Clamp(Settings.AutoplayMaxCallsPerDay,1,2000))return;
        EnsureBudget();reflectionEvidence=evidence.Select(e=>e.Evidence).ToHashSet();reflectionDay=day;reflectionEpoch=agentSaveEpoch;
        string context=AgentJson.Encode(new{day,full_day_native_diary=Data.Autoplay.Memory.Diary.Rows.Where(r=>r.Day==day).Select(DiaryDisplay),evidence,goal=Data.Autoplay.Goal,instruction="仅总结给定真实回执，区分完成、失败和待验证；失败结论带条件，不推断当前库存。"});
        string file=Path.IsPathRooted(Settings.ApiKeyFile)?Settings.ApiKeyFile:Path.Combine(Helper.DirectoryPath,Settings.ApiKeyFile),model=Settings.Model;
        string? trace=Settings.RecordModelTrace?Path.Combine(Helper.DirectoryPath,"logs",Game1.uniqueIDForThisGame.ToString(),agentSaveEpoch,"model-"+Data.Autoplay.RunId+".jsonl"):null;
        Data.Calls++;RecordUsage();
        reflectionRequest=Task.Run(()=>AutoplayModel.Ask(file,model,context,CancellationToken.None,trace,Settings.AutoplayInputTokenBudget,purpose:"memory-summary"));
    }
}
