using System.Text;
using System.Text.Json;

namespace Together;

// Pure data: safe to index off-thread or send to a model; no game object references.
public sealed class KnowledgeEntry {
    public string Id {get;set;}="";
    public string Kind {get;set;}="item";
    public string Name {get;set;}="";
    public string Description {get;set;}="";
    public string Source {get;set;}="";
    public List<string> Aliases {get;set;}=new();
    public List<string> Tags {get;set;}=new();
    public List<string> Links {get;set;}=new();
    public Dictionary<string,string> Fields {get;set;}=new();
}
public sealed record KnowledgeHit(KnowledgeEntry Entry,int Score,string Reason);
public sealed record KnowledgeFact(string Id,string Label,string Value,string Source,string Support="verified");
public sealed class KnowledgePacket {
    public string Query {get;set;}="";
    public string Observed {get;set;}="";
    public string Save {get;set;}="";
    public string Status {get;set;}="ok";
    public List<KnowledgeFact> Facts {get;set;}=new();
    public List<string> Candidates {get;set;}=new();
    public List<string> EntityIds {get;set;}=new();
    public string Text()=>string.Join("\n\n",Facts.Select(f=>f.Label+"："+f.Value+(f.Support=="partial"?"（部分支持）":"")));
}
public sealed class KnowledgeNotebook {
    public bool DiscoveredOnly {get;set;}=true;
    public HashSet<string> Discovered {get;set;}=new();
    public HashSet<string> Visited {get;set;}=new();
    public HashSet<string> Favorites {get;set;}=new();
    public List<string> Recent {get;set;}=new();
    public Dictionary<string,string> Notes {get;set;}=new();
    public int SharedDay {get;set;}=-1;
    public HashSet<string> Shared {get;set;}=new();
    public void Visit(string id) {Recent.Remove(id);Recent.Insert(0,id);if(Recent.Count>24)Recent.RemoveRange(24,Recent.Count-24);}
    public void Note(string id,string value) {value=value.Trim();if(value.Length>240)value=value[..240];if(value.Length==0)Notes.Remove(id);else Notes[id]=value;}
}
public sealed class KnowledgeIndex {
    public IReadOnlyList<KnowledgeEntry> Entries {get;}
    private readonly Dictionary<string,KnowledgeEntry> byId;
    private readonly List<(KnowledgeEntry Entry,string[] Names,string[] Tags)> rows;
    public KnowledgeIndex(IEnumerable<KnowledgeEntry> entries) {
        Entries=entries.GroupBy(e=>e.Id).Select(g=>g.First()).ToArray();
        byId=Entries.ToDictionary(e=>e.Id,StringComparer.OrdinalIgnoreCase);
        rows=Entries.Select(e=>(e,new[]{e.Name,e.Id}.Concat(e.Aliases).Select(Normalize).Where(n=>n.Length>0).Distinct().ToArray(),e.Tags.Select(Normalize).ToArray())).ToList();
    }
    public KnowledgeEntry? Get(string id)=>byId.GetValueOrDefault(id);
    public static string Normalize(string text)=>new(text.Normalize(NormalizationForm.FormKC).ToLowerInvariant().Where(char.IsLetterOrDigit).ToArray());
    public IReadOnlyList<KnowledgeHit> Search(string query,Func<KnowledgeEntry,bool> visible,string kind="all",int limit=8) {
        string q=Normalize(query);limit=Math.Clamp(limit,1,20000);
        var hits=new List<KnowledgeHit>();
        foreach(var row in rows) {
            var e=row.Entry;if(!visible(e) || kind!="all" && e.Kind!=kind)continue;
            int score=0;string reason="分类浏览";
            if(q.Length==0)score=1;
            foreach(string n in row.Names) {
                int s=0;string why="";
                if(n==q){s=1000;why="名称或别名完全匹配";}
                else if(q.Length>0 && n.StartsWith(q)){s=800;why="名称前缀";}
                else if(q.Length>1 && n.Contains(q)){s=700;why="名称包含";}
                else if(n.Length>=2 && q.Contains(n)){s=650+Math.Min(n.Length,40);why="问题提到此条目";}
                else if(q.Length>=3 && q.Length<=24 && Math.Abs(n.Length-q.Length)<=1 && Distance(n,q)<=1){s=400;why="可能有一个错字";}
                if(s>score){score=s;reason=why;}
            }
            int tags=row.Tags.Count(t=>t.Length>=2 && (q.Contains(t) || q.Length>=2 && t.Contains(q)));
            if(tags>0 && score<300){score=200+Math.Min(80,tags*20);reason="主题或用途匹配";}
            if(score>0)hits.Add(new(e,score,reason));
        }
        return hits.OrderByDescending(h=>h.Score).ThenBy(h=>h.Entry.Name,StringComparer.Ordinal).Take(limit).ToArray();
    }
    private static int Distance(string a,string b) {
        int[] previous=Enumerable.Range(0,b.Length+1).ToArray();
        for(int i=1;i<=a.Length;i++){int[] row=new int[b.Length+1];row[0]=i;for(int j=1;j<=b.Length;j++)row[j]=Math.Min(Math.Min(row[j-1]+1,previous[j]+1),previous[j-1]+(a[i-1]==b[j-1]?0:1));previous=row;}
        return previous[b.Length];
    }
}
public static class KnowledgeRules {
    public static string Season(string s)=>s.ToLowerInvariant() switch {"spring"=>"春","summer"=>"夏","fall"=>"秋","winter"=>"冬",_=>s};
    public static string Kind(string s)=>s switch {"item"=>"物品","fish"=>"鱼类","crop"=>"种植","npc"=>"人物","recipe"=>"配方","machine"=>"机器","location"=>"地点","guide"=>"机制","goal"=>"目标",_=>s};
    public static string Field(string[] parts,int i)=>i>=0 && i<parts.Length?parts[i]:"";
    public static int Integer(string text,int fallback=0)=>int.TryParse(text,out int n)?n:fallback;
    public static bool InTime(string spans,int time) {
        var a=spans.Split(' ',StringSplitOptions.RemoveEmptyEntries);for(int i=0;i+1<a.Length;i+=2)if(time>=Integer(a[i],9999)&&time<Integer(a[i+1],-1))return true;return false;
    }
    public static string ValidateAnswer(string json,KnowledgePacket packet) {
        using var d=JsonDocument.Parse(json);var r=d.RootElement;
        string speech=r.GetProperty("speech").GetString()??"";
        var ids=r.GetProperty("evidence_ids").EnumerateArray().Select(e=>e.GetString()??"").ToArray();
        if(speech.Length==0 || speech.Length>900 || ids.Length==0 || ids.Any(id=>!packet.Facts.Any(f=>f.Id==id)))throw new InvalidOperationException("回答缺少可核对的依据，显示原始资料。");
        // Reject invented numbers, including dates and quantities. Context stamps are evidence too.
        var allowed=System.Text.RegularExpressions.Regex.Matches(packet.Text()+packet.Observed,@"\d+(?:\.\d+)?").Select(m=>m.Value).ToHashSet();
        if(System.Text.RegularExpressions.Regex.Matches(speech,@"\d+(?:\.\d+)?").Any(m=>!allowed.Contains(m.Value)))throw new InvalidOperationException("回答出现未在资料中提供的数字，显示原始资料。");
        return speech;
    }
}
