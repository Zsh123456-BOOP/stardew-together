using StardewValley;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Together;

public sealed class KnowledgeService {
    private readonly ModEntry mod;
    public KnowledgeIndex Index {get;private set;}=new(Array.Empty<KnowledgeEntry>());
    private IEnumerator<KnowledgeEntry>? loader;
    private readonly List<KnowledgeEntry> loading=new();
    private bool dirty=true;
    private string language="";
    public string Status {get;private set;}="正在准备资料…";
    public bool Ready=>!dirty && loader==null;
    public int Revision {get;private set;}
    public KnowledgeService(ModEntry mod){this.mod=mod;}
    public void Invalidate(){dirty=true;loader?.Dispose();loader=null;Index=new(Array.Empty<KnowledgeEntry>());Revision++;}
    public void Tick() {
        string current=LocalizedContentManager.CurrentLanguageCode.ToString();if(current!=language){language=current;Invalidate();}
        if(!dirty)return;
        try {
            if(loader==null){loading.Clear();loader=KnowledgeCatalog.Read().GetEnumerator();}
            long start=System.Diagnostics.Stopwatch.GetTimestamp();
            for(int n=0;n<80;n++) {
                if(!loader.MoveNext()){Index=new(loading);loader.Dispose();loader=null;dirty=false;Status=$"已载入 {Index.Entries.Count} 条资料";Revision++;return;}
                loading.Add(loader.Current);
                if((System.Diagnostics.Stopwatch.GetTimestamp()-start)*1000.0/System.Diagnostics.Stopwatch.Frequency>3)break;
            }
            Status=$"正在准备资料：{loading.Count}";
        }catch(Exception e){loader?.Dispose();loader=null;dirty=false;Index=new(loading);Status="部分资料读取失败："+e.GetType().Name;Revision++;}
    }
    public void Observe() {
        var b=mod.Data.Knowledge;var p=Game1.player;
        b.Visited.Add(Game1.currentLocation.NameOrUniqueName);
        foreach(var s in mod.Facts.Stock)b.Discovered.Add(s.Item);
        foreach(var key in p.basicShipped.Keys)b.Discovered.Add(ItemRegistry.QualifyItemId(key)??key);
        foreach(var key in p.fishCaught.Keys)b.Discovered.Add(ItemRegistry.QualifyItemId(key)??key);
        foreach(var obj in Game1.currentLocation.objects.Values)b.Discovered.Add(obj.QualifiedItemId);
        foreach(var feature in Game1.currentLocation.terrainFeatures.Values)if(feature is HoeDirt {crop:{} crop})b.Discovered.Add(ItemRegistry.QualifyItemId(crop.indexOfHarvest.Value)??crop.indexOfHarvest.Value);
    }
    public bool Visible(KnowledgeEntry e) {
        var b=mod.Data.Knowledge;if(!b.DiscoveredOnly || e.Kind=="guide")return true;
        if(e.Id.StartsWith("npc:"))return Game1.player.friendshipData.ContainsKey(e.Id[4..]);
        if(e.Id.StartsWith("location:"))return b.Visited.Contains(e.Id[9..]);
        if(e.Id.StartsWith("craft:"))return Game1.player.craftingRecipes.ContainsKey(e.Id[6..]);
        if(e.Id.StartsWith("cooking:"))return Game1.player.cookingRecipes.ContainsKey(e.Id[8..]);
        return b.Discovered.Contains(e.Id) || mod.Facts.Bundles.Any(g=>g.Missing.Any(n=>n.Item==e.Id)) || mod.Facts.Goals.Any(g=>g.Needs.Any(n=>n.Item==e.Id));
    }
    public IReadOnlyList<KnowledgeHit> Search(string query,string kind="all",int limit=12)=>Index.Search(query,Visible,kind,limit);
    public KnowledgePacket Query(string query,string? id=null) {
        mod.RefreshKnowledgeFacts();
        var packet=new KnowledgePacket{Query=query,Observed=$"第{Game1.year}年 {KnowledgeRules.Season(Game1.currentSeason)}{Game1.dayOfMonth}日 {Game1.timeOfDay/100}:{Game1.timeOfDay%100:00}",Save=Game1.uniqueIDForThisGame+":"+Game1.player.UniqueMultiplayerID};
        if(!Ready){packet.Status="loading";packet.Facts.Add(new("catalog","资料状态",Status,"Together 索引"));return packet;}
        var hits=id!=null?Index.Get(id) is {} entry && Visible(entry)?new[]{new KnowledgeHit(entry,1000,"选中条目")}:Array.Empty<KnowledgeHit>():Search(query,limit:8).ToArray();
        if(hits.Length==0){packet.Status="not_found";packet.Facts.Add(new("missing","没有匹配资料","可尝试具体名称、别名或类型；随探索模式会隐藏未发现条目。","检索结果"));return packet;}
        foreach(var h in hits)packet.Candidates.Add(h.Entry.Name+" ["+h.Entry.Id+"] · "+h.Reason);
        // Tied exact names and all fuzzy guesses require a user choice; never silently bind an entity.
        if(id==null && ((hits[0].Score>=400 && hits[0].Score<650) || (hits.Length>1 && hits[0].Score==hits[1].Score && hits[0].Score>=700))) {
            packet.Status="ambiguous";packet.Facts.Add(new("choices","请先选择",string.Join("；",packet.Candidates),"名称匹配"));return packet;
        }
        foreach(var h in hits.Take(id==null?3:1))Append(packet,h.Entry);
        if(query.Contains("比较") || query.Contains("对比"))packet.Facts.Add(new("comparison","比较口径","以上基础售价不是净利润；种子售价、品质、职业与劳务未统一，不据此声称某项最赚钱。","Together 比较边界","partial"));
        return packet;
    }
    private void Append(KnowledgePacket p,KnowledgeEntry e) {
        p.EntityIds.Add(e.Id);int counter=0;
        void Add(string label,string value,string source="",string support="verified") {if(!string.IsNullOrWhiteSpace(value))p.Facts.Add(new(e.Id+":"+(counter++),label,value,source.Length==0?e.Source:source,support));}
        Add(e.Name,e.Description);
        foreach(var field in e.Fields)Add(field.Key,field.Value);
        if(e.Id.StartsWith("(")) {
            var stocks=mod.Facts.Stock.Where(s=>s.Item==e.Id).ToArray();
            int owned=stocks.Sum(s=>s.Count),reserved=mod.Data.Reservations.GetValueOrDefault(e.Id);
            Add("我们的物资",$"已观察到 {owned} 件；计划预留 {reserved} 件。"+string.Join("；",stocks.Take(8).Select(s=>$"{s.Location}：{s.Count} 件，品质 {s.Quality}"))+"。范围：玩家、队友物资及农场/建筑内已读取容器，未覆盖地点不等于没有。","WorldReader 实时库存");
            var needs=mod.Facts.Bundles.Where(b=>!b.Complete).SelectMany(b=>b.Missing.Where(n=>n.Item==e.Id).Select(n=>$"{b.Name}：需求 {n.Count}，最低品质 {n.Quality}，合格库存 {n.Owned}（交付前需核对槽位）"));
            Add("献祭用途",string.Join("；",needs),"原生献祭槽位");
            var related=Index.Entries.Where(r=>r.Kind=="recipe" && r.Links.Contains(e.Id)&&Visible(r)).Take(8).ToArray();
            if(related.Length>0)Add("已知配方用途",string.Join("、",related.Select(r=>r.Name)),"当前已知配方");
        }
        if(e.Kind=="crop")Crop(e,Add);
        if(e.Kind=="fish")Fish(e,Add);
        if(e.Kind=="recipe")Recipe(e,Add);
        if(e.Kind=="npc")Npc(e,Add);
        if(e.Kind=="machine")Machine(e,Add);
        if(e.Kind=="location")Add("探索记录",mod.Data.Knowledge.Visited.Contains(e.Id[9..])?"本手册记录你到过这里。":"本手册尚无到访记录；解锁和路线未核实。","共同手册记录");
        if(e.Id=="guide:calendar")foreach(var row in Calendar())Add("日历",row,"运行时日历与农场实例");
        if(e.Id=="guide:bundle") {
            foreach(var b in mod.Facts.Bundles.Where(b=>!b.Complete).Take(8))Add(b.Name,string.Join("；",b.Missing.Select(n=>$"{n.Name} 需 {n.Count} / 合格库存 {n.Owned} / 品质≥{n.Quality}"))+ $"。已完成 {b.CompletedSlots}/{b.RequiredSlots} 槽；缺失项可能可选，不代表全要提交。","原生献祭状态");
            foreach(var g in mod.Facts.Goals.Where(g=>!g.Complete && !g.Id.StartsWith("craft:")).Take(6))Add(g.Title,g.PlayerStep+(g.Deadline>=0?$" 剩余 {Math.Max(0,g.Deadline-mod.Facts.Day)} 天。":""),"原生任务状态");
            Add("材料分工",string.Join("；",mod.Data.Today.Where(n=>n.Count>0).Take(8).Select(n=>n.Title+"："+n.Count+"；"+n.Reason)),"Together 当前经营计划");
        }
        if(e.Id=="guide:growth")foreach(string row in FarmRows())Add("农场诊断",row,"当前作物实例");
        if(e.Id=="guide:machine")Add("当前机器",$"有 {mod.Facts.MachinesReady} 台可收取；打开对应机器条目查看加工中产物。","WorldReader");
        if(e.Id=="guide:fishing" || e.Id=="guide:bundle" && p.Query.Contains("鱼")) {
            foreach(var f in mod.Facts.Bundles.Where(b=>!b.Complete).SelectMany(b=>b.Missing).Select(n=>Index.Get(n.Item)).Where(f=>f?.Kind=="fish" && Visible(f)).DistinctBy(f=>f!.Id).Take(4))Fish(f!,Add);
        }
        if(mod.Data.Knowledge.Notes.TryGetValue(e.Id,out var note))Add("你的便签",note,"玩家手写便签（不是游戏事实）");
        var memories=MemoryRecall.Select(mod.Current.Life.Experiences,e.Name,Game1.Date.TotalDays);
        Add("相处记录",string.Join("；",memories.Take(2).Select(m=>m.Summary)),"真实伙伴行动记录，不证明玩家亲自完成");
    }
    private void Crop(KnowledgeEntry e,Action<string,string,string,string> add) {
        if(!DataLoader.Crops(Game1.content).TryGetValue(e.Id[3..],out var c))return;
        int days=c.DaysInPhase.Sum(),date=Game1.dayOfMonth+days;
        bool season=c.Seasons.Any(s=>s.ToString().Equals(Game1.currentSeason,StringComparison.OrdinalIgnoreCase));
        string text=!season?"当前农场季节不在该作物的生长季节中。":date<=28?$"今天播种、每天正常生长，按基础阶段预计本季 {date} 日首次成熟。":$"基础生长期 {days} 天，本季剩余 {28-Game1.dayOfMonth} 个过夜生长机会；需核对跨季存活或加速条件。";
        add("播种日历",text+" 这里按无加速、露天普通土壤计算；肥料、职业、水稻、温室和自定义规则不包含在此预测。","Data/Crops + 当前日期","partial");
        foreach(string row in FarmRows(c.HarvestItemId))add("已种作物",row,"实例阶段（已含种下时的生长调整）","verified");
    }
    public IEnumerable<string> FarmRows(string? harvest=null) {
        var locations=new[]{Game1.getFarm() as GameLocation}.Concat(Game1.getFarm().buildings.Select(b=>b.GetIndoors()).Where(l=>l!=null));int count=0;
        foreach(var l in locations)foreach(var pair in l.terrainFeatures.Pairs)if(pair.Value is HoeDirt {crop:{} c} d) {
            if(harvest!=null && c.indexOfHarvest.Value!=harvest)continue;
            string name=KnowledgeCatalog.ItemName(ItemRegistry.QualifyItemId(c.indexOfHarvest.Value)??c.indexOfHarvest.Value);
            int remaining=c.fullyGrown.Value?Math.Max(0,c.dayOfCurrentPhase.Value):c.phaseDays.Skip(c.currentPhase.Value).Where(n=>n<99999).Sum()-c.dayOfCurrentPhase.Value;
            string state=c.dead.Value?"已枯死":d.readyForHarvest()?"可以收获":$"按当前阶段还需约 {Math.Max(0,remaining)} 个正常生长夜晚";
            yield return $"{l.DisplayName} ({pair.Key.X},{pair.Key.Y}) {name}：{state}；"+(d.state.Value==HoeDirt.dry?"土壤未浇水。":"土壤有水。")+"未预测后续断水或季节死亡。";
            if(++count>=12)yield break;
        }
    }
    private void Recipe(KnowledgeEntry e,Action<string,string,string,string> add) {
        bool cook=e.Id.StartsWith("cooking:");string key=e.Id[(cook?8:6)..];var r=new CraftingRecipe(key,cook);
        foreach(var need in r.recipeList) {
            string id=ItemRegistry.QualifyItemId(need.Key)??need.Key;
            var stocks=mod.Facts.Stock.Where(s=>s.Item==id || int.TryParse(need.Key,out int cat)&&cat<0&&s.Category==cat);
            int owned=stocks.Sum(s=>s.Count);
            int reserved=stocks.Select(s=>s.Item).Distinct().Sum(i=>mod.Data.Reservations.GetValueOrDefault(i));
            add(KnowledgeCatalog.IngredientName(need.Key),$"需要 {need.Value}，已观察库存 {owned}，相关预留 {reserved}；忽略预留的缺口 {Math.Max(0,need.Value-owned)}。","配方 + 实时库存","verified");
        }
        add("制作边界","材料列表不代表可直接制作；还需玩家所在界面可用库存及原生制作条件。伙伴可备料，实际制作由玩家完成。","Together 能力边界","verified");
    }
    private void Npc(KnowledgeEntry e,Action<string,string,string,string> add) {
        string name=e.Id[4..];var npc=Game1.getCharacterFromName(name);if(npc==null)return;
        if(Game1.player.friendshipData.TryGetValue(name,out var f))add("我们的关系",$"原生好感 {f.Points}；本周赠礼 {f.GiftsThisWeek}，今日 {f.GiftsToday}。关系状态：{f.Status}","当前玩家 friendshipData","verified");
        var gifts=new List<string>();
        foreach(var s in mod.Facts.Stock.DistinctBy(s=>s.Item).Take(120)) {
            if(!s.Item.StartsWith("(O)"))continue;
            if(mod.Data.Knowledge.DiscoveredOnly && !Game1.player.hasGiftTasteBeenRevealed(npc,s.Item[3..]))continue;
            var item=ItemRegistry.Create(s.Item);int taste=npc.getGiftTasteForThisItem(item);
            if(taste is 0 or 2)gifts.Add(s.Name+(taste==0?"（最爱）":"（喜欢）")+(mod.Data.Reservations.GetValueOrDefault(s.Item)>0?"，已有计划预留":""));
        }
        add("手头礼物",gifts.Count==0?"已观察库存中没有已知喜欢的礼物；未知偏好不猜测。":string.Join("、",gifts.Take(10)),"游戏礼物判断 + 偏好发现记录","verified");
    }
    private void Machine(KnowledgeEntry e,Action<string,string,string,string> add) {
        int count=0;
        foreach(var l in new[]{Game1.getFarm() as GameLocation}.Concat(Game1.getFarm().buildings.Select(b=>b.GetIndoors()).Where(l=>l!=null)))
            foreach(var pair in l.objects.Pairs)if(pair.Value.QualifiedItemId==e.Id) {
                var m=pair.Value;string state=m.readyForHarvest.Value?"可以收取":m.heldObject.Value!=null?$"加工中，游戏计时剩余 {m.MinutesUntilReady} 分钟":"空闲；投料条件未判定";
                add("机器实例",$"{l.DisplayName} ({pair.Key.X},{pair.Key.Y})：{state}","原生机器实例","verified");if(++count==8)return;
            }
        if(count==0)add("机器实例","本次农场/建筑范围未观察到该设施。","WorldReader 范围","verified");
    }
    public List<string> Calendar() {
        var rows=new List<string>{$"今天：{KnowledgeRules.Season(Game1.currentSeason)}{Game1.dayOfMonth}日；当前地点{(Game1.currentLocation.IsRainingHere()?"下雨":"无雨")}。明日农场预报：{Weather(Game1.weatherForTomorrow)}。"};
        foreach(var pair in Game1.characterData.Where(p=>p.Value.BirthSeason?.ToString().ToLowerInvariant()==Game1.currentSeason && p.Value.BirthDay>=Game1.dayOfMonth).OrderBy(p=>p.Value.BirthDay))
            if(Index.Get("npc:"+pair.Key) is {} e && Visible(e))rows.Add(e.Name+"生日："+pair.Value.BirthDay+"日");
        foreach(var f in DataLoader.Festivals_FestivalDates(Game1.content).Where(f=>f.Key.StartsWith(Game1.currentSeason)))rows.Add("节日："+f.Value+"（"+f.Key.Replace(Game1.currentSeason,KnowledgeRules.Season(Game1.currentSeason))+"）");
        rows.Add($"农场：待浇 {mod.Facts.DryCrops} 株、成熟 {mod.Facts.RipeCrops} 株；待抚摸 {mod.Facts.AnimalsUnpetted} 只、食槽缺草 {mod.Facts.FeedNeeded} 份；机器可收 {mod.Facts.MachinesReady} 台。");
        rows.AddRange(FarmRows().Take(6));return rows;
    }
    private static string Weather(string value)=>value switch {"Sun"=>"晴","Rain"=>"雨","Wind"=>"风","Storm"=>"雷雨","Festival"=>"节日天气","Snow"=>"雪","Wedding"=>"婚礼天气","GreenRain"=>"绿雨",_=>"未适配的天气类型"};
    private void Fish(KnowledgeEntry e,Action<string,string,string,string> add) {
        if(!DataLoader.Fish(Game1.content).TryGetValue(e.Id[3..],out var raw))return;
        var parts=raw.Split('/');if(KnowledgeRules.Field(parts,1)=="trap"){add(e.Name+"捕获","使用蟹笼；这里不按钓竿时段判断。","Data/Fish","verified");return;}
        var rod=Game1.player.CurrentTool as FishingRod;bool magic=rod?.HasMagicBait()==true;
        int rows=0;
        foreach(var pair in Game1.locationData.Where(p=>p.Key!="Default")) {
            if(Index.Get("location:"+pair.Key) is {} le && !Visible(le))continue;
            var rules=(pair.Value.Fish??new()).Concat(Game1.locationData.GetValueOrDefault("Default")?.Fish??new()).Where(s=>(ItemRegistry.QualifyItemId(s.ItemId??"")??s.ItemId)==e.Id);
            foreach(var rule in rules) {
                var missing=new List<string>();var unknown=new List<string>();
                var location=Game1.getLocationFromName(pair.Key);string season=location==null?Game1.currentSeason:Game1.GetSeasonForLocation(location).ToString().ToLowerInvariant();
                if(!magic && rule.Season.HasValue && rule.Season.Value.ToString().ToLowerInvariant()!=season)missing.Add("季节不符");
                if(Game1.player.FishingLevel<rule.MinFishingLevel)missing.Add("钓鱼等级不足");
                if(rule.RequireMagicBait&&!magic)missing.Add("需要魔法鱼饵");
                if(rule.Chance<=0)missing.Add("基础出现概率为零（特殊修正未计算）");
                if(rule.CatchLimit>=0 && Game1.player.fishCaught.TryGetValue(e.Id,out var caught)&&caught[0]>=rule.CatchLimit)missing.Add("已达捕获上限");
                if(!rule.IgnoreFishDataRequirements) {
                    if(!magic && !KnowledgeRules.InTime(KnowledgeRules.Field(parts,5),Game1.timeOfDay))missing.Add("当前时段不符");
                    string weather=KnowledgeRules.Field(parts,7);
                    if(!magic && weather is "rainy" or "sunny") {
                        if(location==null)unknown.Add("目标地图天气未知");else if((weather=="rainy")!=location.IsRainingHere())missing.Add("目标地点天气不符");
                    }
                    if(Game1.player.FishingLevel<KnowledgeRules.Integer(KnowledgeRules.Field(parts,12)))missing.Add("鱼种等级门槛不足");
                }
                if(rod?.QualifiedItemId=="(T)TrainingRod" && (rule.CanUseTrainingRod==false || rule.CanUseTrainingRod==null && KnowledgeRules.Integer(KnowledgeRules.Field(parts,1))>=50))missing.Add("训练鱼竿不适用");
                if(!string.IsNullOrEmpty(rule.Condition))unknown.Add("额外解锁/条件尚未计算");
                if(rule.FishAreaId!=null)unknown.Add("需指定水域 "+rule.FishAreaId);
                if(rule.PlayerPosition.HasValue || rule.BobberPosition.HasValue)unknown.Add("需特定站位/落点");
                if(rule.MinDistanceFromShore>0 || rule.MaxDistanceFromShore>=0)unknown.Add("需核对抛竿水深");
                if(rod==null)unknown.Add("尚未选中鱼竿，装备条件未确认");
                string result=missing.Count>0?"当前不符合："+string.Join("、",missing):"基础季节/时段/天气/等级检查通过；仍不保证能捕获";
                if(unknown.Count>0)result+="；"+string.Join("、",unknown);
                add(e.Name+" · "+(location?.DisplayName??pair.Key),result+"。特殊地点覆盖、自定义算法与随机概率未模拟。","Data/Locations + Data/Fish + 实时玩家状态","partial");
                if(++rows>=10)return;
            }
        }
        if(rows==0)add(e.Name+" · 地点","当前可见地图中未找到静态鱼种规则；动态查询、未探索地点及特殊地点需要单独适配。","运行时鱼类规则","partial");
    }
}
