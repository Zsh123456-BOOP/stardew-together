using System.Text.Json;
namespace Together;

// Keep the common farming contracts stable for prompt caching. Other complete
// contracts are loaded on demand; discovery never truncates parameter schemas.
public static class AgentToolDiscovery {
    public static readonly string[] CoreNames={"tools.lookup","world.read","menu.read","menu.choose","menu.close","plan.read","plan.submit","plan.cancel","work.run","day.plan","farm.business_status","farm.operating","farm.production","farm.cleanup","player.collect_home_gifts","player.travel","player.sleep","knowledge.search","knowledge.get","memory.search","agent.wait","agent.pause"};
    public static Dictionary<string,string> Core(IReadOnlyDictionary<string,string> catalog) {
        var result=CoreNames.Where(catalog.ContainsKey).ToDictionary(k=>k,k=>catalog[k]);
        if(result.ContainsKey("work.run"))result["work.run"]="常用模式完整参数：{goal:water|harvest|forage|wood|stone|fiber|fish|store|refill,actor_id?:player或真实伙伴ID,location?:真实地图名,count?:0..999,stock_target?:1..9999,item?:QID,include_trees?:bool,required_free_slots?:0..12,reserve_stamina?:15..270,until?:HHMM<=2300,max_food?:0..10}。经营材料stock_target/count为全队目标库存，不是每次新增量，必须有已批准用途。water/harvest/forage的count=0完成范围内目标；fish的count=1..100是实际新增捕获数(默认3)，省略item为任意鱼。程序持续选点、原生操作、拾取、补给并回原地点，无需坐标。fish/refill/完整伐木限玩家；NPC木材仅树枝。store按required_free_slots准备容量，不出售。默认保留20体力，22点停止。其他模式plant/mine_trip/加工/畜牧等先用tools.lookup(names:[work.run])查询完整定义。";
        return result;
    }
    public static object Lookup(IReadOnlyDictionary<string,string> catalog,JsonElement args) {
        var names=args.TryGetProperty("names",out var raw)?raw.Deserialize<string[]>()??Array.Empty<string>():Array.Empty<string>();
        if(names.Length>12||names.Any(n=>n==null))throw new InvalidOperationException("lookup_requires_at_most_12_names");
        string query=args.TryGetProperty("query",out var q)?q.GetString()??"":"";
        if(query.Length>100)throw new InvalidOperationException("lookup_query_too_long");
        var keys=names.Length>0?names.Distinct().ToArray():catalog.Keys.Where(k=>query.Length>0&&(k.Contains(query,StringComparison.OrdinalIgnoreCase)||catalog[k].Contains(query,StringComparison.OrdinalIgnoreCase))).OrderBy(k=>k).Take(12).ToArray();
        return new{definitions=keys.Where(catalog.ContainsKey).ToDictionary(k=>k,k=>catalog[k]),unknown=keys.Where(k=>!catalog.ContainsKey(k)).ToArray(),index=query.Length==0&&names.Length==0?catalog.Keys.OrderBy(k=>k).ToArray():null,note="完整参数定义；缺少工具可按名称或中文用途继续查询。查询不执行动作。"};
    }
}
