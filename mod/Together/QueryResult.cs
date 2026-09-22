using System.Text.Json;
namespace Together;

// Saved receipts survive pauses and reloads. Delivery is not proof of comprehension.
public sealed class QueryResult {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Call {get;set;}="";
    public string Tool {get;set;}="";
    public int Day {get;set;}
    public int Time {get;set;}
    public string Kind {get;set;}="query";
    public JsonElement Args {get;set;}
    public JsonElement Result {get;set;}
    public bool Delivered {get;set;}
    public object Observation()=>new{result_id=Id,call_id=Call,tool=Tool,kind=Kind,observed_day=Day,observed_time=Time,args=Args.ValueKind==JsonValueKind.Undefined?JsonSerializer.SerializeToElement(new{}):Args,result=Tool=="tools.lookup"?LookupObservation():ObservationContract.Observation(Result,Kind)};
    private JsonElement LookupObservation() {
        if(!Result.TryGetProperty("definitions",out var definitions))return ObservationContract.Observation(Result,Kind);
        var view=new Dictionary<string,object?>{{"equipped_names",definitions.EnumerateObject().Select(p=>p.Name).ToArray()},{"definitions_in","system_tools"}};
        foreach(string field in new[]{"unknown","fallback","index"})if(Result.TryGetProperty(field,out var value))view[field]=value.Clone();
        return JsonSerializer.SerializeToElement(view);
    }
    public static object[] Pending(IEnumerable<QueryResult> queries,int budget=4200) {
        var rows=new List<object>();int size=2;
        foreach(var q in queries.Where(q=>!q.Delivered).OrderBy(q=>q.Kind=="outcome"?0:q.Kind=="quote"?1:2).ThenBy(q=>q.Day).ThenBy(q=>q.Time)) {
            var row=q.Observation();int cost=ContextBudget.Estimate(AgentJson.Encode(row));
            if(size+cost>budget)continue;
            rows.Add(row);size+=cost+1;
        }
        return rows.ToArray();
    }
    public static bool NeedsDecisionWhileBusy(IEnumerable<QueryResult> queries)=>queries.Any(q=>!q.Delivered&&(q.Kind=="quote"||q.Kind=="outcome"&&q.Result.ValueKind==JsonValueKind.Object&&q.Result.TryGetProperty("status",out var status)&&status.GetString() is "failed" or "blocked" or "partial" or "cancelled"));
    public static bool ResolvesDraft(JsonElement draft,string tool,IEnumerable<string> used,IEnumerable<QueryResult> results) {
        var call=draft.GetProperty("call");if(call.GetProperty("tool").GetString()!=tool)return false;
        var ids=used.ToHashSet();foreach(var q in results.Where(q=>q.Delivered&&ids.Contains(q.Id)))if(q.Call.Length>0)ids.Add(q.Call);
        var dependencies=call.GetProperty("depends_on_query").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
        return dependencies.Length>0&&dependencies.All(ids.Contains);
    }
    public object Page(int offset=0,int count=12) {
        var rows=new List<object>();
        void Visit(JsonElement node,string path) {
            if(node.ValueKind==JsonValueKind.Object){if(!node.EnumerateObject().Any())rows.Add(new{path,value=node.Clone()});foreach(var p in node.EnumerateObject())Visit(p.Value,path+"/"+p.Name.Replace("~","~0").Replace("/","~1"));return;}
            if(node.ValueKind==JsonValueKind.Array){int i=0;foreach(var v in node.EnumerateArray())Visit(v,path+"/"+(i++));if(i==0)rows.Add(new{path,value=node.Clone()});return;}
            if(node.ValueKind==JsonValueKind.String&&node.GetString() is {} text&&text.Length>512) {
                for(int start=0;start<text.Length;) {int length=Math.Min(512,text.Length-start);if(start+length<text.Length&&char.IsHighSurrogate(text[start+length-1]))length--;
                    rows.Add(new{path,value_fragment=text.Substring(start,length),character_offset=start,total_characters=text.Length,encoding="json_string_content",partial_value=true});start+=length;}
            }else rows.Add(new{path,value=node.Clone()});
        }
        Visit(Result,"");count=Math.Clamp(count,1,40);offset=Math.Max(0,offset);
        var page=new List<object>();int size=0;
        foreach(var row in rows.Skip(offset).Take(count)){int cost=ContextBudget.Estimate(AgentJson.Encode(row));if(page.Count>0&&size+cost>1800)break;page.Add(row);size+=cost;}
        return new{result_id=Id,tool=Tool,observed_day=Day,observed_time=Time,offset,total_fields=rows.Count,fields=page,next=offset+page.Count<rows.Count?new{tool="query.read",args=new{id=Id,offset=offset+page.Count,count}}:null,note="JSON Pointer叶节点分页；大字符串按character_offset无损拼接value_fragment；片段不是完整事实，原文未丢弃"};
    }
    public static bool IsRead(string tool)=>ObservationContract.IsRead(tool);
    public static bool IsRead(string tool,JsonElement args)=>ObservationContract.IsRead(tool,args);
}
