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
            Add(85,goal.Id,goal.Summary,new{tool=goal.AutoExecute?"world.read":"goal.run",args=goal.AutoExecute?(object)new{}:new{id=goal.Id}});
        foreach(var recipe in goalRecipes.Values.Where(r=>r.Kind=="craft"&&Game1.player.craftingRecipes.ContainsKey(r.Id[6..])&&Game1.player.craftingRecipes[r.Id[6..]]==0)) {
            var ledger=new GoalLedger(Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category}));
            foreach(var reserved in AllReservations().OrderByDescending(n=>n.Quality))ledger.Take(reserved.Item,reserved.Count,reserved.Quality);
            int missing=recipe.Inputs.Sum(i=>i.Count-ledger.Take(i.Item,i.Count,i.Quality));
            if(missing==0)Add(65,recipe.Id,"配方已知，备料可用，原生制作记录仍为零",new{tool="goal.create",args=new{request_id="craft-"+Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(recipe.Id)))[..12],entity=recipe.Id,count=1,completion="crafted"}});
        }
        if(Game1.player.Items.Any(i=>i?.Category==-74)&&Game1.currentLocation.IsFarm)
            Add(70,"farm:layout","持有种子，计算季节/地形/维护负担后安排",new{tool="farm.plan",args=new{priority="collection"}});
        if(Game1.player.Stamina>=28&&Game1.currentLocation.canFishHere()&&Game1.player.Items.Any(i=>i is StardewValley.Tools.FishingRod))
            Add(45,"fish:progress","当地允许钓鱼，持有鱼竿且留有体力",new{tool="player.fish",args=new{count=3,reserve_stamina=0}});
        if(Facts.AnimalsUnpetted>0)Add(92,"animals:pet","动物还有未完成抚摸，可由玩家或伙伴分担",new{tool="work.run",args=new{goal="pet",count=0}});
        if(Facts.FeedNeeded>0)Add(93,"animals:feed","食槽缺草，先取筒仓实际库存再喂养",new{tool="work.run",args=new{goal="feed",count=0}});
        foreach(var quest in Game1.player.questLog.Where(q=>q.completed.Value&&q.HasMoneyReward()).Take(3))
            Add(90,"reward:"+NativeQuestIdentity.Id(quest),"任务已完成，金币奖励尚未领取",new{tool="player.claim_reward",args=new{quest_id=NativeQuestIdentity.Id(quest)}});
        if(Game1.player.deepestMineLevel<120&&Game1.player.Stamina>=80&&Game1.player.health>=70&&Game1.timeOfDay<1400&&Game1.player.Items.Any(i=>i is StardewValley.Tools.Pickaxe)&&Game1.player.Items.Any(i=>i is StardewValley.Tools.MeleeWeapon w&&!w.isScythe()))
            Add(60,"mine:next_checkpoint","体力生命和时间允许，推进下一电梯里程碑",new{tool="work.run",args=new{goal="mine_trip",target_level=Math.Min(120,(StardewValley.Locations.MineShaft.lowestLevelReached/5+1)*5),until=2100,reserve_stamina=25}});
        foreach(var bundle in Facts.Bundles.Where(b=>!b.Complete&&b.Missing.Any(n=>Facts.Stock.Where(s=>s.Item==n.Item&&s.Quality>=n.Quality).Sum(s=>s.Count)>=n.Count)).Take(2))
            Add(75,"bundle:"+bundle.Id,"存在已具备的献祭材料；先按品质取回，再原生提交",new{tool="progress.catalog",args=new{kind="bundle"},bundle=bundle.Id,next_tool="player.bundle"});
        return choices.OrderByDescending(c=>c.Priority).Take(10).Select(c=>c.Value).ToArray();
    }
}
