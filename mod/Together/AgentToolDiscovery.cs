using System.Text.Json;
namespace Together;

// Keep the common farming contracts stable for prompt caching. Other complete
// contracts are loaded on demand; discovery never truncates parameter schemas.
public static class AgentToolDiscovery {
    public static readonly string[] CoreNames={"progress.read","progress.catalog","progress.dependencies","quest_board.read","day.routine","farm.business","player.service","player.buy","player.procure","player.ship_items","player.accept_quest","player.claim_reward","player.social","inventory.read","shop.read","farm.select_seeds","goal.create","goal.run","goal.prepare","tools.lookup","world.read","menu.read","menu.choose","menu.close","plan.read","plan.submit","plan.cancel","work.run","day.plan","farm.business_status","farm.operating","farm.production","farm.cleanup","farm.plan","player.collect_home_gifts","player.travel","player.sleep","knowledge.search","knowledge.get","memory.search","agent.wait","agent.pause"};
    public static Dictionary<string,string> Core(IReadOnlyDictionary<string,string> catalog) {
        var result=CoreNames.Where(catalog.ContainsKey).ToDictionary(k=>k,k=>catalog[k]);
        if(result.ContainsKey("work.run"))result["work.run"]="常用模式完整参数：{goal:cleanup|water|harvest|forage|wood|stone|fiber|fish|store|refill,actor_id?:player或真实伙伴ID,location?:真实地图名,cleanup_id?:已批准清理单ID,count?:0..999,stock_target?:1..9999,item?:QID,include_trees?:bool,required_free_slots?:0..12,reserve_stamina?:0..270,until?:HHMM<=2400,max_food?:0..10}。stock_target表示全队目标库存；count始终为新增数量。材料采集无需项目批准；count>0新增指定数量，stock_target补库存差额。count=0或省略时优先补当前项目缺口，无缺口默认新增20。cleanup+cleanup_id可交给玩家或伙伴，按既定片区混合清理（伙伴不砍整树），无需材料用途，不越过保护区。water/harvest/forage的count=0完成范围内目标；fish的count=1..100是实际新增捕获数(默认3)，省略item为任意鱼。程序持续选点、原生操作、拾取、补给并回原地点，无需坐标。fish/refill/完整伐木限玩家；NPC木材仅树枝。玩家木材默认比较整树与树枝，可用include_trees=false排除树木。store按required_free_slots准备容量，不出售；存取货收尾可指定until至2500。water省略location时指农场；真实缺水为0会直接报告无需浇水，不出发行程。默认不额外保留体力，模型可指定本任务reserve_stamina，默认24点停止。播种必须先farm.plan(seed,count)取得plan_id，再work.run(goal:plant,plan_id)，自动清障、翻土、播种和浇水，不能以harvest代替播种。其他模式mine_trip/加工/畜牧等先用tools.lookup(names:[work.run])查询完整定义。";
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
