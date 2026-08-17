using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private object[] ProgressOpportunities() {
        ReadGoalRecipes();var choices=new List<(int Priority,object Value)>();
        void Add(int priority,string goal,string reason,object next)=>choices.Add((priority,new{goal,reason,next}));
        if(Facts.DryCrops>0)Add(100,"farm:water",$"{Facts.DryCrops} 株待照料",new{tool="work.run",args=new{goal="water",location="Farm",count=0}});
        if(Facts.RipeCrops>0)Add(98,"farm:harvest",$"{Facts.RipeCrops} 株成熟",new{tool="work.run",args=new{goal="harvest",location="Farm",count=0}});
        foreach(var quest in Facts.Goals.Where(g=>!g.Complete&&g.Deadline>=Facts.Day&&g.Deadline-Facts.Day<=2).OrderBy(g=>g.Deadline).Take(3))
            Add(95,quest.Id,"期限临近："+quest.Title,new{tool="progress.catalog",args=new{kind="quest"},quest.Deadline,quest.Needs,executor_gap=quest.PlayerStep});
        foreach(var goal in Data.SharedGoals.Where(g=>g.Status=="active").Take(4))
            Add(85,goal.Id,goal.Summary,new{tool="goal.prepare",args=new{id=goal.Id}});
        foreach(var recipe in goalRecipes.Values.Where(r=>r.Kind=="craft"&&Game1.player.craftingRecipes.ContainsKey(r.Id[6..])&&Game1.player.craftingRecipes[r.Id[6..]]==0)) {
            var ledger=new GoalLedger(Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category}));
            foreach(var reserved in AllReservations().OrderByDescending(n=>n.Quality))ledger.Take(reserved.Item,reserved.Count,reserved.Quality);
            int missing=recipe.Inputs.Sum(i=>i.Count-ledger.Take(i.Item,i.Count,i.Quality));
            if(missing==0)Add(65,recipe.Id,"配方已知，备料可用，原生制作记录仍为零",new{tool="goal.create",args=new{request_id="craft-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(recipe.Id)))[..12],entity=recipe.Id,count=1,completion="crafted"}});
        }
        if(Game1.player.Items.Any(i=>i?.Category==-74)&&Game1.currentLocation.IsFarm)
            Add(70,"farm:layout","持有种子，计算季节/地形/维护负担后安排",new{tool="farm.plan",args=new{priority="collection"}});
        if(Game1.player.Stamina>=28&&Game1.currentLocation.canFishHere()&&Game1.player.Items.Any(i=>i is StardewValley.Tools.FishingRod))
            Add(45,"fish:progress","当地允许钓鱼，持有鱼竿且留有体力",new{tool="player.fish",args=new{count=3,reserve_stamina=20}});
        if(Facts.AnimalsUnpetted+Facts.FeedNeeded>0)Add(92,"animals:care","动物还有未完成照料，可由伙伴分担",new{tool="world.read",args=new{},locations=Facts.CareLocations});
        return choices.OrderByDescending(c=>c.Priority).Take(10).Select(c=>c.Value).ToArray();
    }
}
