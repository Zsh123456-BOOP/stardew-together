using System.Text.Json;
namespace Together;

// Series encode the same native metric, not a fabricated prerequisite graph.
public static class AchievementView {
    public static string Series(int id)=>id switch {
        >=0 and <=4=>"income",18 or 19=>"house",29 or 30=>"quests",20 or 21 or 22=>"crafting",15 or 16 or 17=>"cooking",
        24 or 25 or 26=>"fish_variety",27=>"fish_catches",6 or 11 or 12 or 13=>"friendship_1250",7 or 9=>"friendship_2500",_=>"achievement:"+id
    };
    public static object Summary(int id,string title,bool earned,JsonElement rule)=>new {
        id="achievement:"+id,title,series=Series(id),earned,
        known=rule.TryGetProperty("known",out var known)&&known.ValueKind==JsonValueKind.True,
        metric=rule.TryGetProperty("metric",out var metric)?metric.GetString():null,
        current=rule.TryGetProperty("current",out var current)?(long?)current.GetInt64():null,
        target=rule.TryGetProperty("target",out var target)?(long?)target.GetInt64():null,
        condition_satisfied=rule.TryGetProperty("condition_satisfied",out var met)?(bool?)met.GetBoolean():null
    };
    public static JsonElement[] Next(IEnumerable<JsonElement> rows) {
        double Target(JsonElement r)=>r.TryGetProperty("target",out var t)&&t.ValueKind==JsonValueKind.Number?t.GetDouble():double.MaxValue;
        return rows.Where(r=>!r.GetProperty("earned").GetBoolean()).GroupBy(r=>r.GetProperty("series").GetString()).Select(g=>g.OrderBy(Target).First()).ToArray();
    }
    public static JsonElement[] Candidates(IEnumerable<JsonElement> rows,int limit=5) {
        string[] order={"income","crafting","fish_variety","quests","friendship_1250","house","cooking","fish_catches","friendship_2500"};
        int Rank(JsonElement r){int i=Array.IndexOf(order,r.GetProperty("series").GetString());return i<0?order.Length:i;}
        double Ratio(JsonElement r)=>r.TryGetProperty("current",out var c)&&r.TryGetProperty("target",out var t)&&t.ValueKind==JsonValueKind.Number&&c.ValueKind==JsonValueKind.Number?c.GetDouble()/Math.Max(1,t.GetDouble()):-1;
        return Next(rows).OrderByDescending(Ratio).ThenBy(Rank).Take(limit).ToArray();
    }
}
