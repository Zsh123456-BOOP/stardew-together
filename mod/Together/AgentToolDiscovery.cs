using System.Text.Json;
namespace Together;

// Keep the common farming contracts stable for prompt caching. Other complete
// contracts are loaded on demand; discovery never truncates parameter schemas.
public static class AgentToolDiscovery {
    public static readonly string[] CoreNames={"query.read","context.read","tools.lookup","knowledge.search","work.run","plan.submit","plan.cancel","player.travel","player.sleep","agent.wait","agent.pause"};
    public static readonly Dictionary<string,string[]> Groups=new(){
        ["farm"]=new[]{"farm.plan","farm.cleanup","day.routine","player.collect_home_gifts"},
        ["trade"]=new[]{"player.service","shop.read","farm.select_seeds","player.buy","player.procure","player.ship_items"},
        ["storage_production"]=new[]{"inventory.capacity","goal.create","goal.run","farm.production","farm.business_status"},
        ["progress_social"]=new[]{"progress.read","progress.catalog","progress.dependencies","quest_board.read","player.social","player.accept_quest","player.claim_reward"},
        ["animals"]=new[]{"animals.read","farm.business","player.acquire_animal"},
        ["exploration"]=new[]{"world.read","map.scan","fishing.options","player.read_mail"},
        ["menu"]=new[]{"menu.read","menu.choose","menu.close","menu.text"}
    };
    public const string GroupIndex="farm=农务布局/整理/礼包；trade=商店采购/选种/出货；storage_production=容量/制作/设施投资；progress_social=任务/依赖/社交；animals=畜牧；exploration=地图/钓鱼/邮件；menu=原生菜单。tools.lookup可按group、用途或名称扩展；工具可见不代表已解锁或已获预算。";
    public static string[] LeasedTools(MemoryCheckpoint memory,int day,int decision)=>memory.EquippedTools.Where(p=>p.Value==day&&decision<=memory.EquippedToolsUntilDecision.GetValueOrDefault(p.Key,-1)).Select(p=>p.Key).OrderBy(n=>n,StringComparer.Ordinal).ToArray();
    public static Dictionary<string,string> Select(IReadOnlyDictionary<string,string> catalog,JsonElement context) {
        var names=CoreNames.ToHashSet(StringComparer.Ordinal);
        void Group(string group){foreach(string name in Groups[group])names.Add(name);}
        void Scan(JsonElement node) {
            if(node.ValueKind==JsonValueKind.Array){foreach(var row in node.EnumerateArray())Scan(row);return;}
            if(node.ValueKind!=JsonValueKind.Object)return;
            foreach(var p in node.EnumerateObject()) {
                if(p.Name=="equipped_names"&&p.Value.ValueKind==JsonValueKind.Array)foreach(var n in p.Value.EnumerateArray())if(n.ValueKind==JsonValueKind.String)names.Add(n.GetString()!);
                if(p.Name=="definitions"&&p.Value.ValueKind==JsonValueKind.Object)foreach(var d in p.Value.EnumerateObject())if(catalog.ContainsKey(d.Name))names.Add(d.Name);
                if(p.Name is "tool" or "Tool"&&p.Value.ValueKind==JsonValueKind.String&&catalog.ContainsKey(p.Value.GetString()!))names.Add(p.Value.GetString()!);
                if(p.Name is "goal"&&p.Value.ValueKind==JsonValueKind.String) {
                    string goal=p.Value.GetString()!;
                    if(goal is "plant" or "water" or "harvest" or "cleanup")Group("farm");
                    if(goal is "fish" or "mine_trip" or "volcano_trip")Group("exploration");
                    if(goal is "store" or "withdraw" or "storage_expand")Group("storage_production");
                    if(goal is "milk" or "shear" or "feed" or "pet" or "animal_collect")Group("animals");
                }
                Scan(p.Value);
            }
        }
        foreach(string field in new[]{"operating_candidates","schedule","task_card","goals","progression","pending_queries"})if(context.TryGetProperty(field,out var node))Scan(node);
        if(context.TryGetProperty("equipped_tools",out var equipped)&&equipped.ValueKind==JsonValueKind.Array)foreach(var name in equipped.EnumerateArray())if(name.ValueKind==JsonValueKind.String)names.Add(name.GetString()!);
        if(context.TryGetProperty("now",out var now)) {
            if(now.TryGetProperty("day",out var day)&&day.GetInt32()==0)names.Add("player.collect_home_gifts");
            if(now.TryGetProperty("location",out var location)&&location.GetString() is {} place&&place.Contains("Shop",StringComparison.OrdinalIgnoreCase))Group("trade");
        }
        if(context.TryGetProperty("ui",out var ui)&&ui.TryGetProperty("type",out var type)&&type.GetString() is not ("none" or "executor_owned_fishing"))Group("menu");
        if(context.TryGetProperty("planting_execution",out var planting)&&planting.TryGetProperty("unplanted_owned_seeds",out var seeds)&&seeds.ValueKind==JsonValueKind.Array&&seeds.GetArrayLength()>0)Group("farm");
        // Ordering is deterministic: changing state only changes the relevant suffix.
        return CoreNames.Concat(names.Except(CoreNames).OrderBy(n=>n,StringComparer.Ordinal)).Where(catalog.ContainsKey).ToDictionary(n=>n,n=>catalog[n]);
    }
    public static Dictionary<string,string> Core(IReadOnlyDictionary<string,string> catalog) {
        var result=CoreNames.Where(catalog.ContainsKey).ToDictionary(k=>k,k=>catalog[k]);
        return result;
    }
    public static object Lookup(IReadOnlyDictionary<string,string> catalog,JsonElement args) {
        var names=args.TryGetProperty("names",out var raw)?raw.Deserialize<string[]>()??Array.Empty<string>():Array.Empty<string>();
        if(names.Length>12||names.Any(n=>n==null))throw new InvalidOperationException("lookup_requires_at_most_12_names");
        string query=args.TryGetProperty("query",out var q)?q.GetString()??"":"";
        string group=args.TryGetProperty("group",out var g)?g.GetString()??"":"";
        if(group.Length>0){if(!Groups.TryGetValue(group,out var members))throw new InvalidOperationException("unknown_tool_group");names=names.Concat(members).Distinct().ToArray();}
        if(query.Length>100)throw new InvalidOperationException("lookup_query_too_long");
        var keys=names.Length>0?names.Distinct().ToArray():catalog.Keys.Where(k=>query.Length>0&&(k.Contains(query,StringComparison.OrdinalIgnoreCase)||catalog[k].Contains(query,StringComparison.OrdinalIgnoreCase))).OrderBy(k=>k).Take(12).ToArray();
        var unknown=keys.Where(k=>!catalog.ContainsKey(k)).ToArray();
        var workProfiles=ToolSpecs.LookupProfiles(args);
        bool fallback=!keys.Any(catalog.ContainsKey)&&workProfiles.Length==0;
        if(fallback&&(query.Length>0||names.Length>0)) {
            string text=(query+" "+string.Join(" ",names)).ToLowerInvariant();
            var synonyms=new Dictionary<string,string>{{"social","社交"},{"greet","社交"},{"talk","社交"},{"gift","送礼"},{"seed","种子"},{"buy","采购"},{"shop","商店"},{"fish","钓鱼"},{"recipe","配方"},{"craft","制作"},{"quest","任务"},{"sleep","睡觉"},{"wood","木材"},{"storage","存货"}};
            var terms=text.Split(new[]{' ','.','_',',','，','/'},StringSplitOptions.RemoveEmptyEntries).Concat(synonyms.Where(kv=>text.Contains(kv.Key)).Select(kv=>kv.Value)).Distinct().ToArray();
            keys=catalog.Select(kv=>new{kv.Key,Score=terms.Count(t=>kv.Key.Contains(t,StringComparison.OrdinalIgnoreCase)||kv.Value.Contains(t,StringComparison.OrdinalIgnoreCase))}).Where(r=>r.Score>0).OrderByDescending(r=>r.Score).ThenBy(r=>r.Key).Take(8).Select(r=>r.Key).ToArray();
            if(keys.Length==0)keys=new[]{"knowledge.search","world.read","plan.read","work.run"}.Where(catalog.ContainsKey).ToArray();
        }
        if(workProfiles.Length>0)keys=keys.Append("work.run").Distinct().ToArray();
        return new{definitions=keys.Where(catalog.ContainsKey).ToDictionary(k=>k,k=>catalog[k]),work_profiles=workProfiles,unknown,fallback,index=query.Length==0&&names.Length==0&&workProfiles.Length==0?catalog.Keys.OrderBy(k=>k).ToArray():null,note=fallback?"未精确匹配，已返回相近能力或世界/百科入口；不要重试同一不存在的工具名。":"完整参数定义；查询不执行动作。"};
    }
}
