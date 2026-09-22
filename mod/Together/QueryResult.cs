using System.Text.Json;
namespace Together;

// Saved receipts survive pauses and reloads. Delivery is not proof of comprehension.
public sealed class QueryResult {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Call {get;set;}="";
    public string Tool {get;set;}="";
    public int Day {get;set;}
    public JsonElement Args {get;set;}
    public JsonElement Result {get;set;}
    public bool Delivered {get;set;}
    public static bool ResolvesDraft(JsonElement draft,string tool,IEnumerable<string> used,IEnumerable<QueryResult> results) {
        var call=draft.GetProperty("call");if(call.GetProperty("tool").GetString()!=tool)return false;
        var ids=used.ToHashSet();foreach(var q in results.Where(q=>q.Delivered&&ids.Contains(q.Id)))if(q.Call.Length>0)ids.Add(q.Call);
        var dependencies=call.GetProperty("depends_on_query").EnumerateArray().Select(x=>x.GetString()??"").ToArray();
        return dependencies.Length>0&&dependencies.All(ids.Contains);
    }
    public object Page(int offset=0,int count=12) {
        var rows=new List<object>();
        void Visit(JsonElement node,string path) {
            if(node.ValueKind==JsonValueKind.Object){foreach(var p in node.EnumerateObject())Visit(p.Value,path+"/"+p.Name.Replace("~","~0").Replace("/","~1"));return;}
            if(node.ValueKind==JsonValueKind.Array){int i=0;foreach(var v in node.EnumerateArray())Visit(v,path+"/"+(i++));if(i==0)rows.Add(new{path,value=node.Clone()});return;}
            rows.Add(new{path,value=node.Clone()});
        }
        Visit(Result,"");count=Math.Clamp(count,1,40);offset=Math.Max(0,offset);
        // Whole JSON values are retained, never truncated into misleading partial facts.
        return new{result_id=Id,tool=Tool,observed_day=Day,offset,total_fields=rows.Count,fields=rows.Skip(offset).Take(count),next=offset+count<rows.Count?new{tool="query.read",args=new{id=Id,offset=offset+count,count}}:null,note="分页只代表未展开，非不存在；JSON Pointer路径保留原始字段含义"};
    }
    public static bool IsRead(string tool)=>tool.StartsWith("knowledge.")||tool.StartsWith("memory.")||tool.StartsWith("tools.")||tool.EndsWith(".read")||tool is "progress.dependencies" or "progress.catalog" or "progress.roadmap" or "goal.requirements";
}
