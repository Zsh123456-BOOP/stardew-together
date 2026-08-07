namespace Together;

// Pure, deterministic planning. Neither model text nor an action receipt can satisfy a goal.
public sealed class SharedGoal {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Entity {get;set;}="";
    public string Item {get;set;}="";
    public string Title {get;set;}="";
    public int Count {get;set;}=1;
    public int CreatedDay {get;set;}
    public int BaselineCrafts {get;set;}
    public string Status {get;set;}="active";
    public string Owner {get;set;}="together";
    public Dictionary<string,string> Assignments {get;set;}=new();
    public List<GoalNode> Nodes {get;set;}=new();
    public List<GoalEvent> History {get;set;}=new();
    public List<Requirement> Reserved {get;set;}=new();
    public string Fingerprint {get;set;}="";
    public int SharedDay {get;set;}=-1;
    public string Summary {get;set;}="";
}
public sealed class GoalEvent {
    public int Day {get;set;}
    public string Text {get;set;}="";
}
public sealed class GoalNode {
    public string Id {get;set;}="";
    public string Item {get;set;}="";
    public string Name {get;set;}="";
    public string Owner {get;set;}="together";
    public string Kind {get;set;}="gather";
    public string Status {get;set;}="missing";
    public int Required {get;set;}
    public int Owned {get;set;}
    public int Missing=>Math.Max(0,Required-Owned);
    public string Reason {get;set;}="";
    public string Source {get;set;}="";
    public List<string> DependsOn {get;set;}=new();
}
public sealed class GoalRecipe {
    public string Kind {get;set;}="craft";
    public string Facility {get;set;}="";
    public string Id {get;set;}="";
    public string Item {get;set;}="";
    public string Name {get;set;}="";
    public int Output {get;set;}=1;
    public bool Known {get;set;}
    public string Unlock {get;set;}="";
    public string Source {get;set;}="";
    public List<Requirement> Inputs {get;set;}=new();
}
public sealed class GoalStock {
    public string Item {get;set;}="";
    public int Count {get;set;}
    public int Quality {get;set;}
    public int Category {get;set;}
}
public sealed class GoalLedger {
    private readonly List<GoalStock> stock;
    public GoalLedger(IEnumerable<GoalStock> source){stock=source.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category}).ToList();}
    public int Take(string id,int count,int quality=0) {
        bool category=int.TryParse(id.Replace("(O)",""),out int cat)&&cat<0;
        int taken=0;
        foreach(var s in stock.Where(s=>s.Quality>=quality && (s.Item==id || category&&s.Category==cat)).OrderBy(s=>s.Quality)) {
            int n=Math.Min(Math.Max(0,s.Count),count-taken);s.Count-=n;taken+=n;if(taken==count)break;
        }
        return taken;
    }
}
public static class GoalPlanner {
    public static void Rebuild(SharedGoal goal,IReadOnlyDictionary<string,GoalRecipe> recipes,GoalLedger ledger,int day,
        Func<string,string> name,int crafted=0) {
        if(goal.Status!="active"){goal.Reserved.Clear();return;}
        goal.Nodes=new();goal.Reserved=new();
        int budget=96;
        string Expand(string item,int count,string path,HashSet<string> ancestors,int depth,GoalRecipe? selected=null) {
            var node=new GoalNode{Id=path,Item=item,Name=name(item),Required=count,Owner=goal.Assignments.GetValueOrDefault(path,goal.Owner),Source="当前存档库存"};
            goal.Nodes.Add(node);budget--;
            node.Owned=ledger.Take(item,count);
            if(node.Owned>0)goal.Reserved.Add(new(){Item=item,Count=node.Owned,Name=node.Name});
            if(node.Missing==0){node.Status="ready";node.Reason="这份目标已分配到足量库存";return path;}
            var alternatives=recipes.Values.Where(r=>r.Item==item).ToArray();
            var recipe=selected??alternatives.OrderByDescending(r=>r.Known).ThenBy(r=>r.Kind=="craft"?0:1).ThenBy(r=>r.Output).ThenBy(r=>r.Id,StringComparer.Ordinal).FirstOrDefault();
            if(recipe==null){node.Reason=alternatives.Length>1?"存在多种制作途径，请从百科选择具体配方":"先收集；没有已核实的制作配方，可查百科或请玩家处理获取条件";return path;}
            node.Kind=recipe.Kind;node.Source=recipe.Source;
            node.Status=recipe.Known?"waiting":"locked";
            node.Reason=recipe.Kind=="process"?"备料后在"+name(recipe.Facility)+"加工；先收齐原料，最终以实际产物核验。"+(recipe.Known?"":"还没有观察到已放置的设备。"):(recipe.Known?"备料后由玩家制作；伙伴继续准备能取得的材料":"配方尚未解锁；先备料。"+recipe.Unlock);
            if(depth>=6 || budget<recipe.Inputs.Count || !ancestors.Add(item)) {node.Status="blocked";node.Reason="依赖过深或循环，停止自动展开；需要选择其他获取途径";return path;}
            if(recipe.Kind=="process" && !recipe.Known && recipe.Facility!="")node.DependsOn.Add(Expand(recipe.Facility,1,path+"/facility",new(ancestors),depth+1));
            int batches=(int)Math.Ceiling(node.Missing/(double)Math.Max(1,recipe.Output));
            foreach(var input in recipe.Inputs) {
                int required=(int)Math.Min(100000,(long)batches*input.Count);
                node.DependsOn.Add(Expand(input.Item,required,path+"/"+input.Item,new(ancestors),depth+1));
            }
            if(recipe.Known && node.DependsOn.All(id=>goal.Nodes.First(n=>n.Id==id).Status=="ready"))node.Status="player_step";
            return path;
        }
        recipes.TryGetValue(goal.Entity,out var rootRecipe);
        Expand(goal.Item,goal.Count,"root",new(),0,rootRecipe);
        var root=goal.Nodes[0];
        bool craftedEnough=rootRecipe!=null && (long)Math.Max(0,crafted-goal.BaselineCrafts)*rootRecipe.Output>=goal.Count;
        if(root.Missing==0 || craftedEnough) {
            goal.Status="fulfilled";goal.Reserved.Clear();goal.Summary=craftedEnough?"原生制作计数确认目标数量已完成":"当前实际库存确认已拥有目标数量";
        } else {
            int missing=goal.Nodes.Where(n=>n.Kind=="gather").Sum(n=>n.Missing);
            goal.Summary=$"目标 {goal.Count} 份，已分配 {root.Owned} 份；基础材料还缺 {missing} 件";
            if(goal.Nodes.Any(n=>n.Status=="locked"))goal.Summary+="；有配方或设备条件尚未满足，仍可先备料";
            else if(goal.Nodes.Any(n=>n.Status=="player_step"))goal.Summary+="；有一步备料齐全，等你制作";
        }
        string fingerprint=goal.Status+"|"+string.Join(";",goal.Nodes.Select(n=>$"{n.Id}:{n.Owned}:{n.Status}:{n.Owner}"));
        if(goal.Fingerprint!=fingerprint) {
            goal.Fingerprint=fingerprint;goal.History.Add(new(){Day=day,Text=goal.Summary});
            if(goal.History.Count>24)goal.History.RemoveRange(0,goal.History.Count-24);
        }
    }
}
