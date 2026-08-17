using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private MemoryArchive? memoryArchive;
    private void AttachMemoryArchive() {
        string root=Path.Combine(Helper.DirectoryPath,"memory-archive",Game1.uniqueIDForThisGame+"-"+Game1.player.UniqueMultiplayerID);
        memoryArchive=new(root,agentSaveEpoch,Data.Autoplay.Memory);
        memoryArchive.Flush();
        Data.Autoplay.Archive=(kind,text)=>memoryArchive.Append(Game1.Date.TotalDays,"autoplay",kind,text);
    }
    internal object ReadAgentMemory(JsonElement args)=>memoryArchive?.Read(AgentToolRegistry.Text(args,"query"),AgentToolRegistry.Text(args,"actor"),AgentToolRegistry.Number(args,"limit",8),AgentToolRegistry.Number(args,"offset",0))??new{error="memory_archive_unavailable"};
    internal object ReadMemoryEvidence(JsonElement args)=>memoryArchive?.Evidence(AgentToolRegistry.Text(args,"id"),AgentToolRegistry.Number(args,"offset",0))??new{error="memory_archive_unavailable"};
    private object AgentMemoryContext()=>new {
        days=Data.Autoplay.Memory.Days.TakeLast(4),archive_error=Data.Autoplay.Memory.LastError,
        unfinished=Data.SharedGoals.Where(g=>g.Status=="active").Select(g=>new{g.Id,g.Title,g.Summary}).Take(8),
        promises=Data.People.SelectMany(kv=>kv.Value.Job is {Status:"active" or "waiting" or "paused"} j?new[]{new{actor=kv.Key,j.Id,j.Title,j.Status,j.Index,j.DoneInStep}}:Array.Empty<object>()).Take(8),
        note="事实以本轮原生状态为准；memory.search 可检索归档回执。当前承诺保存在结构化计划，未因摘要窗口删除。"
    };
}
