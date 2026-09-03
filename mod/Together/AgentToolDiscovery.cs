using System.Text.Json;
namespace Together;

// Keep the common farming contracts stable for prompt caching. Other complete
// contracts are loaded on demand; discovery never truncates parameter schemas.
public static class AgentToolDiscovery {
    public static readonly string[] CoreNames={"tools.lookup","world.read","inventory.read","map.read","menu.read","menu.choose","menu.close","plan.read","plan.submit","plan.cancel","work.run","action.status","day.plan","farm.business","farm.business_status","farm.operating","farm.production","farm.cleanup","farm.plan","player.collect_home_gifts","player.travel","player.procure","player.craft","player.place_facility","player.ship_items","player.fish","player.sleep","knowledge.search","knowledge.get","memory.search","agent.wait","agent.pause"};
    public static Dictionary<string,string> Core(IReadOnlyDictionary<string,string> catalog)=>CoreNames.Where(catalog.ContainsKey).ToDictionary(k=>k,k=>catalog[k]);
    public static object Lookup(IReadOnlyDictionary<string,string> catalog,JsonElement args) {
        var names=args.TryGetProperty("names",out var raw)?raw.Deserialize<string[]>()??Array.Empty<string>():Array.Empty<string>();
        if(names.Length>12||names.Any(n=>n==null))throw new InvalidOperationException("lookup_requires_at_most_12_names");
        string query=args.TryGetProperty("query",out var q)?q.GetString()??"":"";
        if(query.Length>100)throw new InvalidOperationException("lookup_query_too_long");
        var keys=names.Length>0?names.Distinct().ToArray():catalog.Keys.Where(k=>query.Length>0&&(k.Contains(query,StringComparison.OrdinalIgnoreCase)||catalog[k].Contains(query,StringComparison.OrdinalIgnoreCase))).OrderBy(k=>k).Take(12).ToArray();
        return new{definitions=keys.Where(catalog.ContainsKey).ToDictionary(k=>k,k=>catalog[k]),unknown=keys.Where(k=>!catalog.ContainsKey(k)).ToArray(),index=query.Length==0&&names.Length==0?catalog.Keys.OrderBy(k=>k).ToArray():null,note="完整参数定义；缺少工具可按名称或中文用途继续查询。查询不执行动作。"};
    }
}
