using System.Text.Json;
using StardewValley;
using StardewValley.Objects;
using StardewValley.SpecialOrders.Objectives;
using Microsoft.Xna.Framework;

namespace Together;
public sealed partial class ModEntry {
    private bool StartCapacityRelief(SemanticJob job) {
        if(job.ReliefAction=="expand")return TryStartStorageExpansion(job);
        CompactPlayerStacks();
        if(job.goal!="store"&&!PlayerNeedsWorkStorage(job)&&(!job.PickupPending||CapacityAdapter.HasSlots(Game1.player,1))){job.Storing=false;job.ReliefDepth=0;return true;}
        if(job.goal=="store"&&CapacityAdapter.HasSlots(Game1.player,Math.Max(1,job.RequiredSlots))&&job.ReliefDepth>0){StopSemanticWork(job,"stored_available_cargo",true);return true;}
        RefreshFacts(true);
        var p=Game1.player;var candidates=new List<CapacityCandidate>();var execute=new Dictionary<string,Action>();
        void Exclude(string id,int priority,string reason,bool authorized=true)=>candidates.Add(new(id,priority,0,authorized,reason,Array.Empty<CapacityOp>()));
        var bag=new CapacityAdapter(p);
        bool Reachable(GameLocation l,Vector2 tile)=>l==Game1.currentLocation?WorkStand(l,tile.ToPoint()).HasValue:
            PlayerExecutor.NextExit(Game1.currentLocation,l.NameOrUniqueName)!=null&&new[]{new Point((int)tile.X+1,(int)tile.Y),new Point((int)tile.X-1,(int)tile.Y),new Point((int)tile.X,(int)tile.Y+1),new Point((int)tile.X,(int)tile.Y-1)}.Any(at=>PlayerExecutor.Passable(l,at));
        // Only already-approved queue items may be advanced for productive relief.
        // Their own native verifiers still execute and finish the original task.
        var approved=Data.Autoplay.Schedule.Tasks.Where(t=>t.state=="queued"&&t.spec.actor=="player"&&t.spec.day==Game1.Date.TotalDays&&t.spec.not_before<=Game1.timeOfDay&&t.spec.after.All(id=>Data.Autoplay.Schedule.Tasks.Any(d=>d.spec.id==id&&d.state=="succeeded"))).ToArray();
        foreach(var (tool,priority,id) in new[]{("player.machine",1,"machine"),("player.order_donate",2,"delivery"),("player.craft",3,"craft")}) {
            bool found=false;
            foreach(var task in approved.Where(t=>t.spec.tool==tool)) {
                found=true;var args=task.spec.args;string candidate=id+":"+task.spec.id;
                try {
                    if(LoadoutSafety.Unsupported(tool,AgentToolRegistry.Text(args,"mode")) is {} shape){string root="loadout_shape_unsupported:"+shape;RecordCapacityConstraint(root);throw new InvalidOperationException(root);}
                    if(AgentToolRegistry.Text(args,"location",Game1.currentLocation.NameOrUniqueName)!=Game1.currentLocation.NameOrUniqueName)throw new InvalidOperationException("not_at_verified_service_location");
                    var spending=new Dictionary<Item,int>();Item? output=null;
                    if(tool=="player.craft") {
                        string recipeName=AgentToolRegistry.Text(args,"recipe");
                        if(!p.craftingRecipes.ContainsKey(recipeName))throw new InvalidOperationException("recipe_not_known");
                        if(AgentToolRegistry.Number(args,"count",1)!=1)throw new InvalidOperationException("multi_batch_relief_requires_separate_approved_step");
                        var recipe=new CraftingRecipe(recipeName,false);spending=CapacityAdapter.Ingredients(p,recipe);output=recipe.createItem();
                    }else if(tool=="player.order_donate") {
                        string orderId=AgentToolRegistry.Text(args,"order"),box=AgentToolRegistry.Text(args,"dropbox");
                        var order=p.team.specialOrders.FirstOrDefault(o=>o.questKey.Value==orderId&&o.UsesDropBox(box))??throw new InvalidOperationException("accepted_order_required");
                        var objective=order.objectives.OfType<DonateObjective>().First(d=>d.dropBox.Value==box);
                        if(objective.GetDropboxLocationName()!=Game1.currentLocation.NameOrUniqueName)throw new InvalidOperationException("not_at_verified_service_location");
                        // One eligible stack: avoid double-counting shared objective limits.
                        var item=p.Items.FirstOrDefault(i=>i!=null&&order.GetAcceptCount(i)>=i.Stack)??throw new InvalidOperationException("no_full_stack_accepted");
                        spending[item]=item.Stack;
                    }else {
                        if(AgentToolRegistry.Text(args,"mode","collect")!="load")throw new InvalidOperationException("collection_does_not_release_capacity");
                        string itemId=AgentToolRegistry.Text(args,"item"),kind=AgentToolRegistry.Text(args,"machine");
                        var input=p.Items.FirstOrDefault(i=>i?.QualifiedItemId==itemId)??throw new InvalidOperationException("machine_input_missing");
                        var machine=Game1.currentLocation.objects.Pairs.FirstOrDefault(m=>m.Value.heldObject.Value==null&&m.Value.GetMachineData()!=null&&(kind.Length==0||m.Value.QualifiedItemId==kind)&&Reachable(Game1.currentLocation,m.Key));
                        if(machine.Value==null)throw new InvalidOperationException("no_reachable_idle_machine");
                        spending=PlayerExecutor.MachineConsumption(p,machine.Value,input);
                    }
                    ValidatePlayerConsumption(spending,AgentToolRegistry.Text(args,"goal_id"),tool=="player.order_donate"?"order:"+AgentToolRegistry.Text(args,"order"):output?.QualifiedItemId??AgentToolRegistry.Text(args,"output"));
                    var a=new CapacityAdapter(p,output==null?null:new[]{output});var ops=spending.Select(v=>a.Take(v.Key,v.Value)).ToList();if(output!=null)ops.Add(a.Put(output));
                    candidates.Add(new(candidate,priority,0,true,"",ops));
                    execute[candidate]=()=>{job.ReliefDepth=1;job.ReliefAction=candidate;WorkChild(job,tool,args,"capacity_relief");Data.Autoplay.Schedule.Started(task,job.child_id!);};
                }catch(InvalidOperationException e){Exclude(candidate,priority,e.Message);}
            }
            if(!found)Exclude(id,priority,"no_ready_approved_task");
        }
        var food=p.Items.Select((item,slot)=>(item,slot)).FirstOrDefault(x=>x.item is StardewValley.Object o&&o.Stack==1&&NativeFoodRules.Block(o)==null&&CanConsumeOne(o)&&!Facts.Bundles.Any(b=>!b.Complete&&b.Missing.Any(r=>r.Item==o.QualifiedItemId))&&p.Stamina<p.MaxStamina&&job.FoodUsed<job.MaxFood);
        if(food.item==null)Exclude("food",4,"no_nonreserved_useful_single_food");
        else {
            candidates.Add(new("food",4,0,true,"",new[]{bag.Take(food.item,1)}));
            execute["food"]=()=>{job.ReliefDepth=1;job.ReliefAction="food";WorkChild(job,"player.eat",new{slot=food.slot},"capacity_relief");job.FoodUsed++;};
        }
        bool storageFound=false;
        foreach(var storage in SharedStorage()) {
            storageFound=true;string id="store:"+storage.Location.NameOrUniqueName+":"+storage.Tile;
            if(storage.Chest.GetMutex().IsLocked()||job.Excluded.Contains("storage:"+storage.Location.NameOrUniqueName+":"+(int)storage.Tile.X+":"+(int)storage.Tile.Y)){Exclude(id,5,"storage_busy_or_already_tried");continue;}
            if(!Reachable(storage.Location,storage.Tile)){Exclude(id,5,"storage_route_or_interaction_site_unavailable");continue;}
            var storable=p.Items.Where(i=>i!=null&&StoreCount(i)>0).ToArray();
            var target=new CapacityAdapter(storage.Chest.GetActualCapacity(),storage.Chest.GetItemsForPlayer(),storable);var puts=new List<CapacityOp>();var takes=new List<CapacityOp>();
            foreach(var item in storable) {
                int low=0,high=StoreCount(item);
                while(low<high){int mid=(low+high+1)/2;if(CapacityPlan.Simulate(target.Snapshot,puts.Append(target.Put(item,mid)).ToArray()).Feasible)low=mid;else high=mid-1;}
                if(low>0){puts.Add(target.Put(item,low));takes.Add(bag.Take(item,low));}
            }
            candidates.Add(new(id,5,storage.Location==Game1.currentLocation?Vector2.Distance(storage.Tile,p.Tile):100,true,"",takes));
            execute[id]=()=>{job.ReliefDepth=1;job.ReliefAction=id;job.StorageTile=storage.Tile.ToPoint();job.StorageLocation=storage.Location.NameOrUniqueName;};
        }
        if(!storageFound)Exclude("store",5,"no_designated_storage");
        try {
            if(!Data.Storage.AutoExpand||SharedStorage().Count()>=Data.Storage.MaxSharedChests)throw new InvalidOperationException("storage_policy_or_limit");
            if(!p.craftingRecipes.ContainsKey("Chest"))throw new InvalidOperationException("chest_recipe_not_known");
            var chest=p.Items.FirstOrDefault(i=>i?.QualifiedItemId=="(BC)130");var recipe=new CraftingRecipe("Chest",false);
            var spending=chest==null?CapacityAdapter.Ingredients(p,recipe):new Dictionary<Item,int>();
            int reserved=Data.Storage.BudgetDay==Game1.Date.TotalDays?Data.Storage.WoodReserved:0;
            if(chest==null&&reserved+recipe.recipeList.GetValueOrDefault("388")>Data.Storage.WoodBudgetPerDay)throw new InvalidOperationException("storage_expansion_budget");
            ValidatePlayerConsumption(spending,"","(BC)130");
            var output=chest??recipe.createItem();var a=new CapacityAdapter(p,new[]{output});var ops=spending.Select(v=>a.Take(v.Key,v.Value)).ToList();
            if(chest==null)ops.Add(a.Put(output));ops.Add(a.Take(output,1));
            candidates.Add(new("expand",6,Game1.currentLocation==Game1.getFarm()?0:40,true,"",ops));
            execute["expand"]=()=>{job.ReliefDepth=1;job.ReliefAction="expand";if(!TryStartStorageExpansion(job))throw new InvalidOperationException("capacity_no_reachable_storage");};
        }catch(InvalidOperationException e){Exclude("expand",6,e.Message);}
        Exclude("sale",7,"requires_explicit_surplus_sale_task",false);Exclude("backpack_upgrade",8,"requires_explicit_purchase_budget_and_keep_gold",false);
        var choice=CapacityRelief.Choose(bag.Snapshot,candidates,job.ReliefDepth);
        job.evidence.Add(new{kind="capacity_relief_plan",choice});Data.Autoplay.Record("capacity_relief_plan",AgentJson.Encode(new{job.command_id,choice}));
        if(choice.Selected==null){if(choice.RootCause!="relief_depth_limit")RecordCapacityConstraint(choice.RootCause,job.actor,choice.Candidates.Select(c=>c.Id+":"+c.Reason).ToArray());StopSemanticWork(job,choice.RootCause);return true;}
        execute[choice.Selected]();return true;
    }
}
