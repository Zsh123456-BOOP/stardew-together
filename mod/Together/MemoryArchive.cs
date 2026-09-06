using System.Text.Json;

namespace Together;
public sealed class MemoryCheckpoint {
    public Dictionary<string,int> Cursors {get;set;}=new();
    public List<MemoryDaySummary> Days {get;set;}=new();
    public string LastError {get;set;}="";
    public List<ArchivedMemory> Pending {get;set;}=new();
    public Dictionary<string,int> ActorGenerations {get;set;}=new();
    public Dictionary<string,string> SourceHashes {get;set;}=new();
    public List<MemorySeasonSummary> Seasons {get;set;}=new();
}
public sealed class MemorySeasonSummary {
    public int SeasonIndex {get;set;}
    public int Successful {get;set;}
    public int Failed {get;set;}
    public int CoveredUntilDay {get;set;}
    public int SchemaVersion {get;set;}=1;
    public List<string> Evidence {get;set;}=new();
}
public sealed class MemoryDaySummary {
    public int Day {get;set;}
    public int Successful {get;set;}
    public int Failed {get;set;}
    public Dictionary<string,int> RepeatedErrors {get;set;}=new();
    public List<string> Evidence {get;set;}=new();
}
public sealed record ArchivedMemory(string Id,int Sequence,int Day,string Actor,string Kind,string Text,int ActorGeneration=0);

// Files are append-only; the cursor is checkpointed WITH the native save. Reloading
// an earlier save cannot expose future events appended after that save's cursor.
public sealed class MemoryArchive {
    private readonly string root,bucket;
    private readonly MemoryCheckpoint checkpoint;
    private readonly bool deferredWrites;
    private Task<(List<ArchivedMemory> Written,string Error)>? writer;

