using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    internal object AgentGoalCreate(JsonElement args) {
        RefreshFacts(true);ReadGoalRecipes();
        string request=AgentToolRegistry.Text(args,"request_id"),entity=AgentToolRegistry.Text(args,"entity");
        int count=AgentToolRegistry.Number(args,"count",1);
        if(request.Length is <1 or >64||!request.All(c=>char.IsLetterOrDigit(c)||c is '-' or '_')||count is <1 or >999)throw new InvalidOperationException("invalid_goal_request");
        string id="agent-"+request;
        var old=Data.SharedGoals.FirstOrDefault(g=>g.Id==id);
        if(old!=null) {
            if(old.Entity!=entity||old.Count!=count)throw new InvalidOperationException("goal_request_id_reused");
            return old;
        }
        if(Data.SharedGoals.Count(g=>g.Status is "active" or "paused")>=16)throw new InvalidOperationException("active_goal_limit");
        string item=goalRecipes.TryGetValue(entity,out var recipe)?recipe.Item:entity;
        var definition=ItemRegistry.GetDataOrErrorItem(item);if(definition.IsErrorItem)throw new InvalidOperationException("known_item_or_recipe_required");
        var goal=new SharedGoal{Id=id,Entity=entity,Item=item,Title=definition.DisplayName,Count=count,CreatedDay=Facts.Day,
            BaselineCrafts=recipe?.Kind=="craft"?Game1.player.craftingRecipes.GetValueOrDefault(recipe.Id[6..]):0};
        Data.SharedGoals.Add(goal);UpdateProjects();return goal;
    }
    internal object AgentGoalPrepare(JsonElement args) {
        RefreshFacts(true);
        var goal=Data.SharedGoals.FirstOrDefault(g=>g.Id==AgentToolRegistry.Text(args,"id"))??throw new InvalidOperationException("shared_goal_not_found");
        var tasks=new List<AgentTaskSpec>();var gaps=new List<object>();
        void Add(string tool,object arguments,string purpose) {
            tasks.Add(new(){id="goal-"+Guid.NewGuid().ToString("N"),actor="player",tool=tool,args=JsonSerializer.SerializeToElement(arguments),purpose=purpose,day=Game1.Date.TotalDays,deadline=2200});
        }
        if(goal.Status=="active") {
            // Prepare one ready production node at a time. Re-read native inventory
            // after it completes instead of spending predicted outputs in advance.
            var ready=goal.Nodes.FirstOrDefault(n=>n.Status=="player_step"&&n.Kind=="craft"&&n.Recipe.StartsWith("craft:"));
            if(ready!=null&&goalRecipes.TryGetValue(ready.Recipe,out var recipe)) {
                int batches=(int)Math.Ceiling(ready.ToPrepare/(double)Math.Max(1,recipe.Output));
                var carried=new GoalLedger(Game1.player.Items.Where(i=>i!=null).Select(i=>new GoalStock{Item=i.QualifiedItemId,Count=i.Stack,Quality=i.Quality,Category=i.Category}));
                var stored=SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer().Where(i=>i!=null)).ToArray();
                var storedLedger=new GoalLedger(stored.Select(i=>new GoalStock{Item=i.QualifiedItemId,Count=i.Stack,Quality=i.Quality,Category=i.Category}));
                foreach(var need in recipe.Inputs) {
                    int required=need.Count*batches,missing=required-carried.Take(need.Item,required,need.Quality);
                    if(missing<=0)continue;
                    bool category=int.TryParse(need.Item.Replace("(O)",""),out int cat)&&cat<0;
                    foreach(var group in stored.Where(i=>i.Quality>=need.Quality&&(i.QualifiedItemId==need.Item||category&&i.Category==cat)).GroupBy(i=>i.QualifiedItemId).OrderBy(g=>g.Min(i=>i.Quality))) {
                        int take=storedLedger.Take(group.Key,missing,need.Quality);if(take==0)continue;
                        Add("work.run",new{goal="withdraw",item=group.Key,count=take,quality=need.Quality},"为"+goal.Title+"取材料");missing-=take;if(missing==0)break;
                    }
                    if(missing>0)gaps.Add(new{node=ready.Id,reason="ingredients_not_in_designated_shared_storage",need.Item,missing});
                }
                if(gaps.Count==0)Add("player.craft",new{recipe=ready.Recipe[6..],count=batches,goal_id=goal.Id},"完成"+ready.Name+"并核验原生制作计数");
            }else {
                foreach(var node in goal.Nodes.Where(n=>n.Kind=="gather"&&n.ToPrepare>0)) {
                    string skill=node.Item switch{"(O)388"=>"wood","(O)390"=>"stone","(O)771"=>"fiber",_=>""};
                    if(skill.Length>0){Add("work.run",new{goal=skill,count=Math.Min(node.ToPrepare,999),location="Farm",include_trees=skill=="wood"},"为"+goal.Title+"收集"+node.Name);break;}
                    gaps.Add(new{node=node.Id,item=node.Item,reason="acquisition_route_requires_choice_or_missing_executor",node.ToPrepare});
                }
                foreach(var node in goal.Nodes.Where(n=>n.Status is "locked" or "blocked" || n.Status=="player_step"&&n.Kind!="craft"))gaps.Add(new{node=node.Id,node.Status,node.Reason});
            }
        }
        if(tasks.Count>24)throw new InvalidOperationException("goal_batch_too_large_split_required");
        if(gaps.Count>0&&tasks.Any(t=>t.tool=="work.run"&&AgentToolRegistry.Text(t.args,"goal")=="withdraw"))tasks.Clear();
        return new{goal,expected_revision=Data.Autoplay.Schedule.Revision,tasks,gaps,
            next="使用plan.submit提交返回的tasks；本批完成后再次goal.prepare，始终按真实新库存展开下一层。没有生成任务不代表目标完成。"};
    }
}
