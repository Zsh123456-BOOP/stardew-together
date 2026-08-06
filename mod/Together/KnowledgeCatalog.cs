using StardewValley;

namespace Together;

public static class KnowledgeCatalog {
    public static string ItemName(string id)=>ItemRegistry.GetDataOrErrorItem(id).DisplayName;
    // Enumerated in bounded batches on the game thread. Patched content is the source of truth.
    public static IEnumerable<KnowledgeEntry> Read() {
        var fish=DataLoader.Fish(Game1.content);
        var crops=DataLoader.Crops(Game1.content);
        foreach(var pair in Game1.objectData) {
            string id="(O)"+pair.Key;var data=ItemRegistry.GetDataOrErrorItem(id);
            var e=new KnowledgeEntry{Id=id,Name=data.DisplayName,Kind=fish.ContainsKey(pair.Key)?"fish":"item",Description=data.Description??"",Source="游戏 Data/Objects",Aliases=new(){pair.Value.Name},Tags=new(){"物品"}};
            e.Fields["基础售价"]=pair.Value.Price+" 金（未计品质与职业，非商店购买价）";
            e.Fields["类别"]=pair.Value.Category.ToString();
            if(fish.TryGetValue(pair.Key,out var raw)) {
                var f=raw.Split('/');e.Tags.AddRange(new[]{"鱼","钓鱼","fish"});e.Source+=" + Data/Fish";
                bool trap=KnowledgeRules.Field(f,1)=="trap";
                e.Fields["捕获方式"]=trap?"蟹笼":"钓竿";
                if(!trap){e.Fields["时段"]=KnowledgeRules.Field(f,5);e.Fields["天气"]=KnowledgeRules.Field(f,7);e.Fields["最低钓鱼等级"]=KnowledgeRules.Field(f,12);}
            }
            if(crops.TryGetValue(pair.Key,out var crop)) {
                e.Kind="crop";e.Tags.AddRange(new[]{"种子","种植","作物","农场","成熟","seed"});e.Source+=" + Data/Crops";
                e.Fields["生长季节"]=string.Join("、",crop.Seasons.Select(s=>KnowledgeRules.Season(s.ToString())));
                e.Fields["基础生长期"]=crop.DaysInPhase.Sum()+" 天";
                e.Fields["复收间隔"]=crop.RegrowDays>0?crop.RegrowDays+" 天":"一次性收获";
                string harvest=ItemRegistry.QualifyItemId(crop.HarvestItemId)??crop.HarvestItemId;
                e.Links.Add(harvest);e.Fields["收获"]=ItemName(harvest);
                e.Tags.Add(e.Fields["生长季节"]);
            }
            if(pair.Key=="472")e.Aliases.AddRange(new[]{"防风草种子","欧防风种子"});
            if(pair.Key=="24")e.Aliases.AddRange(new[]{"防风草","欧防风"});
            yield return e;
        }
        foreach(var pair in DataLoader.BigCraftables(Game1.content)) {
            string id="(BC)"+pair.Key;var data=ItemRegistry.GetDataOrErrorItem(id);
            yield return new(){Id=id,Kind="machine",Name=data.DisplayName,Description=data.Description??"",Aliases=new(){pair.Value.Name},Tags=new(){"机器","设施","加工"},Source="游戏 Data/BigCraftables + Data/Machines"};
        }
        foreach(bool cooking in new[]{false,true}) {
            var recipes=cooking?DataLoader.CookingRecipes(Game1.content):DataLoader.CraftingRecipes(Game1.content);
            foreach(var pair in recipes) {
                var recipe=new CraftingRecipe(pair.Key,cooking);
                var e=new KnowledgeEntry{Id=(cooking?"cooking:":"craft:")+pair.Key,Kind="recipe",Name=recipe.DisplayName,Aliases=new(){pair.Key},Tags=new(){"配方",cooking?"烹饪":"制作","材料"},Source=cooking?"游戏 Data/CookingRecipes":"游戏 Data/CraftingRecipes"};
                e.Description="材料："+string.Join("、",recipe.recipeList.Select(p=>IngredientName(p.Key)+" ×"+p.Value));
                foreach(var p in recipe.recipeList)if(!int.TryParse(p.Key,out int category)||category>=0)e.Links.Add(ItemRegistry.QualifyItemId(p.Key)??p.Key);
                yield return e;
            }
        }
        foreach(var pair in Game1.characterData) {
            var npc=Game1.getCharacterFromName(pair.Key);if(npc==null || npc.IsMonster)continue;
            var e=new KnowledgeEntry{Id="npc:"+pair.Key,Kind="npc",Name=npc.displayName,Aliases=new(){pair.Key},Tags=new(){"人物","生日","礼物","好感"},Source="游戏 Data/Characters + 当前人物"};
            if(pair.Value.BirthSeason.HasValue && pair.Value.BirthDay>0)e.Fields["生日"]=KnowledgeRules.Season(pair.Value.BirthSeason.Value.ToString())+pair.Value.BirthDay+"日";
            e.Description="关系与已知礼物偏好从当前玩家存档读取。";yield return e;
        }
        foreach(var pair in Game1.locationData.Where(p=>p.Key!="Default")) {
            var location=Game1.getLocationFromName(pair.Key);
            yield return new(){Id="location:"+pair.Key,Kind="location",Name=location?.DisplayName??pair.Key,Aliases=new(){pair.Key},Description="地图资料；是否能前往仍取决于当前解锁条件和实际出口。",Tags=new(){"地图","地点"},Source="游戏 Data/Locations"};
        }
        foreach(var e in Guides())yield return e;
    }
    public static string IngredientName(string id)=>int.TryParse(id,out int n)&&n<0?$"类别 {id} 的任意合格材料":ItemName(ItemRegistry.QualifyItemId(id)??id);
    private static IEnumerable<KnowledgeEntry> Guides() {
        yield return Guide("growth","作物与树木为什么不长","作物需检查死亡、季节、浇水与阶段；果树还需检查周围障碍。下方实时诊断只覆盖农场作物，树木特殊规则尚不自动判定。","生长","不长","树","浇水","季末");
        yield return Guide("machine","机器为什么不工作","先看是否已有产物、是否还在加工，再检查原料、燃料与机器条件。百科不会试投材料；自定义机器只展示能读取的规则。","机器","不工作","加工","原料","酿酒");
        yield return Guide("fishing","钓鱼条件与概率","可捕获条件与钓获概率不同。地图、季节、时间、天气、技能、装备及特殊解锁都可能影响结果。手册不调用实际钓鱼过程，不保证下一竿钓到。","钓鱼","钓不到","鱼饵","鱼竿");
        yield return Guide("bundle","献祭与任务材料","库存足够不等于已经交付。质量、数量、替代槽位和原生计数都要核对；同一件物品不能同时满足多项预留。特殊交付仍由玩家完成。","献祭","任务","缺什么","社区中心","材料","成就");
        yield return Guide("calendar","农事与日历","日历结合当前季节、已认识人物生日、节日、作物实际阶段和库存。未来天气只显示游戏已提供的预报，不预测全年。","日历","今天","安排","生日","节日","天气","种什么");
    }
    private static KnowledgeEntry Guide(string id,string name,string description,params string[] tags)=>new(){Id="guide:"+id,Kind="guide",Name=name,Description=description,Tags=tags.ToList(),Source="Together 自编机制说明；具体状态见实时证据"};
}
