using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    // Invoked only through RunLabScenario's isolated AgentLab gate.
    private string KnowledgeContracts() {
        if(!Knowledge.Ready)throw new InvalidOperationException("knowledge_still_loading");
        var results=new List<object>();
        void Check(bool pass,string label)=>results.Add(new{pass,label});
        var notebook=JsonSerializer.Serialize(Data.Knowledge);
        var random=Game1.random;Game1.random=new Random(82519);
        string Before()=>JsonSerializer.Serialize(new{money=Game1.player.Money,items=Game1.player.Items.Where(i=>i!=null).Select(i=>new{i.QualifiedItemId,i.Stack,i.Quality}),quests=Game1.player.questLog.Select(q=>new{id=q.id.Value,complete=q.completed.Value}),fish=Game1.player.fishCaught.Pairs.Select(p=>new{p.Key,p.Value}),achievements=Game1.player.achievements.ToArray()});
        string before=Before();
        try {
            Data.Knowledge.DiscoveredOnly=false;
            Check(Knowledge.Index.Entries.Count>1000,"runtime catalog built from actual game content");
            foreach(string kind in new[]{"item","crop","fish","machine","recipe","npc","location","guide"})Check(Knowledge.Search("",kind).Count>0,"catalog contains "+kind);
            var crop=Knowledge.Query("", "(O)472");Check(crop.Status=="ok" && crop.Facts.Any(f=>f.Label=="播种日历"),"crop contains live planting calculation");
            var fish=Knowledge.Query("", "(O)145");Check(fish.Facts.Any(f=>f.Support=="partial" && f.Source.Contains("Data/Locations")),"fish eligibility preserves spatial uncertainty");
            Check(Knowledge.Query("日历","guide:calendar").Facts.Any(f=>f.Value.Contains("明日农场预报")),"calendar uses native weather forecast");
            Check(Knowledge.Query("春天种什么").EntityIds.Any(id=>Knowledge.Index.Get(id)?.Kind=="crop"),"structured season crop query returns real crop entities");
            Check(Knowledge.Query("献祭","guide:bundle").Facts.Any(f=>f.Source=="原生献祭状态"),"bundle uses real slot progress");
            var npc=Knowledge.Query("", "npc:Abigail");Check(npc.EntityIds.Contains("npc:Abigail"),"NPC lookup resolves stable identity");
            Check(Knowledge.Search("Parsnip Seeds").FirstOrDefault()?.Entry.Id=="(O)472","English canonical name works in localized game");
            Check(Knowledge.Search("防风草种籽").Any(h=>h.Entry.Id=="(O)472" && h.Score<650),"Chinese typo is a candidate, not exact certainty");
            Check(Knowledge.Query("totallyunknownxyz987").Status=="not_found","unknown entity remains unknown");
            Data.Knowledge.DiscoveredOnly=true;
            var hidden=Knowledge.Index.Entries.FirstOrDefault(e=>e.Kind=="location"&&!Knowledge.Visible(e));
            Check(hidden!=null && Knowledge.Query("",hidden.Id).Status=="not_found","direct ID cannot bypass discovery filter");
            Check(Knowledge.Search("",limit:20000).All(h=>Knowledge.Visible(h.Entry)),"discovery filtering applies to full browse");
            Data.Knowledge.Note("(O)472","下次一起种");Data.Knowledge.Favorites.Add("(O)472");Data.Knowledge.Discovered.Add("(O)472");
            Check(Knowledge.Query("","(O)472").Facts.Any(f=>f.Source.Contains("手写便签")),"notebook notes are distinguished from facts");
            var perf=new List<double>();
            foreach(string query in Enumerable.Repeat(new[]{"Parsnip Seeds","机器为什么不工作","钓鱼","xxunrecognized"},30).SelectMany(x=>x)) {
                var watch=System.Diagnostics.Stopwatch.StartNew();Knowledge.Search(query);perf.Add(watch.Elapsed.TotalMilliseconds);
            }
            perf.Sort();results.Add(new{pass=perf[(int)(perf.Count*.95)]<50,label="warm search p95 < 50ms",p95_ms=perf[(int)(perf.Count*.95)],max_ms=perf.Last()});
            Check(before==Before(),"read-only lookup preserves money, inventory, quests, fish counters and achievements");
            KnowledgeCatalog.PreviewItem("(O)145");KnowledgeCatalog.PreviewItem("(BC)12");
            Check(Game1.random.Next()==new Random(82519).Next(),"lookup does not advance game random stream");
        }finally {Game1.random=random;Data.Knowledge=JsonSerializer.Deserialize<KnowledgeNotebook>(notebook)!;}
        return JsonSerializer.Serialize(new{scope="isolated AgentLab runtime contract checks",results},jsonOptions);
    }
}
