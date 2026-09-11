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
        memoryArchive.Flush();
    }
    internal object ReadAgentMemory(JsonElement args)=>memoryArchive?.Read(AgentToolRegistry.Text(args,"query"),AgentToolRegistry.Text(args,"actor"),AgentToolRegistry.Number(args,"limit",8),AgentToolRegistry.Number(args,"offset",0))??new{error="memory_archive_unavailable"};
    internal object ReadMemoryEvidence(JsonElement args)=>memoryArchive?.Evidence(AgentToolRegistry.Text(args,"id"),AgentToolRegistry.Number(args,"offset",0))??new{error="memory_archive_unavailable"};
    private object AgentMemoryContext()=>new {
        days=Data.Autoplay.Memory.Days.TakeLast(4),seasons=Data.Autoplay.Memory.Seasons.TakeLast(2).Select(s=>new{s.SeasonIndex,s.Successful,s.Failed,s.CoveredUntilDay,evidence_count=s.Evidence.Count,s.SchemaVersion}),archive_error=Data.Autoplay.Memory.LastError,pending_archive=Data.Autoplay.Memory.Pending.Count,
        service_constraints=Data.Autoplay.Operations.Constraints.Where(e=>e.Day==Game1.Date.TotalDays).ToArray(),
        recent_failure_rules=Data.Autoplay.Failures.Entries.Where(e=>e.Day==Game1.Date.TotalDays).OrderByDescending(e=>e.UntilChanged).ThenByDescending(e=>e.Attempts).Take(12).Select(e=>new{e.Tool,e.Actor,e.Reason,e.Day,e.RetryAfterMinute,e.Attempts,e.TaskEvidence,e.UntilChanged,family=FailureKnowledge.Family(e.Reason),invalidates=e.UntilChanged?"仅相关前置变化或次日解除；改数量/措辞、走动或无关体力变化不解除。材料看批准用途和库存；目标看目标区域/能力；容量看货袋与仓储。":"空间/瞬时故障按实际相关状态复查，有界冷却。"}),
        unfinished=Data.SharedGoals.Where(g=>g.Status=="active").Select(g=>new{g.Id,g.Title,g.Summary}).Take(8),
        promises=(SinglePlayerMode?Enumerable.Empty<KeyValuePair<string,Companion>>():Data.People).SelectMany(kv=>kv.Value.Job is {Status:"active" or "waiting" or "paused"} j?new[]{new{actor=kv.Key,j.Id,j.Title,j.Status,j.Index,j.DoneInStep}}:Array.Empty<object>()).Take(8),
        note="事实以本轮原生状态为准；memory.search 可检索归档回执。当前承诺保存在结构化计划，未因摘要窗口删除。"
    };
}
