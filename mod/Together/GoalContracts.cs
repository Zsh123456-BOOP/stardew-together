using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private string GoalRefusalContract() {
        if(Thinking)throw new InvalidOperationException("finish_model_call_first");
        var actor=Actor(World(),Selected)??throw new InvalidOperationException("recruit_before_refusal_contract");
        var option=GoalWork(Selected).Select(pair=>GoalOption(Selected,pair.Goal,pair.Node,actor)).FirstOrDefault(o=>o!=null)
            ??throw new InvalidOperationException("requires_actionable_goal_fixture");
        var saved=JsonSerializer.Serialize(Current);
        try {
            pendingName=Selected;pendingGoalWork=option.Id;
            CompleteGoalWork(new("{\"decision\":\"refuse\",\"speech\":\"我不想现在去挖矿，换个分工好吗？\"}",0));
            bool pass=Current.Proposal?.decision=="refuse" && Current.Life.RetryAfter.GetValueOrDefault(option.Id)>Minute
                && !OptionsFor(Selected,Current,SituationFor(Selected,actor,World()),actor).Any(o=>o.Id==option.Id);
            return JsonSerializer.Serialize(new{pass,label="simulated refusal excludes the same material from immediate autonomous work"},jsonOptions);
        } finally {Data.People[Selected]=JsonSerializer.Deserialize<Companion>(saved)!;pendingGoalWork=null;Persist();}
    }
    private string GoalContracts() {
        var results=new List<object>();void Check(bool pass,string label)=>results.Add(new{pass,label});
        var saved=JsonSerializer.Serialize(Data.SharedGoals);var oldProjects=Data.Projects;var notebook=JsonSerializer.Serialize(Data.Knowledge);
        string policy=JsonSerializer.Serialize(Data.FarmPolicy);var rng=Game1.random;Game1.random=new Random(661);
        int money=Game1.player.Money;string inventory=JsonSerializer.Serialize(Game1.player.Items.Where(i=>i!=null).Select(i=>new{i.QualifiedItemId,i.Stack}));
        try {
            Data.SharedGoals.Clear();Data.Projects=new();Data.Knowledge.DiscoveredOnly=false;
            RefreshFacts(true);ReadGoalRecipes();
            Check(goalRecipes.Values.Count(r=>r.Kind=="craft")>100,"native static crafting recipe catalog includes locked recipes");
            Check(goalRecipes.Values.Any(r=>r.Kind=="process" && r.Item=="(O)334" && r.Inputs.Any(i=>i.Item=="(O)378")),"native machine rules connect copper bars to ore");
            var recipe=goalRecipes.Values.First(r=>r.Kind=="craft" && r.Inputs.Any(i=>i.Item=="(O)334"));
            AddSharedGoal(recipe.Id);var goal=Data.SharedGoals.Last();
            Check(goal.Nodes.Any(n=>n.Item=="(O)378") || goal.Nodes.Any(n=>n.Item=="(O)334" && (n.Owned>0 || n.InProgress>0)),"real wish exposes ore dependency or already allocated bars");
            Check(policy==JsonSerializer.Serialize(Data.FarmPolicy),"creating a wish grants no spending or terrain permissions");
            var node=goal.Nodes.First(n=>n.Id!="root");AssignGoalNode(goal.Id,node.Id,"player");
            Check(goal.Nodes.First(n=>n.Id==node.Id).Owner=="player","specific material can be assigned to player");
            Check(!GoalWork(Selected).Any(pair=>pair.Node.Id==node.Id && pair.Goal.Id==goal.Id),"player-owned material excluded from autonomous goal work");
            ToggleSharedGoal(goal.Id);Check(goal.Status=="paused" && goal.Reserved.Count==0,"pause releases real wish reservation");
            ToggleSharedGoal(goal.Id);Check(goal.Status=="active","paused wish resumes without recreation");
            var option=FreshGoalOption("goal:nonexistent:root",Selected);Check(option==null,"stale goal action cannot start");
            var book=Knowledge.Query("",recipe.Id);Check(book.Facts.Any(f=>f.Label.StartsWith("我们的心愿")),"encyclopedia answers include shared goal progress");
            var named=Knowledge.Query("Keg 还缺什么材料？");Check(named.EntityIds.Count>0 && named.EntityIds.All(id=>Knowledge.Index.Get(id)?.Name==Knowledge.Index.Get("craft:Keg")?.Name),"named goal query excludes recipes matched only through generic material tags");
            OpenGoals();var menu=(SharedGoalsMenu)Game1.activeClickableMenu;menu.CheckButton("数量＋");Check(goal.Count==2,"actual goal menu click changes bounded requested count");
            menu.CheckButton("暂停心愿");Check(goal.Status=="paused","actual goal menu click pauses");
            Check(money==Game1.player.Money && inventory==JsonSerializer.Serialize(Game1.player.Items.Where(i=>i!=null).Select(i=>new{i.QualifiedItemId,i.Stack})),"planning does not fabricate money or items");
            Check(Game1.random.Next()==new Random(661).Next(),"planning and native recipe reading preserve gameplay RNG");
        } finally {
            Game1.random=rng;Data.SharedGoals=JsonSerializer.Deserialize<List<SharedGoal>>(saved)!;Data.Projects=oldProjects;Data.Knowledge=JsonSerializer.Deserialize<KnowledgeNotebook>(notebook)!;
            if(Game1.activeClickableMenu is SharedGoalsMenu)Game1.exitActiveMenu();RefreshFacts(true);Persist();
        }
        return JsonSerializer.Serialize(new{results},jsonOptions);
    }
}
