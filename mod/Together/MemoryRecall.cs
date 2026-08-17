namespace Together;

public static class MemoryRecall {
    public static string[] SearchTerms(string query) {
        var related=Topics.Where(group=>group.Any(word=>query.Contains(word,StringComparison.OrdinalIgnoreCase))).SelectMany(g=>g);
        return related.Concat(query.Split(new[]{' ','，','。',',',';','；','？','?'},StringSplitOptions.RemoveEmptyEntries)).Where(s=>s.Length>1).Distinct(StringComparer.OrdinalIgnoreCase).Take(16).ToArray();
    }
    private static readonly string[][] Topics={
        new[]{"钓鱼","鱼","水边","海边"},new[]{"挖矿","矿洞","石头","铜矿"},
        new[]{"农场","农活","浇水","收获","作物"},new[]{"机器","补料","熔炉","加工"},
        new[]{"箱子","材料","物资","收货"},new[]{"献祭","社区","共同目标"},
        new[]{"动物","小鸡","抚摸"},new[]{"休息","累","精力"}
    };
    public static List<Experience> Select(IEnumerable<Experience> memories,string query,int day,int limit=5) {
        var topics=Topics.Where(group=>group.Any(word=>query.Contains(word,StringComparison.OrdinalIgnoreCase))).ToArray();
        return memories.Where(m=>m.Day<=day).OrderByDescending(m=>
            topics.Count(group=>group.Any(word=>m.Summary.Contains(word,StringComparison.OrdinalIgnoreCase)))*100
            +Math.Max(0,30-(day-m.Day)))
            .ThenByDescending(m=>m.Day).ThenByDescending(m=>m.Minute).Take(limit).ToList();
    }
}
