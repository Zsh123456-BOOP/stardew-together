using System.Text.Json;
using StardewModdingAPI;
using StardewValley;
using StardewValley.GameData.Machines;

namespace Together;
public sealed partial class ModEntry {
    private Dictionary<string,GoalRecipe> goalRecipes=new();
    private int goalRecipeRevision=-1;
    private string? pendingGoalWork;
    private List<Requirement> AllReservations()=>Data.Projects.Where(p=>p.Status=="active").SelectMany(p=>p.Needs)
        .Concat(Data.SharedGoals.Where(g=>g.Status=="active").SelectMany(g=>g.Reserved)).ToList();
    private void ReadGoalRecipes() {
        if(goalRecipeRevision==Knowledge.Revision)return;
        goalRecipeRevision=Knowledge.Revision;goalRecipes.Clear();
        ReadProcessingRecipes();
        foreach(var pair in DataLoader.CraftingRecipes(Game1.content)) {
            var fields=pair.Value.Split('/');if(fields.Length<5)continue;
            var output=fields[2].Split(' ',StringSplitOptions.RemoveEmptyEntries);
            if(output.Length!=2 || !int.TryParse(output[1],out int quantity) || quantity<1)continue;
            string item=output[0].StartsWith("(")?output[0]:(fields[3]=="true"?"(BC)":"(O)")+output[0];
            if(ItemRegistry.GetDataOrErrorItem(item).IsErrorItem)continue;
            var recipe=new CraftingRecipe(pair.Key,false);
            string unlock=fields[4];var tokens=unlock.Split(' ');
            var skills=new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase){["Farming"]="耕种",["Fishing"]="钓鱼",["Mining"]="采矿",["Foraging"]="采集",["Combat"]="战斗"};
            if(tokens.Length==2 && skills.TryGetValue(tokens[0],out var skill) && int.TryParse(tokens[1],out var level))unlock=$"原生配方条件：{skill} {level} 级；以配方实际进入制作栏为准。";
            else unlock="原生解锁条件代码："+unlock+"；特殊事件或商店条件需在游戏中确认。";
            goalRecipes["craft:"+pair.Key]=new(){Id="craft:"+pair.Key,Item=item,Name=recipe.DisplayName,Output=quantity,Unlock=unlock,
                Source="游戏 Data/CraftingRecipes · "+pair.Key,
                Inputs=recipe.recipeList.Select(p=>new Requirement{Item=ItemRegistry.QualifyItemId(p.Key)??p.Key,Name=KnowledgeCatalog.IngredientName(p.Key),Count=p.Value}).ToList()};
        }
    }
    private void ReadProcessingRecipes() {
        foreach(var pair in DataLoader.Machines(Game1.content)) {
            if(!string.IsNullOrEmpty(pair.Value.InteractMethod))continue;
            foreach(var rule in pair.Value.OutputRules??new()) {
                if(rule.OutputItem?.Count!=1 || rule.Triggers?.Count!=1 || rule.RecalculateOnCollect)continue;
                var output=rule.OutputItem[0];var input=rule.Triggers[0];
                if(input.Trigger!=MachineOutputTrigger.ItemPlacedInMachine || string.IsNullOrEmpty(input.RequiredItemId) || input.RequiredCount<1
                    || !string.IsNullOrEmpty(input.Condition) || input.RequiredTags?.Count>0
                    || !string.IsNullOrEmpty(output.Condition) || !string.IsNullOrEmpty(output.PerItemCondition) || !string.IsNullOrEmpty(output.OutputMethod)
                    || output.RandomItemId?.Count>0 || output.StackModifiers?.Count>0 || output.MaxStack>Math.Max(1,output.MinStack))continue;
                string? item=ItemRegistry.QualifyItemId(output.ItemId),ingredient=ItemRegistry.QualifyItemId(input.RequiredItemId);
                if(item==null || ingredient==null || ItemRegistry.GetDataOrErrorItem(item).IsErrorItem || ItemRegistry.GetDataOrErrorItem(ingredient).IsErrorItem)continue;
                var inputs=new List<Requirement>{new(){Item=ingredient,Count=input.RequiredCount}};
                bool valid=true;
                foreach(var fuel in pair.Value.AdditionalConsumedItems??new()) {
                    var id=ItemRegistry.QualifyItemId(fuel.ItemId);
                    if(id==null || ItemRegistry.GetDataOrErrorItem(id).IsErrorItem || fuel.RequiredCount<1){valid=false;break;}
                    inputs.Add(new(){Item=id,Count=fuel.RequiredCount});
                }
                if(!valid)continue;
                string key="process:"+pair.Key+":"+rule.Id;
                goalRecipes[key]=new(){Id=key,Item=item,Name=KnowledgeCatalog.IngredientName(item),Kind="process",Facility=pair.Key,
                    Output=Math.Max(1,output.MinStack),Source="游戏 Data/Machines · "+pair.Key+" · "+rule.Id,Inputs=inputs.GroupBy(i=>i.Item).Select(g=>new Requirement{Item=g.Key,Count=g.Sum(i=>i.Count)}).ToList()};
            }
        }
    }
    private void UpdateSharedGoals() {
        if(Knowledge==null)return;
        ReadGoalRecipes();
        var machines=new HashSet<string>();
        void Scan(GameLocation location){foreach(var o in location.objects.Values)if(o.bigCraftable.Value)machines.Add(o.QualifiedItemId);foreach(var b in location.buildings)if(b.GetIndoors() is {} inside)Scan(inside);}
        Scan(Game1.getFarm());
        foreach(var r in goalRecipes.Values)r.Known=r.Kind=="craft"?Game1.player.craftingRecipes.ContainsKey(r.Id[6..]):machines.Contains(r.Facility);
        var ledger=new GoalLedger(Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Category=s.Category,Quality=s.Quality}));
        foreach(var need in Data.Projects.Where(p=>p.Status=="active").SelectMany(p=>p.Needs).OrderByDescending(n=>n.Quality))ledger.Take(need.Item,need.Count,need.Quality);
        foreach(var goal in Data.SharedGoals)GoalPlanner.Rebuild(goal,goalRecipes,ledger,Facts.Day,
            id=>KnowledgeCatalog.IngredientName(id),goal.Entity.StartsWith("craft:")?Game1.player.craftingRecipes.GetValueOrDefault(goal.Entity[6..]):0);
    }
    public void OpenGoals() {if(Context.IsWorldReady){RefreshFacts(true);Game1.activeClickableMenu=new SharedGoalsMenu(this);}}
    public void AddSharedGoal(string entity,int count=1) {
        if(!Context.IsWorldReady)return;
        if(Data.SharedGoals.Count(g=>g.Status is "active" or "paused")>=16){Notice="先处理已有的共同心愿，再添新的吧。";return;}
        var entry=Knowledge.Index.Get(entity);
        if(entry==null || !Knowledge.Visible(entry)){Notice="先在手册选择一个明确且可见的物品或配方。";return;}
        RefreshFacts(true);ReadGoalRecipes();
        string item=goalRecipes.TryGetValue(entity,out var recipe)?recipe.Item:entity;
        if(ItemRegistry.GetDataOrErrorItem(item).IsErrorItem){Notice="这个条目不是可获得的物品；任务和献祭可从原有共同安排添加。";return;}
        var alternatives=goalRecipes.Values.Where(r=>r.Item==item).ToArray();
        if(recipe==null && alternatives.Length==1){recipe=alternatives[0];entity=recipe.Id;}
        if(Data.SharedGoals.Any(g=>g.Item==item && g.Status is "active" or "paused")){Notice="这个愿望已经记着了，可以在共同心愿里调整分工。";OpenGoals();return;}
        Data.SharedGoals.Add(new(){Entity=entity,Item=item,Title=entry.Name,Count=Math.Clamp(count,1,99),CreatedDay=Facts.Day,
            BaselineCrafts=recipe?.Kind!="craft"?0:Game1.player.craftingRecipes.GetValueOrDefault(recipe.Id[6..])});
        UpdateProjects();Persist();
        Say(Selected,"记下来了：一起准备“"+entry.Name+"”。我会先核对缺口，能准备的先准备，不用今天就全部做完。");
        OpenGoals();
    }
    public bool HandleGoalMessage(string message) {
        var prefixes=new[]{"我想要","我想做","我想造","我想弄","我们想要","我们准备做","一起准备","设定目标","共同目标"};
        var prefix=prefixes.FirstOrDefault(message.StartsWith);if(prefix==null)return false;
        if(Thinking){Notice="等我说完这句话，再一起安排。";return true;}
        string query=message[prefix.Length..].Trim(' ',':','：','。','!','！');
        if(query.Length==0){OpenGoals();return true;}
        var hits=Knowledge.Search(query,limit:8).Where(h=>h.Score>=650 && (h.Entry.Id.StartsWith("craft:")||h.Entry.Id.StartsWith("(O)")||h.Entry.Id.StartsWith("(BC)"))).ToArray();
        ReadGoalRecipes();
        var choices=hits.GroupBy(h=>goalRecipes.TryGetValue(h.Entry.Id,out var r)?r.Item:h.Entry.Id).ToArray();
        if(choices.Length==1){AddLine(Current.Chat,"你",message);AddSharedGoal(choices[0].OrderByDescending(h=>h.Entry.Id.StartsWith("craft:")).First().Entry.Id);}
        else {OpenKnowledge(query);Notice="一起在手册选准具体物品，再点加入计划；我不会猜一个高级物品替你决定。";}
        return true;
    }
    public void ToggleSharedGoal(string id) {
        var goal=Data.SharedGoals.FirstOrDefault(g=>g.Id==id);if(goal==null || goal.Status=="fulfilled")return;
        goal.Status=goal.Status=="paused"?"active":"paused";UpdateProjects();Persist();
    }
    public void AssignGoalNode(string goalId,string nodeId,string owner) {
        var goal=Data.SharedGoals.FirstOrDefault(g=>g.Id==goalId && g.Status=="active");if(goal==null)return;
        if(owner!="player" && owner!="together" && !Data.People.ContainsKey(owner))return;
        goal.Assignments[nodeId]=owner;UpdateProjects();Persist();
    }
    private IEnumerable<(SharedGoal Goal,GoalNode Node)> GoalWork(string name)=>Data.SharedGoals.Where(g=>g.Status=="active")
        .SelectMany(g=>g.Nodes.Where(n=>n.Missing>0 && n.Kind=="gather" && (n.Owner=="together" || n.Owner==name)).Select(n=>(g,n)));
    private ActivityOption? GoalOption(string name,SharedGoal goal,GoalNode node,JsonElement actor) {
        if(goal.Status!="active" || node.Missing==0 || node.Kind!="gather")return null;
        // Only observed outputs, never a generic mining action mislabelled as the desired material.
        var candidates=actor.GetProperty("candidates").EnumerateArray().Where(c=>CandidateMatches(c,node.Item)).ToArray();
        if(candidates.Length==0)return null;
        string skill=candidates[0].GetProperty("skill").GetString()!;
        if(!new[]{"mine","forage","harvest","collect"}.Contains(skill))return null;
        return new(){Id="goal:"+goal.Id+":"+node.Id,Title="为"+goal.Title+"准备"+node.Name,Category="shared",Score=Data.Pace=="focused"?105:76,
            Reason=$"这项共同心愿还缺 {node.Missing} 份{node.Name}，当前位置有对应目标；行动后按实际库存重新核算，不保证单次产量。",
            Steps=new(){new(){skill=skill,count=1,location=actor.GetProperty("location").GetString(),target_item=node.Item}}};
    }
    private static bool CandidateMatches(JsonElement candidate,string item)=>candidate.TryGetProperty("expected_items",out var outputs)
        && outputs.ValueKind==JsonValueKind.Array && outputs.EnumerateArray().Any(e=>e.GetString()==item);
    private ActivityOption? FreshGoalOption(string id,string name) {
        RefreshFacts(true);var actor=Actor(World(),name);if(!actor.HasValue)return null;
        foreach(var goal in Data.SharedGoals.Where(g=>g.Status=="active"))foreach(var node in goal.Nodes)
            if("goal:"+goal.Id+":"+node.Id==id)return GoalOption(name,goal,node,actor.Value);
        return null;
    }
    public void RequestGoalWork(string goalId,string nodeId,bool forced=false) {
        var id="goal:"+goalId+":"+nodeId;var option=FreshGoalOption(id,Selected);
        if(option==null){Notice="这一步当前没有可核对的行动目标；可以把伙伴带到材料所在地，或在手册查看获取条件。";return;}
        if(forced){AssignGoalNode(goalId,nodeId,Selected);StartFor(Selected,new(){title=option.Title,steps=option.Steps},true,"player",id,option.Reason);return;}
        if(Thinking){Notice="等我把这句话想完。";return;}
        EnsureBudget();if(Data.Calls>=Math.Clamp(Settings.MaxCallsPerDay,1,100)){Notice="今天的模型额度已用完；可分配一起准备，伙伴仍会按本地偏好安排。";return;}
        pendingGoalWork=id;pendingKnowledge=false;pendingAutonomous=false;pendingName=Selected;pendingGeneration=generation;
        string file=Path.IsPathRooted(Settings.ApiKeyFile)?Settings.ApiKeyFile:Path.Combine(Helper.DirectoryPath,Settings.ApiKeyFile);
        Data.Calls++;RecordUsage();pending=ModelClient.AskGoalWork(file,Settings.Model,new{option,profile=Current.Profile,energy=Current.Energy,social=PromptSocial(Current),current_promise=Current.Job,goals=GoalContext()});
        Notice="正在商量这一步由谁来做…";
    }
    private bool CompleteGoalWork(ModelReply reply) {
        if(pendingGoalWork==null)return false;
        string id=pendingGoalWork;pendingGoalWork=null;
        using var json=JsonDocument.Parse(reply.Json);var root=json.RootElement;
        string mode=root.GetProperty("decision").GetString()??"",speech=root.GetProperty("speech").GetString()??"";
        if(mode is not ("accept" or "refuse" or "negotiate") || speech.Length is 0 or >400)throw new InvalidOperationException("分工回复无效，保留原来的安排。");
        var option=FreshGoalOption(id,pendingName);
        if(option==null){Notice="材料或位置刚刚变化，先重新核对这一步。";return true;}
        var p=Person(pendingName);Say(pendingName,speech);
        p.Proposal=new(){decision=mode,speech=speech,title=option.Title,steps=option.Steps,option_id=id};p.ProposalAutonomous=false;
        if(mode=="accept" && pendingName==Selected)AcceptProposal(false);
        else Notice="伙伴提出了自己的想法；可以同意、换分工，或明确强制。";
        Persist();return true;
    }
    private object GoalContext()=>new {
        observed=$"{Facts.Day}日 {Facts.Time}",goals=Data.SharedGoals.Where(g=>g.Status=="active").Take(6).Select(g=>new {
            g.Id,g.Title,g.Count,g.Summary,steps=g.Nodes.Where(n=>n.Missing>0).Take(16),history=g.History.TakeLast(3)}),
        note="来自原生配方与实际库存；数量已经扣除其他目标预留。未解锁仍可备料；无对应可执行选项时讨论分工或查资料，不能编造动作或完成。"};
    private bool TryGoalShare(string name,Companion p,Situation s) {
        if(p.Social.Mode!="normal" || s.Threat)return false;
        var goal=Data.SharedGoals.FirstOrDefault(g=>g.Status is "active" or "fulfilled" && g.SharedDay!=s.Day && g.History.LastOrDefault()?.Day==s.Day);
        if(goal==null)return false;
        goal.SharedDay=s.Day;
        OpenTopic(name,"还记得我们想准备的“"+goal.Title+"”吗？"+goal.Summary+"。你想换个分工也可以，我们不用一直赶进度。","goal:"+goal.Id+":"+s.Day);return true;
    }
}
