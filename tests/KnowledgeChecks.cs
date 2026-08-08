using System.Text.Json;
using Together;
public static class KnowledgeChecks {
    public static void Run(Action<bool,string> check) {
        var wrapped=KnowledgeRules.Wrap("没有空格的中文说明abcdef",6,c=>c>127?2:1);
        check(wrapped.All(s=>s.Sum(c=>c>127?2:1)<=6) && string.Concat(wrapped)=="没有空格的中文说明abcdef","knowledge Chinese wrapping stays inside panel without losing characters");
        var entries=new[]{new KnowledgeEntry{Id="(O)472",Name="防风草种子",Kind="crop",Aliases=new(){"Parsnip Seeds","欧防风种子"},Tags=new(){"种植"}},new KnowledgeEntry{Id="mod:seed",Name="防风草种子",Kind="crop"},new KnowledgeEntry{Id="guide:growth",Name="生长指南",Kind="guide",Tags=new(){"树","不长"}},new KnowledgeEntry{Id="secret",Name="秘密种子",Kind="item"}};
        var index=new KnowledgeIndex(entries);
        check(index.Search("ＰＡＲＳＮＩＰ SEEDS",_=>true).First().Entry.Id=="(O)472","knowledge Unicode English alias normalization");
        check(index.Search("欧防风种子",_=>true).First().Score==1000,"knowledge Chinese alias exact match");
        check(index.Search("防风草种籽",_=>true).First().Reason.Contains("错字"),"knowledge one-character typo stays a candidate");
        check(index.Search("防风草种子",_=>true).Count==2,"knowledge same-name entities preserved");
        check(index.Search("秘密",e=>e.Id!="secret").Count==0,"knowledge hidden entries filtered before ranking");
        check(index.Search("欧防风种子",_=>true,"fish").Count==0,"knowledge category is a hard filter");
        check(index.Search("为什么树不长",_=>true).First().Entry.Id=="guide:growth","knowledge mechanism topic matching without full-text scoring");
        check(index.Search("不存在的东西xyz",_=>true).Count==0,"knowledge unknown query returns no invented entity");
        check(index.Search("",_=>true,limit:100).Count==4,"knowledge empty query supports catalog browsing");
        check(!KnowledgeRules.InTime("600 1900",1900) && KnowledgeRules.InTime("600 1900",600),"knowledge fish time end boundary exclusive");
        check(KnowledgeRules.InTime("600 1000 1600 2400",1700) && !KnowledgeRules.InTime("600 1000 1600 2400",1200),"knowledge split fishing time ranges");
        var book=new KnowledgeNotebook();for(int n=0;n<40;n++)book.Visit(n.ToString());book.Visit("39");book.Note("39",new string('文',300));
        check(book.Recent.Count==24 && book.Recent.Distinct().Count()==24 && book.Notes["39"].Length==240,"knowledge notebook bounded and deduplicated");
        var saved=JsonSerializer.Deserialize<SaveData>(JsonSerializer.Serialize(new SaveData{Knowledge=book}))!;
        check(saved.Knowledge.Recent.SequenceEqual(book.Recent) && saved.Knowledge.DiscoveredOnly && saved.SchemaVersion==5,"knowledge save persistence, downgrade protection and spoiler-safe default");
        check(JsonSerializer.Deserialize<SaveData>("{}")!.Knowledge.DiscoveredOnly,"knowledge migration from pre-encyclopedia saves");
        var packet=new KnowledgePacket{Facts=new(){new("f","库存","有 3 件。","游戏")}};
        check(KnowledgeRules.ValidateAnswer("{\"speech\":\"我们有3件。\",\"evidence_ids\":[\"f\"]}",packet).Contains("3"),"knowledge grounded reply accepted");
        bool Reject(string json){try{KnowledgeRules.ValidateAnswer(json,packet);return false;}catch{return true;}}
        check(Reject("{\"speech\":\"有99件\",\"evidence_ids\":[\"f\"]}"),"knowledge unsupported numeric claim rejected");
        check(Reject("{\"speech\":\"我们有材料\",\"evidence_ids\":[\"invented\"]}"),"knowledge forged source rejected");
        check(Reject("{\"speech\":\"已经完成任务\",\"evidence_ids\":[]}"),"knowledge answer without evidence rejected");
    }
}
