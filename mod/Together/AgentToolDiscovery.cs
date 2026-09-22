using System.Text.Json;
namespace Together;

// Keep the common farming contracts stable for prompt caching. Other complete
// contracts are loaded on demand; discovery never truncates parameter schemas.
public static class AgentToolDiscovery {
    public static readonly string[] CoreNames={"query.read","context.read","progress.read","progress.catalog","progress.dependencies","quest_board.read","day.routine","farm.business","player.service","player.buy","player.procure","player.ship_items","player.accept_quest","player.claim_reward","player.social","inventory.read","inventory.capacity","player.discard","shop.read","farm.select_seeds","goal.create","goal.run","goal.prepare","tools.lookup","world.read","menu.read","menu.choose","menu.close","plan.read","plan.submit","plan.cancel","work.run","day.plan","farm.business_status","farm.operating","farm.production","farm.cleanup","farm.plan","player.collect_home_gifts","player.travel","player.sleep","knowledge.search","knowledge.get","memory.search","agent.wait","agent.pause"};
    public static Dictionary<string,string> Core(IReadOnlyDictionary<string,string> catalog) {
        var result=CoreNames.Where(catalog.ContainsKey).ToDictionary(k=>k,k=>catalog[k]);
        return result;
    }
    public static object Lookup(IReadOnlyDictionary<string,string> catalog,JsonElement args) {
        var names=args.TryGetProperty("names",out var raw)?raw.Deserialize<string[]>()??Array.Empty<string>():Array.Empty<string>();
        if(names.Length>12||names.Any(n=>n==null))throw new InvalidOperationException("lookup_requires_at_most_12_names");
        string query=args.TryGetProperty("query",out var q)?q.GetString()??"":"";
        if(query.Length>100)throw new InvalidOperationException("lookup_query_too_long");
        var keys=names.Length>0?names.Distinct().ToArray():catalog.Keys.Where(k=>query.Length>0&&(k.Contains(query,StringComparison.OrdinalIgnoreCase)||catalog[k].Contains(query,StringComparison.OrdinalIgnoreCase))).OrderBy(k=>k).Take(12).ToArray();
        var unknown=keys.Where(k=>!catalog.ContainsKey(k)).ToArray();
        bool fallback=!keys.Any(catalog.ContainsKey);
        if(fallback&&(query.Length>0||names.Length>0)) {
            string text=(query+" "+string.Join(" ",names)).ToLowerInvariant();
            var synonyms=new Dictionary<string,string>{{"social","社交"},{"greet","社交"},{"talk","社交"},{"gift","送礼"},{"seed","种子"},{"buy","采购"},{"shop","商店"},{"fish","钓鱼"},{"recipe","配方"},{"craft","制作"},{"quest","任务"},{"sleep","睡觉"},{"wood","木材"},{"storage","存货"}};
            var terms=text.Split(new[]{' ','.','_',',','，','/'},StringSplitOptions.RemoveEmptyEntries).Concat(synonyms.Where(kv=>text.Contains(kv.Key)).Select(kv=>kv.Value)).Distinct().ToArray();
            keys=catalog.Select(kv=>new{kv.Key,Score=terms.Count(t=>kv.Key.Contains(t,StringComparison.OrdinalIgnoreCase)||kv.Value.Contains(t,StringComparison.OrdinalIgnoreCase))}).Where(r=>r.Score>0).OrderByDescending(r=>r.Score).ThenBy(r=>r.Key).Take(8).Select(r=>r.Key).ToArray();
            if(keys.Length==0)keys=new[]{"knowledge.search","world.read","plan.read","work.run"}.Where(catalog.ContainsKey).ToArray();
        }
        return new{definitions=keys.Where(catalog.ContainsKey).ToDictionary(k=>k,k=>catalog[k]),unknown,fallback,index=query.Length==0&&names.Length==0?catalog.Keys.OrderBy(k=>k).ToArray():null,note=fallback?"未精确匹配，已返回相近能力或世界/百科入口；不要重试同一不存在的工具名。":"完整参数定义；查询不执行动作。"};
    }
}
