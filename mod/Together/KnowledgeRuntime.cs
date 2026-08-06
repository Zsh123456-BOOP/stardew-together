using System.Text.Json;
using StardewModdingAPI;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    public KnowledgeService Knowledge {get;private set;}=null!;
    public KnowledgePacket? LastKnowledge {get;private set;}
    public string KnowledgeAnswer {get;private set;}="";
    private bool pendingKnowledge,knowledgePlanning;
    private string knowledgeQuestion="";
    private int knowledgeRevision;
    private void SetupKnowledge() {
        Knowledge=new(this);
        Helper.Events.Content.AssetsInvalidated+=(_,e)=>{if(e.NamesWithoutLocale.Any(n=>n.BaseName.StartsWith("Data/",StringComparison.OrdinalIgnoreCase)))Knowledge.Invalidate();};
        Helper.ConsoleCommands.Add("together_book","Open the shared encyclopedia or query an entry.",(_,args)=>{if(Context.IsWorldReady)OpenKnowledge(string.Join(" ",args));});
        Helper.ConsoleCommands.Add("together_knowledge","Read-only encyclopedia query; export structured evidence.",(_,args)=>{
            if(!Context.IsWorldReady)return;
            var p=Knowledge.Query(string.Join(" ",args));
            Directory.CreateDirectory(Path.Combine(Helper.DirectoryPath,"diagnostics"));
            File.WriteAllText(Path.Combine(Helper.DirectoryPath,"diagnostics","knowledge.json"),JsonSerializer.Serialize(p,jsonOptions));
            Monitor.Log(p.Status+": "+p.Text(),LogLevel.Info);
        });
        Helper.ConsoleCommands.Add("together_knowledge_ask","Ask companion using encyclopedia evidence.",(_,args)=>{if(Context.IsWorldReady)AskKnowledge(string.Join(" ",args));});
        Helper.ConsoleCommands.Add("together_knowledge_mode","Set encyclopedia discovery mode: discovered/all.",(_,args)=>{
            if(Context.IsWorldReady && args.Length==1 && args[0] is "discovered" or "all"){Data.Knowledge.DiscoveredOnly=args[0]=="discovered";CancelKnowledge();}
        });
    }
    private void ResetKnowledge(){pendingKnowledge=false;knowledgePlanning=false;LastKnowledge=null;KnowledgeAnswer="";Knowledge?.Invalidate();}
    public void RefreshKnowledgeFacts(){RefreshFacts(true);Knowledge.Observe();}
    public void OpenKnowledge(string query="") {
        if(!Context.IsWorldReady)return;
        RefreshKnowledgeFacts();Game1.activeClickableMenu=new EncyclopediaMenu(this,query);
    }
    public void ToggleKnowledgeMode(){Data.Knowledge.DiscoveredOnly=!Data.Knowledge.DiscoveredOnly;CancelKnowledge();}
    private void CancelKnowledge(){if(pendingKnowledge){generation++;pending=null;}pendingKnowledge=false;LastKnowledge=null;KnowledgeAnswer="";}
    public bool IsKnowledgeQuestion(string text) {
        if(text.StartsWith("百科") || text.StartsWith("查一下") || text.StartsWith("查资料"))return true;
        bool question=new[]{"怎么","如何","哪里","什么","多少","几天","何时","时候","为什么","能不能","是否","吗","?","？","比较","对比"}.Any(text.Contains);
        bool topic=new[]{"献祭","成熟","种子","作物","配方","材料","天气","日历","生日","礼物","钓到","捕获","机器","百科","成就","季节","生长"}.Any(text.Contains);
        return question && (topic || Knowledge.Ready && Knowledge.Search(text,limit:1).Any(h=>h.Score>=650));
    }
    public void AskKnowledge(string question,string? id=null) {
        if(!Context.IsWorldReady || question.Length>400 || string.IsNullOrWhiteSpace(question))return;
        if(Thinking){Notice="等我把这句话说完，再一起查。";return;}
        LastKnowledge=Knowledge.Query(question,id);knowledgeQuestion=question;
        AddLine(Current.Chat,"你",question);Current.Social.Reply(question,Game1.Date.TotalDays);
        KnowledgeAnswer="";pendingAutonomous=false;pendingOptions.Clear();
        if(LastKnowledge.Status is "loading" or "ambiguous"){KnowledgeAnswer=LastKnowledge.Text();Notice=KnowledgeAnswer;return;}
        StartKnowledgeCall(LastKnowledge.Status=="not_found");
    }
    private void StartKnowledgeCall(bool plan) {
        EnsureBudget();
        if(Data.Calls>=Math.Clamp(Settings.MaxCallsPerDay,1,100)){KnowledgeAnswer="今日模型额度已用完，仍可查看下面的本地资料。";pendingKnowledge=false;Notice=KnowledgeAnswer;return;}
        pendingKnowledge=true;knowledgePlanning=plan;pendingName=Selected;pendingGeneration=generation;knowledgeRevision=Knowledge.Revision;
        Data.Calls++;RecordUsage();
        string file=Path.IsPathRooted(Settings.ApiKeyFile)?Settings.ApiKeyFile:Path.Combine(Helper.DirectoryPath,Settings.ApiKeyFile);
        var evidence=LastKnowledge!;
        // Bound evidence, retaining explicit uncertainty and provenance on every included fact.
        var facts=new List<KnowledgeFact>();int chars=0;
        foreach(var f in evidence.Facts){if(chars+f.Value.Length>6500 || facts.Count==28)break;facts.Add(f);chars+=f.Value.Length;}
        evidence.Facts=facts;
        pending=ModelClient.AskKnowledge(file,Settings.Model,new{question=knowledgeQuestion,profile=Current.Profile,evidence,
            memories=MemoryRecall.Select(Current.Life.Experiences,knowledgeQuestion,Game1.Date.TotalDays,2)},plan);
        Notice=plan?"正在整理查询条件…":"正在根据手册回答…";
    }
    private bool CompleteKnowledgeReply(ModelReply reply) {
        if(!pendingKnowledge)return false;
        if(knowledgeRevision!=Knowledge.Revision){KnowledgeFailure("资料刚刚变化，请重新查询。");return true;}
        if(knowledgePlanning) {
            using var doc=JsonDocument.Parse(reply.Json);string query=doc.RootElement.GetProperty("query").GetString()??"";
            if(query.Length==0 || query.Length>80)throw new InvalidOperationException("查询词无效。");
            LastKnowledge=Knowledge.Query(query);
            if(LastKnowledge.Status!="ok"){KnowledgeFailure("没有找到明确资料，可以在手册里选择条目继续问。");return true;}
            StartKnowledgeCall(false);return true;
        }
        KnowledgeAnswer=KnowledgeRules.ValidateAnswer(reply.Json,LastKnowledge!);
        string stamp="（依据 "+LastKnowledge!.Observed+" 的资料）";
        Say(pendingName,KnowledgeAnswer+"\n"+stamp);
        Notice="回答已附在手册；查询没有改变当前约定。";pendingKnowledge=false;return true;
    }
    private void KnowledgeFailure(string reason="模型暂时没能给出可核对的解释，请看下面的本地资料。") {
        pendingKnowledge=false;KnowledgeAnswer=reason;Notice=reason;
        if(LastKnowledge!=null)AddLine(Person(pendingName).Chat,"手册",reason+"\n"+string.Join("；",LastKnowledge.Facts.Take(3).Select(f=>f.Label+"："+f.Value)));
    }
    public void PinKnowledge(string id) {
        var e=Knowledge.Index.Get(id);if(e==null || !Knowledge.Visible(e))return;
        Data.Knowledge.Favorites.Add(id);
        var needs=Facts.Bundles.FirstOrDefault(b=>!b.Complete && b.Missing.Any(n=>n.Item==id));
        if(needs!=null && Facts.Route!="joja") {
            string kind="bundle:"+needs.Id;
            if(!Data.Projects.Any(p=>p.Kind==kind && p.Status=="active"))Data.Projects.Add(new(){Kind=kind,Title="一起准备："+needs.Name,CreatedDay=Facts.Day,Owner="together"});
            UpdateProjects();Notice="已加入这项献祭准备；原有预算和物资权限保持有效。";
        } else if(id.StartsWith("craft:")){AddProgressProject(id);}
        else {Data.Knowledge.Note(id,"想一起了解或准备："+e.Name);Notice="已收藏并记下心愿；尚未派工，具体活动可以再与伙伴商量。";}
    }
    private bool TryKnowledgeShare(string name,Companion person,Situation s) {
        var book=Data.Knowledge;
        if(!Knowledge.Ready || book.SharedDay==s.Day || person.Social.Mode!="normal" || s.Threat || person.Job?.Status is "active" or "waiting")return false;
        foreach(string id in book.Favorites) {
            var entry=Knowledge.Index.Get(id);if(entry==null || !Knowledge.Visible(entry))continue;
            var need=Data.Projects.Where(p=>p.Status=="active").SelectMany(p=>p.Needs).FirstOrDefault(n=>n.Item==id && n.Owned>=n.Count);
            if(need==null || book.Shared.Contains(id+":"+s.Day))continue;
            book.SharedDay=s.Day;book.Shared.Add(id+":"+s.Day);if(book.Shared.Count>80)book.Shared=book.Shared.TakeLast(80).ToHashSet();
            OpenTopic(name,"你收藏的“"+entry.Name+"”，手头合格材料够这项需求了。要不要找个方便的时候去交？我可以陪着；其他计划的预留还要一起核对。","knowledge:"+id+":"+s.Day);return true;
        }
        return false;
    }
}