    public MemoryArchive(string root,string bucket,MemoryCheckpoint checkpoint,bool deferredWrites=false) {
        this.root=root;this.bucket=bucket;this.checkpoint=checkpoint;this.deferredWrites=deferredWrites;
        if(bucket.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))throw new ArgumentException("invalid_memory_bucket");
    }
    public void Append(int day,string actor,string kind,string text) {
        string key=bucket+"-"+day;int sequence=checkpoint.Cursors.GetValueOrDefault(key)+1;
        var entry=new ArchivedMemory(key+":"+sequence,sequence,day,actor,kind,text,checkpoint.ActorGenerations.GetValueOrDefault(actor));
        checkpoint.Cursors[key]=sequence;checkpoint.Pending.Add(entry);
        if(kind=="action_result")Summarize(entry);
        if(deferredWrites&&checkpoint.Pending.Count<128)QueueFlush();else Flush();
    }
    public void Remember(int day,string actor,string kind,string source,object value) {
        string text=AgentJson.Encode(new{source_id=source,value}),key=actor+":"+checkpoint.ActorGenerations.GetValueOrDefault(actor)+":"+source;
        string hash=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(text)));
        if(checkpoint.SourceHashes.GetValueOrDefault(key)==hash)return;
        Append(day,actor,kind,text);checkpoint.SourceHashes[key]=hash;
    }
    public void ForgetActor(string actor) {
        checkpoint.ActorGenerations[actor]=checkpoint.ActorGenerations.GetValueOrDefault(actor)+1;
        foreach(var key in checkpoint.SourceHashes.Keys.Where(k=>k.StartsWith(actor+":",StringComparison.Ordinal)).ToArray())checkpoint.SourceHashes.Remove(key);
    }
    private (List<ArchivedMemory> Written,string Error) WriteBatch(ArchivedMemory[] batch) {
        var written=new List<ArchivedMemory>();
        try {
            Directory.CreateDirectory(root);
            foreach(var entry in batch) {
                string target=Path.Combine(root,entry.Id.Replace(':','_')+".json"),temp=target+".tmp";
                File.WriteAllText(temp,AgentJson.Encode(entry));File.Move(temp,target,true);written.Add(entry);
            }
            return (written,"");
        }catch(IOException e){return (written,"memory_archive_write_failed:"+e.GetType().Name);}
        catch(UnauthorizedAccessException){return (written,"memory_archive_permission_denied");}
    }
    private void ApplyWrite((List<ArchivedMemory> Written,string Error) result) {
        foreach(var entry in result.Written)checkpoint.Pending.Remove(entry);
        checkpoint.LastError=result.Error;
    }
    public void QueueFlush() {
        if(writer is {IsCompleted:true}){ApplyWrite(writer.GetAwaiter().GetResult());writer=null;}
        if(writer!=null||checkpoint.Pending.Count==0)return;
        var batch=checkpoint.Pending.ToArray(); // Only immutable entries cross threads.
        writer=Task.Run(()=>WriteBatch(batch));
    }
    public void Flush() {
        if(writer!=null){ApplyWrite(writer.GetAwaiter().GetResult());writer=null;}
        if(checkpoint.Pending.Count>0)ApplyWrite(WriteBatch(checkpoint.Pending.ToArray()));
    }
    private void Summarize(ArchivedMemory entry) {
        var day=checkpoint.Days.FirstOrDefault(x=>x.Day==entry.Day);
        if(day==null){day=new(){Day=entry.Day};checkpoint.Days.Add(day);}
        try {
            using var doc=JsonDocument.Parse(entry.Text);var r=doc.RootElement;
            string status=r.TryGetProperty("status",out var s)?s.GetString()??"":"";
            if(status=="succeeded")day.Successful++;else if(status=="failed")day.Failed++;
            var season=checkpoint.Seasons.FirstOrDefault(s=>s.SeasonIndex==entry.Day/28);
            if(season==null){season=new(){SeasonIndex=entry.Day/28};checkpoint.Seasons.Add(season);}
            if(status=="succeeded")season.Successful++;else if(status=="failed")season.Failed++;
            season.CoveredUntilDay=Math.Max(season.CoveredUntilDay,entry.Day);
            season.Evidence.Add(entry.Id);
            if(r.TryGetProperty("error",out var e)&&e.ValueKind==JsonValueKind.String){string code=e.GetString()!;day.RepeatedErrors[code]=day.RepeatedErrors.GetValueOrDefault(code)+1;}
            day.Evidence.Add(entry.Id);if(day.Evidence.Count>12)day.Evidence.RemoveAt(0);
        }catch(JsonException){checkpoint.LastError="memory_event_invalid_json";}
    }
    public object Read(string query,string actor,int limit,int offset=0) {
        limit=Math.Clamp(limit,1,20);if(offset<0||offset>1000)throw new InvalidOperationException("invalid_memory_offset");
        var matched=new List<ArchivedMemory>();bool incomplete=false;int scanned=0;var terms=MemoryRecall.SearchTerms(query);
        foreach(var pair in checkpoint.Cursors.Reverse()) {
            if(pair.Key.Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))continue;
            for(int sequence=pair.Value;sequence>0;sequence--) {
                if(++scanned>5000){incomplete=true;break;}
                var entry=Find(pair.Key,sequence);if(entry==null){incomplete=true;continue;}
                if(entry.Kind=="decision"&&entry.Id!=query)continue; // Unverified model plans are not factual recall.
                if(entry.ActorGeneration!=checkpoint.ActorGenerations.GetValueOrDefault(entry.Actor))continue;
                if(entry==null||entry.Sequence>pair.Value||actor.Length>0&&entry.Actor!=actor)continue;
                if(query.Length>0&&!terms.Any(t=>entry.Text.Contains(t,StringComparison.OrdinalIgnoreCase)||entry.Kind.Contains(t,StringComparison.OrdinalIgnoreCase))&&entry.Id!=query)continue;
                matched.Add(entry);if(matched.Count>offset+limit)break;
            }
            if(matched.Count>offset+limit||scanned>5000)break;
        }
        return new{entries=matched.Skip(offset).Take(limit).Select(e=>new{e.Id,e.Day,e.Actor,e.Kind,text=e.Text.Length>4000?e.Text[..4000]:e.Text,text_truncated=e.Text.Length>4000}),offset,next_offset=matched.Count>offset+limit?(int?)(offset+limit):null,scan_incomplete=incomplete,last_error=checkpoint.LastError,note="只读取当前存档 checkpoint 已记录的事件；历史错误不是永久阻碍，执行前重验条件。"};
    }
    private ArchivedMemory? Find(string key,int sequence) {
        string id=key+":"+sequence;
        var pending=checkpoint.Pending.FirstOrDefault(e=>e.Id==id);if(pending!=null)return pending;
        string file=Path.Combine(root,key+"_"+sequence+".json");
        try{return File.Exists(file)?JsonSerializer.Deserialize<ArchivedMemory>(File.ReadAllText(file)):null;}
        catch(IOException){return null;}catch(JsonException){return null;}
    }
    public object Evidence(string id,int offset=0) {
        var parts=id.Split(':');
        if(parts.Length!=2||!int.TryParse(parts[1],out int sequence)||sequence<1||!checkpoint.Cursors.TryGetValue(parts[0],out int cursor)||sequence>cursor||parts[0].Any(c=>!char.IsLetterOrDigit(c)&&c!='-'))throw new InvalidOperationException("memory_evidence_outside_saved_timeline");
        var entry=Find(parts[0],sequence)??throw new InvalidOperationException("memory_evidence_missing");
        if(entry.ActorGeneration!=checkpoint.ActorGenerations.GetValueOrDefault(entry.Actor))throw new InvalidOperationException("memory_evidence_forgotten");
        if(offset<0||offset>entry.Text.Length)throw new InvalidOperationException("invalid_evidence_offset");
        int length=Math.Min(4000,entry.Text.Length-offset);
        return new{entry.Id,entry.Actor,entry.Kind,entry.Day,text=entry.Text.Substring(offset,length),offset,total_characters=entry.Text.Length,next_offset=offset+length<entry.Text.Length?(int?)(offset+length):null};
    }
}
