using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private MemoryArchive? memoryArchive;
    private void AttachMemoryArchive() {
        string root=Path.Combine(Helper.DirectoryPath,"memory-archive",Game1.uniqueIDForThisGame+"-"+Game1.player.UniqueMultiplayerID);
        memoryArchive=new(root,agentSaveEpoch,Data.Autoplay.Memory,deferredWrites:true);
        memoryArchive.Flush();
        Data.Autoplay.Archive=(kind,text)=>{memoryArchive.Append(Game1.Date.TotalDays,"autoplay",kind,text);WriteBusinessLog(kind,text);};
        foreach(var person in Data.People)BindCompanionArchive(person.Key,person.Value);
    }
    private void BindCompanionArchive(string name,Companion person) {
        person.Life.ArchiveRemoved=entries=>{foreach(var e in entries)memoryArchive?.Remember(e.Day,name,"experience",e.Id,e);};
        person.Social.ArchiveRemoved=entries=>{foreach(var e in entries)memoryArchive?.Remember(e.Day,name,"diary","diary:"+e.Day,e);};
    }
    private void ArchiveCompanionCheckpoint() {
        if(memoryArchive==null)return;
        foreach(var pair in Data.People) {
            var p=pair.Value;BindCompanionArchive(pair.Key,p);
            p.Life.ArchiveRemoved?.Invoke(p.Life.Experiences);p.Social.ArchiveRemoved?.Invoke(p.Social.Diary);
            memoryArchive.Remember(Game1.Date.TotalDays,pair.Key,"preferences","current-preferences",p.Social.Preferences);
        }
        memoryArchive.Remember(Game1.Date.TotalDays,"autoplay","native_checkpoint","day:"+Game1.Date.TotalDays,new{money=Game1.player.Money,earned=Game1.player.totalMoneyEarned,goals=Data.SharedGoals,inventory=AgentToolRegistry.Inventory(),achievements=Game1.player.achievements.ToArray()});
        memoryArchive.Remember(Game1.Date.TotalDays,"autoplay","daily_activity","diary:"+Game1.Date.TotalDays,DiaryContext());
        memoryArchive.Flush();
    }
    internal object ReadAgentMemory(JsonElement args)=>memoryArchive?.Read(AgentToolRegistry.Text(args,"query"),AgentToolRegistry.Text(args,"actor"),AgentToolRegistry.Number(args,"limit",8),AgentToolRegistry.Number(args,"offset",0))??new{error="memory_archive_unavailable"};
    internal object ReadMemoryEvidence(JsonElement args)=>memoryArchive?.Evidence(AgentToolRegistry.Text(args,"id"),AgentToolRegistry.Number(args,"offset",0))??new{error="memory_archive_unavailable"};
    private void RecordNativeDiary(PlayerAction action) {
        var raw=JsonSerializer.SerializeToElement(action,AgentJson.Options);
        Data.Autoplay.Memory.Diary.Native(raw,Game1.Date.TotalDays,Game1.timeOfDay);
        Data.Autoplay.Record("native_diary_receipt",AgentJson.Encode(raw));
    }
    private object DiaryContext()=>new {
        today_day=Game1.Date.TotalDays,previous_day=Game1.Date.TotalDays-1,today_maintenance=WateringObservation("Farm"),
        today=Data.Autoplay.Memory.Diary.Rows.Where(r=>r.Day==Game1.Date.TotalDays).Select(DiaryDisplay),
        previous=Data.Autoplay.Memory.Diary.Rows.Where(r=>r.Day==Game1.Date.TotalDays-1&&r.Kind is not ("行动期间入包" or "行动期间取用" or "现金变化")).Select(DiaryDisplay),
        active_tasks=semanticJobs.Values.Where(j=>j.status=="running").Select(j=>new{j.command_id,j.goal,j.location,j.requested,j.completed,j.gained,remaining=Math.Max(0,j.requested-Math.Max(j.completed,j.gained)),j.phase}),
        note="今日干地是今日维护需求，不能推翻昨日已浇水记录。只记录真实结果，不是当前库存。种下/浇水为地块数；收获点不是产物数量；行动期间入包/取用含补给，不能与购买/收获重复加总。出货尚未到账。当前库存以实时读取为准。"
    };
    private object DiaryDisplay(DiaryRow r) {
        string item=r.Item.Split('|')[0],name=item;
        if(item.StartsWith("("))try{name=ItemRegistry.GetDataOrErrorItem(item).DisplayName;}catch{}
        return new{r.Day,first=r.First,last=r.Last,activity=r.Kind,item=r.Item,name,location=r.Location,count=r.Count,cost=r.Cost,batches=r.Batches,evidence=r.Evidence,supporting_evidence=r.SupportingEvidence};
    }
    private object AgentMemoryContext()=>new {
        daily_activity=DiaryContext(),
        reflections=Data.Autoplay.Memory.Reflections.TakeLast(2),
        relevant=memoryArchive?.Recall(Data.Autoplay.Plan,Game1.Date.TotalDays),
        days=Data.Autoplay.Memory.Days.TakeLast(4),seasons=Data.Autoplay.Memory.Seasons.TakeLast(2).Select(s=>new{s.SeasonIndex,s.Successful,s.Failed,s.CoveredUntilDay,evidence_count=s.Evidence.Count,s.SchemaVersion}),archive_error=Data.Autoplay.Memory.LastError,pending_archive=Data.Autoplay.Memory.Pending.Count,
        service_constraints=Data.Autoplay.Operations.Constraints.Where(e=>e.Day==Game1.Date.TotalDays).ToArray(),
        recent_failure_rules=ActiveFailureRules().OrderByDescending(e=>e.UntilChanged).ThenByDescending(e=>e.Attempts).Take(12).Select(e=>new{e.Tool,e.Actor,e.Reason,e.Day,e.RetryAfterMinute,e.Attempts,e.Suppressed,e.TaskEvidence,e.UntilChanged,e.Location,subject_args=e.Arguments,condition_version=e.Conditions.Length>0?FailureKnowledge.Hash(e.Conditions)[..12]:"",family=FailureKnowledge.Family(e.Reason),invalidates=e.UntilChanged?"相关条件变化后复查；单纯换日、改数量/措辞不解除。目标看区域对象与工具；容量看真实背包与仓储。":"空间/瞬时故障按实际相关状态复查，有界冷却。"}),
        unfinished=Data.SharedGoals.Where(g=>g.Status=="active").Select(g=>new{g.Id,g.Title,g.Summary}).Take(8),
        promises=(SinglePlayerMode?Enumerable.Empty<KeyValuePair<string,Companion>>():Data.People).SelectMany(kv=>kv.Value.Job is {Status:"active" or "waiting" or "paused"} j?new[]{new{actor=kv.Key,j.Id,j.Title,j.Status,j.Index,j.DoneInStep}}:Array.Empty<object>()).Take(8),
        note="事实以本轮原生状态为准；memory.search 可检索归档回执。当前承诺保存在结构化计划，未因摘要窗口删除。"
    };
}
