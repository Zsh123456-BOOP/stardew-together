using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private void RefreshCapacityVersion() {
        var p=Game1.player;
        foreach(var task in Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal&&t.spec.tool is "player.craft" or "player.machine" or "player.order_donate"))
            Data.Autoplay.Capacity.ApprovedUses.Add(FailureKnowledge.Key("player",task.spec.tool,task.spec.args.GetRawText()));
        // Deliberately excludes day, clock, stamina, actor position, dialogue and
        // task IDs. Rewording/reissuing a task cannot erase a physical constraint.
        var stamp=FailureKnowledge.Hash(AgentJson.Encode(new{slots=p.MaxItems,
            inventory=p.Items.Select((i,n)=>new{slot=n,id=i?.QualifiedItemId,quality=i?.Quality,count=i?.Stack,max=i?.maximumStackSize(),type=i?.GetType().FullName,data=i?.modData.Pairs.OrderBy(v=>v.Key).ToArray()}),
            storage=SharedStorage().OrderBy(s=>s.Location.NameOrUniqueName).ThenBy(s=>s.Tile.X).ThenBy(s=>s.Tile.Y).Select(s=>new{location=s.Location.NameOrUniqueName,x=s.Tile.X,y=s.Tile.Y,slots=s.Chest.GetActualCapacity(),reachable=s.Location==Game1.currentLocation?WorkStand(s.Location,s.Tile.ToPoint()).HasValue:PlayerExecutor.NextExit(Game1.currentLocation,s.Location.NameOrUniqueName)!=null,items=s.Chest.GetItemsForPlayer().Select(i=>new{id=i?.QualifiedItemId,quality=i?.Quality,count=i?.Stack})}),
            protection=ConsumptionRequirements(protectProgress:true).OrderBy(r=>r.Item).ThenBy(r=>r.Quality).Select(r=>new{r.Item,r.Count,r.Quality}),
            uses=Data.Autoplay.Capacity.ApprovedUses.OrderBy(x=>x)}));
        var state=Data.Autoplay.Capacity;bool had=state.Constraints.Count>0;
        if(state.Observe(stamp)&&had)Data.Autoplay.Record("capacity_constraints_released",AgentJson.Encode(new{state.Version,reason="capacity_facts_changed"}));
    }
    private void RecordCapacityConstraint(string code,string actor="player",string[]? excluded=null) {
        if(!CapacityState.IsConstraint(code))return;
        RefreshCapacityVersion();if(actor=="decision")actor="player";
        var state=Data.Autoplay.Capacity;
        if(state.Constraints.Any(c=>c.Actor==actor&&c.RootCause==code&&c.CapacityVersion==state.Version) )return;
        state.Block(actor,code,Game1.Date.TotalDays,Game1.timeOfDay,excluded);
        Data.Autoplay.Record("capacity_constraint_created",AgentJson.Encode(state.Constraints.Last()));
    }
    private CapacityVerdict? CapacityAdmission(string tool,JsonElement args) {
        var p=Game1.player;
        if(tool is "player.craft" or "player.cook") {
            string name=AgentToolRegistry.Text(args,"recipe");var recipes=tool=="player.cook"?p.cookingRecipes:p.craftingRecipes;
            if(!recipes.ContainsKey(name))return null;var recipe=new CraftingRecipe(name,tool=="player.cook");
            if(!recipe.doesFarmerHaveIngredientsInInventory())return null;
            var output=recipe.createItem();if(tool=="player.cook"&&p.Items.Any(i=>i?.QualifiedItemId=="(O)917"))output.Quality=2;
            return CapacityAdapter.After(p,CapacityAdapter.Ingredients(p,recipe),output);
        }
        if(tool=="player.buy"&&!args.TryGetProperty("recipe",out _)) {
            string item=AgentToolRegistry.Text(args,"item");if(item.Length>0)return CapacityAdapter.Receive(p,ItemRegistry.Create(item));
        }
        string goal=tool=="work.run"?AgentToolRegistry.Text(args,"goal"):"";
        if(tool=="work.run") {
            if(goal is "store" or "storage_expand" or "water" or "refill" or "pet" or "feed" or "plant" or "clear_dead")return null;
            string item=AgentToolRegistry.Text(args,"item",goal switch{"wood"=>"(O)388","stone"=>"(O)390","fiber"=>"(O)771","hardwood"=>"(O)709",_=>""});
            if(item.Length>0)return CapacityAdapter.Receive(p,ItemRegistry.Create(item));
            if(goal is "harvest" or "forage" or "cleanup" or "fish" or "mine_trip" or "volcano_trip" or "milk" or "shear" or "animal_collect")return CapacityPlan.Simulate(CapacityAdapter.Of(p),new CapacityOp[]{new RequireFreeOp(CapacityPlan.RequiredSlots(goal,1))});
        }
        if(tool is "player.collect_reward" or "player.claim_reward" or "player.find_lost_item" or "player.geodes" or "player.fish" or "player.crab_pots")return CapacityPlan.Simulate(CapacityAdapter.Of(p),new CapacityOp[]{new RequireFreeOp(1)});
        return null;
    }
    private void GuardCapacity(string actor,string tool,JsonElement args) {
        if(actor!="player")return;
        RefreshCapacityVersion();var state=Data.Autoplay.Capacity;
        if(!state.Constraints.Any(c=>c.Actor==actor&&c.CapacityVersion==state.Version&&CapacityState.IsCapacity(c.RootCause)))return;
        if(tool=="work.run"&&AgentToolRegistry.Text(args,"goal") is "store" or "storage_expand"&&state.Constraints.Any(c=>c.Actor==actor&&c.CapacityVersion==state.Version&&c.RootCause=="capacity_all_candidates_infeasible")) {
            Data.Autoplay.Record("capacity_dispatch_blocked",AgentJson.Encode(new{actor,tool,state.Version,reason="relief_already_proved_infeasible",excluded=state.Constraints.Where(c=>c.Actor==actor).SelectMany(c=>c.ExcludedCandidates),alternatives="重新检查实际可行的生产消耗与仓储材料前置；不要重复存货"}));
            throw new InvalidOperationException("known_failure_conditions_unchanged:capacity_relief:"+state.Version);
        }
        var plan=CapacityAdmission(tool,args);
        if(plan is {Feasible:false}) {
            Data.Autoplay.Record("capacity_dispatch_blocked",AgentJson.Encode(new{actor,tool,state.Version,plan,alternatives=new[]{"inspect actual feasible work and missing storage prerequisites"}}));
            throw new InvalidOperationException("known_failure_conditions_unchanged:capacity:"+state.Version);
        }
    }
}
