using System.Text.Json;
using System.Text.Json.Nodes;
namespace Together;

// Frozen per request: generation and validation consume these same detached schemas.
public sealed record ToolSpec(string Name,string Description,JsonElement Parameters,string[] Profiles);
public static class ToolSpecs {
    public static readonly Dictionary<string,string[]> WorkProfiles=new(){
        ["farm"]=new[]{"plant","water","refill","harvest","clear_dead","cleanup"},
        ["resource"]=new[]{"resource","hardwood","stone","wood","fiber","forage"},
        ["storage"]=new[]{"store","withdraw","storage_expand"},["fish"]=new[]{"fish"},["mine"]=new[]{"mine_trip","volcano_trip"},
        ["animals"]=new[]{"milk","shear","animal_collect","pet","feed"},["production"]=new[]{"tend","collect","process"}
    };
    public static readonly HashSet<string> WorkFields=new("actor_id goal item quality plan_id cleanup_id goal_id target_level start_level region travel_budget keep_gold include_trees required_free_slots max_food location count stock_target reserve_stamina until additional exact_quality order_id objective quest_id".Split(' '));
    private static JsonObject Field(string type,string? description=null)=>description==null?new(){["type"]=type}:new(){["type"]=type,["description"]=description};
    private static JsonObject Enum(params string[] values)=>new(){["type"]="string",["enum"]=JsonSerializer.SerializeToNode(values)};
    public static ToolSpec[] Select(IReadOnlyDictionary<string,string> selected,JsonElement context)=>selected.Select(p=>p.Key=="work.run"?Work(context):NativeToolProtocol.LegacySpec(p.Key,p.Value)).ToArray();
    public static string Contract() {
        var spec=Work(JsonSerializer.SerializeToElement(new{active_actors=new[]{"player","companion"},work_profiles=WorkProfiles.Keys}));
        string Hint(JsonProperty p)=>p.Name=="actor_id"?"string":p.Value.TryGetProperty("enum",out var e)?string.Join("|",e.EnumerateArray().Select(v=>v.GetString())):p.Value.GetProperty("type").GetString() switch{"integer"=>"int","boolean"=>"bool",_=>"string"};
        return "{"+string.Join(",",spec.Parameters.GetProperty("properties").EnumerateObject().Where(p=>p.Name!="_plan").Select(p=>p.Name+(p.Name=="goal"?"":"?")+":"+Hint(p)))+"}: "+spec.Description;
    }
    public static string[] LookupProfiles(JsonElement args) {
        var result=new HashSet<string>();
        if(args.TryGetProperty("work_profiles",out var profiles))foreach(var p in profiles.EnumerateArray()) {string name=p.GetString()??"";if(!WorkProfiles.ContainsKey(name))throw new InvalidOperationException("unknown_work_profile:"+name);result.Add(name);}
        string group=args.TryGetProperty("group",out var g)?g.GetString()??"":"",query=args.TryGetProperty("query",out var q)?q.GetString()??"":"";
        if(group=="exploration")result.UnionWith(new[]{"fish","mine"});if(group=="animals")result.Add("animals");if(group=="storage_production")result.Add("production");
        foreach(var p in WorkProfiles)if(query.Contains(p.Key,StringComparison.OrdinalIgnoreCase)||p.Value.Any(goal=>query.Contains(goal,StringComparison.OrdinalIgnoreCase)))result.Add(p.Key);
        if(result.Count==0&&args.TryGetProperty("names",out var names)&&names.EnumerateArray().Any(n=>n.GetString()=="work.run"))result.UnionWith(WorkProfiles.Keys);
        return WorkProfiles.Keys.Where(result.Contains).ToArray();
    }
    public static ToolSpec Work(JsonElement context) {
        // Routine work remains directly actionable. Unrelated specialized work is discoverable by profile.
        var profiles=new HashSet<string>{"farm","resource","storage"};
        void Scan(JsonElement n) {
            if(n.ValueKind==JsonValueKind.Array){foreach(var v in n.EnumerateArray())Scan(v);return;}
            if(n.ValueKind!=JsonValueKind.Object)return;
            foreach(var p in n.EnumerateObject()) {
                if(p.Name=="work_profiles"&&p.Value.ValueKind==JsonValueKind.Array)foreach(var v in p.Value.EnumerateArray())if(v.ValueKind==JsonValueKind.String&&WorkProfiles.ContainsKey(v.GetString()!))profiles.Add(v.GetString()!);
                if(p.Name=="goal"&&p.Value.ValueKind==JsonValueKind.String)foreach(var group in WorkProfiles)if(group.Value.Contains(p.Value.GetString()))profiles.Add(group.Key);
                Scan(p.Value);
            }
        }
        foreach(string field in new[]{"schedule","task_card","goals","operating_candidates","pending_queries","work_profiles","active_work"})if(context.TryGetProperty(field,out var value)){if(field=="work_profiles")Scan(JsonSerializer.SerializeToElement(new{work_profiles=value}));else Scan(value);}
        string[] actors=context.TryGetProperty("active_actors",out var active)&&active.ValueKind==JsonValueKind.Array?active.EnumerateArray().Where(a=>a.ValueKind==JsonValueKind.String).Select(a=>a.GetString()!).ToArray():new[]{"player"};
        if(actors.Length==0)actors=new[]{"player"};if(actors.All(a=>a=="player"))profiles.Remove("production");
        var props=new JsonObject{["actor_id"]=Enum(actors),["goal"]=Enum(WorkProfiles.Where(g=>profiles.Contains(g.Key)).SelectMany(g=>g.Value).ToArray()),["location"]=Field("string"),["count"]=Field("integer"),["item"]=Field("string"),["reserve_stamina"]=Field("integer"),["until"]=Field("integer"),["max_food"]=Field("integer"),["goal_id"]=Field("string"),["_plan"]=Field("string")};
        var descriptions=new List<string>{"持续原生劳动，自动寻路/选工具/补给/卸货/续作；无需坐标。actor_id默认player；location默认当前图，water默认Farm，跨图请明确。count默认0（资源另述），范围0..999；reserve_stamina默认0，0..270；max_food默认3，0..10，仅非预留食物；until默认2400，HHMM，存取货可至2500。0目标仅核验清空后完成；部分完成如实返回数量/stop_reason，条件不明报告阻碍。实际装不下才寻找Farm/畜舍output箱，按storage策略必要时建箱；不因空槽少就卸货，不丢弃物品，额外掉落保留位置续收。"};
        void Add(string name,string type,string? note=null)=>props[name]=Field(type,note);
        foreach(string profile in WorkProfiles.Keys.Where(profiles.Contains))switch(profile) {
            case "farm":Add("plan_id","string");Add("cleanup_id","string");Add("quality","integer");descriptions.Add("plant先farm.plan取得plan_id，含清障/锄地/播种/浇水；cleanup须farm.cleanup的cleanup_id，不重复派已有整理队列。玩家/伙伴cleanup仅授权范围，伙伴只清杂草/树枝/普通石。plant/refill/clear_dead限玩家；浇水自动补水；quality为最低品质。");break;
            case "resource":Add("stock_target","integer");Add("include_trees","boolean");Add("quality","integer");descriptions.Add("木/石/纤维/硬木/矿物count为本次新增；stock_target是全队总库存目标，含货袋，不与count混用。省略或count=0补项目缺口，无缺口新增20；自主备料不需立项。resource需item，hardwood限玩家且受工具等级限制；木材默认可砍成熟无树液器普通树，include_trees=false禁整树。");break;
            case "storage":Add("required_free_slots","integer","0..12，已满足则不移动");Add("additional","boolean");Add("exact_quality","boolean");Add("quality","integer");descriptions.Add("store保留工具/种子/补给，预留材料存共享箱仍保护用途。storage_expand先复用现有容量，明确额外扩建用additional:true；不为存货先扩建。withdraw从共享箱或空闲伙伴货袋取货，quality默认最低，exact_quality=true精确；withdraw/storage_expand限玩家。");break;
            case "fish":descriptions.Add("fish限玩家，item可选目标鱼，省略为任意鱼；count默认3按Farmer实际新增捕获核验，自动选地图/水域/力度及补给卸货，NPC货物不算玩家钓获。");break;
            case "mine":Add("start_level","integer");Add("target_level","integer");props["region"]=Enum("normal","skull");Add("travel_budget","integer");Add("keep_gold","integer");descriptions.Add("mine_trip/volcano_trip限玩家。mine自动原生入矿/电梯/挖石/战斗/补给/返程；region默认normal，target_level默认下一5层（1..120），skull默认25。start_level仅已解锁5倍数；travel_budget授权车票，keep_gold为保留金。volcano自动装水冷却熔岩/踩开关/过层，target_level默认10，1..10，10是Caldera；正常通道，体力/生命/时间不足真实返程。");break;
            case "animals":descriptions.Add("pet/feed玩家与伙伴均可；milk/shear/animal_collect限玩家，自动跨畜舍照料/收地面和自动采集器产品，补给卸货续接。");break;
            case "production":descriptions.Add("tend/collect/process仅伙伴，调用Squad生产控制器；process从原料箱补机器，投料不是成品出炉。伙伴无原生体力条，受能力/货物/时间限制。");break;
        }
        // These IDs bind native objective counters; they are independent of autonomous material budgets.
        Add("order_id","string");Add("objective","integer");Add("quest_id","string");
        descriptions.Add("order_id+objective索引绑定真实订单（排除fail_on_completion）；quest_id绑定玩家采集/钓鱼/讨伐委托，以原生计数达标停止。其他work能力用tools.lookup work_profiles展开。");
        var schema=new JsonObject{["type"]="object",["properties"]=props,["required"]=new JsonArray("goal"),["additionalProperties"]=true};
        return new("work.run",string.Concat(descriptions),JsonSerializer.SerializeToElement(schema),WorkProfiles.Keys.Where(profiles.Contains).ToArray());
    }
    public static void CheckProfile(ToolSpec spec,JsonElement args) {
        if(spec.Name=="context.read") {
            bool one=args.TryGetProperty("section",out _),many=args.TryGetProperty("sections",out var sections);
            if(one==many||many&&(sections.ValueKind!=JsonValueKind.Array||sections.GetArrayLength() is <1 or >4))throw new InvalidOperationException("context_requires_section_or_1_to_4_sections");
            if(many&&spec.Parameters.GetProperty("properties").TryGetProperty("section",out var sectionSchema)&&sectionSchema.TryGetProperty("enum",out var valid))foreach(var section in sections.EnumerateArray())if(!valid.EnumerateArray().Any(v=>v.GetRawText()==section.GetRawText()))throw new InvalidOperationException("unknown_context_section:"+section.GetRawText());
        }
        if(spec.Name!="work.run")return;
        var props=spec.Parameters.GetProperty("properties");
        foreach(var p in args.EnumerateObject())if(WorkFields.Contains(p.Name)&&!props.TryGetProperty(p.Name,out _))throw new InvalidOperationException("work_parameter_profile_not_loaded:"+p.Name+":use_tools.lookup_work_profiles");
        string actor=args.TryGetProperty("actor_id",out var a)?a.GetString()??"player":"player",goal=args.GetProperty("goal").GetString()??"";
        if(actor=="player"&&goal is "tend" or "collect" or "process"||actor!="player"&&goal is "plant" or "refill" or "clear_dead" or "hardwood" or "withdraw" or "storage_expand" or "fish" or "mine_trip" or "volcano_trip" or "milk" or "shear" or "animal_collect")throw new InvalidOperationException("work_goal_actor_not_supported");
    }
}
