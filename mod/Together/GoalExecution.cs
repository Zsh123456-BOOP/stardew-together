using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    internal object AgentGoalCreate(JsonElement args) {
        RefreshFacts(true);ReadGoalRecipes();
        string request=AgentToolRegistry.Text(args,"request_id"),entity=AgentToolRegistry.Text(args,"entity");
        string completion=AgentToolRegistry.Text(args,"completion","owned");
        if(completion is not ("owned" or "crafted" or "cooked"))throw new InvalidOperationException("invalid_completion_predicate");
        int count=AgentToolRegistry.Number(args,"count",1),quality=AgentToolRegistry.Number(args,"quality",0);
        if(quality is not (0 or 1 or 2 or 4)||quality>0&&completion!="owned")throw new InvalidOperationException("quality_requires_owned_goal_and_native_quality_tier");
        if(request.Length is <1 or >64||!request.All(c=>char.IsLetterOrDigit(c)||c is '-' or '_')||count is <1 or >999)throw new InvalidOperationException("invalid_goal_request");
        string id="agent-"+request;
        var old=Data.SharedGoals.FirstOrDefault(g=>g.Id==id);
        if(old!=null) {
            if(old.Entity!=entity||old.Count!=count||old.Completion!=completion||old.MinimumQuality!=quality)throw new InvalidOperationException("goal_request_id_reused");
            return old;
        }
        if(Data.SharedGoals.Count(g=>g.Status is "active" or "paused")>=16)throw new InvalidOperationException("active_goal_limit");
        string item=goalRecipes.TryGetValue(entity,out var recipe)?recipe.Item:entity;
        if(completion=="cooked"&&recipe?.Kind!="cook")throw new InvalidOperationException("cooked_goal_requires_native_cooking_recipe");
        if(completion=="crafted"&&recipe?.Kind!="craft")throw new InvalidOperationException("crafted_goal_requires_native_crafting_recipe");
        var definition=ItemRegistry.GetDataOrErrorItem(item);if(definition.IsErrorItem)throw new InvalidOperationException("known_item_or_recipe_required");
        var goal=new SharedGoal{Id=id,Entity=entity,Item=item,Title=definition.DisplayName,Count=count,MinimumQuality=quality,CreatedDay=Facts.Day,Completion=completion,
            BaselineCrafts=recipe?.Kind=="craft"?Game1.player.craftingRecipes.GetValueOrDefault(recipe.Id[6..]):0};
        goal.BaselineCrafts=NativeGoalCount(goal);Data.SharedGoals.Add(goal);UpdateProjects();return goal;
    }
    private IEnumerable<(GameLocation Location,StardewValley.Object Object)> GoalMachines() {
        var locations=new List<GameLocation>();void Scan(GameLocation l){locations.Add(l);foreach(var b in l.buildings)if(b.GetIndoors() is {} inside)Scan(inside);}Scan(Game1.getFarm());
        return locations.SelectMany(l=>l.objects.Values.Where(o=>o.GetMachineData()!=null).Select(o=>(l,o)));
    }
    private string? FindGoalResourceLocation(string item,string skill) {
        var locations=new[]{Game1.currentLocation,Game1.getFarm()}.Concat(Game1.locations).Distinct();
        return locations.FirstOrDefault(l=>skill=="resource"?l.objects.Values.Any(o=>ResourceRules.Nodes.GetValueOrDefault(o.ItemId)==item):l.resourceClumps.Any(c=>ResourceRules.Clump(c.parentSheetIndex.Value)?.Output==item))?.NameOrUniqueName;
    }
    internal GoalBatch AgentGoalPrepare(JsonElement args) {
        RefreshFacts(true);
        var goal=Data.SharedGoals.FirstOrDefault(g=>g.Id==AgentToolRegistry.Text(args,"id"))??throw new InvalidOperationException("shared_goal_not_found");
        var tasks=new List<AgentTaskSpec>();var gaps=new List<object>();
        void Add(string tool,object arguments,string purpose) {
            tasks.Add(new(){id="goal-"+Guid.NewGuid().ToString("N"),actor="player",tool=tool,args=JsonSerializer.SerializeToElement(arguments),purpose=purpose,goal_id=goal.Id,day=Game1.Date.TotalDays,deadline=2200});
        }
        if(goal.Status=="active") {
            var facilityNode=goal.Nodes.FirstOrDefault(n=>n.Kind=="process"&&n.Status=="locked"&&goalRecipes.TryGetValue(n.Recipe,out var r)&&r.Facility.Length>0&&goal.Nodes.Any(child=>child.Item==r.Facility&&child.Owned>0));
            if(facilityNode!=null&&goalRecipes.TryGetValue(facilityNode.Recipe,out var facilityRecipe)) {
                if(!Game1.player.Items.Any(i=>i?.QualifiedItemId==facilityRecipe.Facility))Add("work.run",new{goal="withdraw",item=facilityRecipe.Facility,count=1},"取出已制作的加工设备");
                Add("player.place_facility",new{item=facilityRecipe.Facility,location="Farm",goal_id=goal.Id},"算法选址并原生放置设备，之后继续投料加工");
                return new(goal,Data.Autoplay.Schedule.Revision,tasks,gaps);
            }
            // Prepare one ready production node at a time. Re-read native inventory
            // after it completes instead of spending predicted outputs in advance.
            var ready=goal.Nodes.FirstOrDefault(n=>n.Status=="player_step"&&n.Kind is "craft" or "cook" or "process");
            if(ready!=null&&goalRecipes.TryGetValue(ready.Recipe,out var recipe)) {
                int batches=Math.Min(99,(int)Math.Ceiling(ready.ToPrepare/(double)Math.Max(1,recipe.Output)));
                string machineLocation="";
                if(recipe.Kind=="process") {
                    var machines=GoalMachines().Where(m=>m.Object.QualifiedItemId==recipe.Facility&&m.Object.heldObject.Value==null).GroupBy(m=>m.Location.NameOrUniqueName).OrderByDescending(g=>g.Count()).FirstOrDefault();
                    if(machines==null){gaps.Add(new{node=ready.Id,reason="processing_facility_busy_or_missing"});batches=0;}
                    else {machineLocation=machines.Key;batches=Math.Min(batches,machines.Count());}
                }
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
                if(gaps.Count==0) {
                    if(recipe.Kind=="process")Add("player.machine",new{mode="load",location=machineLocation,machine=recipe.Facility,item=recipe.Inputs[0].Item,count=batches,goal_id=goal.Id,output=recipe.Item},"投料加工"+ready.Name+"，等待原生出炉");
                    else Add(recipe.Kind=="cook"?"player.cook":"player.craft",new{recipe=ready.Recipe[(recipe.Kind=="cook"?5:6)..],count=batches,goal_id=goal.Id},"完成"+ready.Name+"并核验原生制作计数");
                }
            }else {
                var product=GoalMachines().FirstOrDefault(m=>m.Object.readyForHarvest.Value&&m.Object.heldObject.Value is {} output&&goal.Nodes.Any(n=>n.Item==output.QualifiedItemId&&n.Missing>0));
                if(product.Object!=null)Add("player.machine",new{mode="collect",location=product.Location.NameOrUniqueName,machine=product.Object.QualifiedItemId,count=1},"领取目标真实加工产物");
                foreach(var node in goal.Nodes.Where(n=>n.Kind=="gather"&&n.ToPrepare>0&&tasks.Count==0)) {
                    string skill=node.Item switch{"(O)388"=>"wood","(O)390"=>"stone","(O)771"=>"fiber","(O)709"=>"hardwood",_=>ResourceRules.Nodes.Values.Contains(node.Item)?"resource":""};
                    string? resourceLocation=skill is "resource" or "hardwood"?FindGoalResourceLocation(node.Item,skill):"Farm";
                    if(skill.Length>0&&resourceLocation!=null&&node.Quality==0){Add("work.run",new{goal=skill,item=node.Item,count=Math.Min(node.ToPrepare,999),location=resourceLocation,include_trees=skill=="wood"},"为"+goal.Title+"收集"+node.Name);break;}
                    if(node.Quality==0&&FishingLocations(node.Item).FirstOrDefault() is {} fishLocation) {
                        Add("work.run",new{goal="fish",item=node.Item,count=Math.Min(10,node.ToPrepare),location=fishLocation.NameOrUniqueName},"定向准备"+node.Name+"，按原生捕获与真实库存续接");break;
                    }
                    if(PrepareLivingMaterial(goal,node,Add,out string livingWait)) {if(livingWait.Length>0)gaps.Add(new{node=node.Id,item=node.Item,reason=livingWait});break;}
                    gaps.Add(new{node=node.Id,item=node.Item,node.Quality,reason="acquisition_route_requires_choice_or_missing_executor",node.ToPrepare});
                }
                foreach(var node in goal.Nodes.Where(n=>n.Status is "locked" or "blocked" || n.Status=="player_step"&&n.Kind is not ("craft" or "cook" or "process")))gaps.Add(new{node=node.Id,node.Status,node.Reason});
            }
        }
        if(tasks.Count>24)throw new InvalidOperationException("goal_batch_too_large_split_required");
        if(gaps.Count>0&&tasks.Any(t=>t.tool=="work.run"&&AgentToolRegistry.Text(t.args,"goal")=="withdraw"))tasks.Clear();
        return new(goal,Data.Autoplay.Schedule.Revision,tasks,gaps);
    }
}

public sealed record GoalBatch(SharedGoal goal,int expected_revision,List<AgentTaskSpec> tasks,List<object> gaps) {
    public string next=>"goal.run 持续执行或 plan.submit 提交本批；真实库存核算，不把排队当完成。";
}
