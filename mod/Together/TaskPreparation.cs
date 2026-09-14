using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;

namespace Together;
public sealed partial class ModEntry {
    private sealed record KitNeed(string Label,int Count,Func<Item,bool> Accept);
    private sealed record KitTransfer(Item Item,int Count,bool IntoBag);
    private sealed class KitPlan {
        public ScheduledAgentTask Task=null!;
        public string Origin="",Child="",Phase="",StorageLocation="";
        public Point OriginTile,ChestTile;
        public List<KitNeed> Needs=new();
        public bool KeepUnknown,Departure,Outputs;
        public Item? ExpectedOutput;
        public List<string> Reasons=new();
        public DateTime Started=DateTime.UtcNow;
    }
    private KitPlan? preparation;
    private readonly HashSet<string> preparedTasks=new();
    private string preparationSummary="尚无整备任务";
    private void ResetPreparation(){preparation=null;preparedTasks.Clear();preparationSummary="等待任务";}
    private List<ScheduledAgentTask> KitTrip(ScheduledAgentTask task) {
        var trip=new List<ScheduledAgentTask>{task};
        // Follow explicit same-intent dependencies only. Tool requirements are a
        // union; ordered outputs offset later consumables without creating stock.
        for(int n=0;n<3;n++) {
            var next=Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.state=="queued"&&t.spec.actor=="player"&&t.spec.intent_id==task.spec.intent_id&&!trip.Contains(t)&&t.spec.after.Contains(trip[^1].spec.id)&&t.spec.day==Game1.Date.TotalDays);
            if(next==null)break;trip.Add(next);
        }
        return trip;
    }
    private void RejectKitShape(ScheduledAgentTask task,string shape) {
        string root="loadout_shape_unsupported:"+shape;RefreshCapacityVersion();
        bool repeat=Data.Autoplay.Capacity.Constraints.Any(c=>c.Actor==task.spec.actor&&c.RootCause==root&&c.CapacityVersion==Data.Autoplay.Capacity.Version);
        RecordCapacityConstraint(root,task.spec.actor);
        Data.Autoplay.Record("loadout_shape_rejected",AgentJson.Encode(new{task.spec.id,task.spec.tool,shape,root,repeat,version=Data.Autoplay.Capacity.Version,transfer_started=false}));
        throw new InvalidOperationException(repeat?"known_failure_conditions_unchanged:"+root:root);
    }
    private KitPlan DescribeKit(ScheduledAgentTask task) {
        var plan=new KitPlan{Task=task,Origin=Game1.currentLocation.NameOrUniqueName,OriginTile=Game1.player.TilePoint};
        var trip=KitTrip(task);
        var future=new PreparationCredits<Item>();var required=new List<KitNeed>();var toolLabels=new HashSet<string>();
        foreach(var t in trip)if(LoadoutSafety.Unsupported(t.spec.tool,AgentToolRegistry.Text(t.spec.args,"mode")) is {} shape)RejectKitShape(task,shape);
        void Tool<T>(string label) where T:Item {if(toolLabels.Add(label))required.Add(new(label,1,i=>i is T));}
        void Id(string id,int count){if(id.Length>0&&count>0)plan.Needs.Add(new(id,count,i=>i.QualifiedItemId==id));}
        foreach(var t in trip) {
            plan.Needs=new();
            var a=t.spec.args;string tool=t.spec.tool,goal=AgentToolRegistry.Text(a,"goal");int count=Math.Max(1,AgentToolRegistry.Number(a,"count",1));
            if(tool=="work.run") {
                switch(goal) {
                    case "plant":
                        if(!farmPlantPlans.TryGetValue(AgentToolRegistry.Text(a,"plan_id"),out var field)||field.Day!=Game1.Date.TotalDays||field.Epoch!=agentSaveEpoch)throw new InvalidOperationException("read_farm_plan_first");
                        Tool<Hoe>("锄头");Tool<WateringCan>("水壶");Tool<Axe>("斧头");Tool<Pickaxe>("镐");Tool<MeleeWeapon>("镰刀/防身工具");
                        int unplanted=field.Tiles.Count(t=>Game1.getLocationFromName(field.Location)?.terrainFeatures.GetValueOrDefault(new(t.X,t.Y)) is not StardewValley.TerrainFeatures.HoeDirt {crop:not null});
                        Id(field.Seed,unplanted);Id(field.Fertilizer,unplanted);break;
                    case "cleanup":Tool<Axe>("斧头");Tool<Pickaxe>("镐");Tool<MeleeWeapon>("镰刀/防身工具");plan.Outputs=true;break;
                    case "wood":Tool<Axe>("斧头");plan.Outputs=true;plan.ExpectedOutput=ItemRegistry.Create("(O)388");break;
                    case "stone":case "hardwood":case "resource":
                        if(goal=="hardwood")Tool<Axe>("斧头");else Tool<Pickaxe>("镐");plan.Outputs=true;plan.ExpectedOutput=ItemRegistry.Create(goal=="stone"?"(O)390":goal=="hardwood"?"(O)709":AgentToolRegistry.Text(a,"item"));break;
                    case "fiber":Tool<MeleeWeapon>("镰刀/防身工具");plan.Outputs=true;plan.ExpectedOutput=ItemRegistry.Create("(O)771");break;
                    case "water":case "refill":Tool<WateringCan>("水壶");break;
                    case "clear_dead":Tool<MeleeWeapon>("镰刀/防身工具");break;
                    case "harvest":case "forage":case "animal_collect":plan.Outputs=true;break;
                    case "milk":Tool<MilkPail>("奶桶");plan.Outputs=true;break;
                    case "shear":Tool<Shears>("剪刀");plan.Outputs=true;break;
                    case "fish":Tool<FishingRod>("鱼竿");plan.Outputs=true;plan.Departure=true;break;
                    case "mine_trip":case "volcano_trip":Tool<Pickaxe>("镐");Tool<MeleeWeapon>("防身武器");Tool<WateringCan>("水壶");plan.Outputs=true;plan.Departure=true;break;
                    case "feed":case "pet":break;
                    default:plan.KeepUnknown=true;break;
                }
            }else if(tool is "player.craft" or "player.cook") {
                string recipeName=AgentToolRegistry.Text(a,"recipe");bool cooking=tool=="player.cook";
                if(!(cooking?Game1.player.cookingRecipes:Game1.player.craftingRecipes).ContainsKey(recipeName))throw new InvalidOperationException("recipe_not_known");
                var recipe=new CraftingRecipe(recipeName,cooking);
                foreach(var need in recipe.recipeList){string ingredient=need.Key;plan.Needs.Add(new("配方材料:"+ingredient,checked(need.Value*count),i=>CraftingRecipe.ItemMatchesForCrafting(i,ingredient)));}
                // Seasoning changes the native output quality; preserve it rather
                // than promising a lower-quality output or eating the last unit.
                if(cooking)Id("(O)917",Game1.player.Items.Where(i=>i?.QualifiedItemId=="(O)917").Sum(i=>i.Stack));
            }else if(tool=="player.machine") {
                if(AgentToolRegistry.Text(a,"mode","collect")=="load") {
                    var recipe=goalRecipes.Values.FirstOrDefault(r=>r.Kind=="process"&&r.Facility==AgentToolRegistry.Text(a,"machine")&&r.Item==AgentToolRegistry.Text(a,"output")&&r.Inputs.FirstOrDefault()?.Item==AgentToolRegistry.Text(a,"item"));
                    if(recipe!=null)foreach(var need in recipe.Inputs)Id(need.Item,checked(need.Count*count));
                    else {Id(AgentToolRegistry.Text(a,"item"),count);plan.KeepUnknown=true;}
                }else plan.Outputs=true;
            }else if(tool is "player.buy" or "player.procure") {
                plan.Outputs=true;plan.ExpectedOutput=AgentToolRegistry.Text(a,"item") is {Length:>0} id?ItemRegistry.Create(id,count):null;
                Id(AgentToolRegistry.Text(a,"trade_item"),AgentToolRegistry.Number(a,"trade_budget",0));
            }else if(tool is "player.move" or "player.travel" or "player.service") {
                plan.Departure=tool=="player.travel";if(trip.Count==1)plan.KeepUnknown=true;
            }else if(tool is "player.sleep" or "player.read_mail" or "player.watch_tv" or "player.collect_home_gifts") { }
            else if(tool=="player.place_facility")Id(AgentToolRegistry.Text(a,"item"),1);
            else if(tool=="player.order_donate") {
                var order=Game1.player.team.specialOrders.FirstOrDefault(o=>o.questKey.Value==AgentToolRegistry.Text(a,"order")&&o.UsesDropBox(AgentToolRegistry.Text(a,"dropbox")))??throw new InvalidOperationException("active_order_dropbox_required");
                foreach(var i in Game1.player.Items.Where(i=>i!=null&&order.GetAcceptCount(i)>0))plan.Needs.Add(new("交付:"+i.QualifiedItemId,Math.Min(i.Stack,order.GetAcceptCount(i)),other=>ReferenceEquals(i,other)));
                plan.KeepUnknown=true;
            }else {plan.KeepUnknown=true;}
            foreach(var need in plan.Needs){int net=future.Require(need.Count,need.Accept);if(net>0)required.Add(need with{Count=net});}
            // Only dependencies earlier in this trip can supply later requirements.
            // Native execution will re-read stocks before each task starts.
            if(tool is "player.buy" or "player.procure") {
                string id=AgentToolRegistry.Text(a,"item");
                if(quoteEpoch==agentSaveEpoch&&observedProducts.TryGetValue(AgentToolRegistry.Text(a,"shop")+":"+id,out var q)&&q.Day==Game1.Date.TotalDays)future.Produce(q.Item,checked(count*q.Units));
            }else if(tool is "player.craft" or "player.cook") {
                var recipe=new CraftingRecipe(AgentToolRegistry.Text(a,"recipe"),tool=="player.cook");var output=recipe.createItem();future.Produce(output,checked(output.Stack*count));
            }
        }
        plan.Needs=required;
        // Do not require food to exist. Keep a bounded amount of an already
        // allowed food; never take a quest/bundle reservation as travel food.
        var food=Game1.player.Items.OfType<StardewValley.Object>().Where(i=>i.Edibility>0&&CanConsumeOne(i)&&!Facts.Bundles.Any(b=>!b.Complete&&b.Missing.Any(m=>m.Item==i.QualifiedItemId))).OrderByDescending(i=>i.staminaRecoveredOnConsumption()).FirstOrDefault();
        if(food!=null)Id(food.QualifiedItemId,Math.Min(food.Stack,(int)Math.Ceiling((plan.Departure?80:40)/(double)Math.Max(1,food.staminaRecoveredOnConsumption()))));
        plan.Departure=plan.Departure||trip.Any(t=>AgentToolRegistry.Text(t.spec.args,"location") is {Length:>0} location&&location!=plan.Origin);
        return plan;
    }
    private Dictionary<Item,int> AllocateKit(KitPlan plan,IEnumerable<Item> sources,out List<string> missing) {
        var keep=new Dictionary<Item,int>(ReferenceEqualityComparer.Instance);missing=new();var items=sources.ToArray();
        foreach(var need in plan.Needs) {
            int left=need.Count;
            foreach(var i in items.Where(need.Accept)) {
                int take=Math.Min(left,Math.Max(0,i.Stack-keep.GetValueOrDefault(i)));if(take>0){keep[i]=keep.GetValueOrDefault(i)+take;left-=take;}if(left==0)break;
            }
            if(left>0)missing.Add(need.Label+" 缺 "+left);
        }
        return keep;
    }
    private static bool KitStorable(Item item)=>item is Tool||item is StardewValley.Object o&&!o.questItem.Value&&!o.IsRecipe;
    private bool KitProtected(Item item)=>ConsumptionRequirements(protectProgress:true).Any(r=>r.Quality<=item.Quality&&(r.Item==item.QualifiedItemId||r.Item==item.Category.ToString()));
    private (PackingResult Result,List<KitTransfer> Transfers) PackKit(KitPlan plan,Chest chest) {
        var bag=Game1.player.Items.Where(i=>i!=null).ToArray();var stored=chest.GetItemsForPlayer().Where(i=>i!=null).ToArray();
        var keep=AllocateKit(plan,bag.Concat(stored),out var missing);
        if(missing.Count>0)return(new(false,Array.Empty<PackingMove>(),"loadout_missing:"+string.Join(";",missing)),new());
        var transfers=new List<KitTransfer>();
        foreach(var item in bag)if(!plan.KeepUnknown&&KitStorable(item)&&!KitProtected(item)&&item.Stack>keep.GetValueOrDefault(item))transfers.Add(new(item,item.Stack-keep.GetValueOrDefault(item),false));
        foreach(var item in stored)if(keep.GetValueOrDefault(item)>0)transfers.Add(new(item,keep[item],true));
        var a=new CapacityAdapter(Game1.player,stored);
        var other=new CapacitySnapshot(chest.GetActualCapacity(),stored.Select(i=>(a.Key(i),i.Stack,a.Limit(i),false)));
        // Deposits are optional. A nearly full chest should not reject a useful
        // subset that frees enough space; all required withdrawals remain mandatory.
        var deposits=transfers.Where(t=>!t.IntoBag).OrderByDescending(t=>t.Count==t.Item.Stack).ThenByDescending(t=>t.Count).ToArray();
        var withdrawals=transfers.Where(t=>t.IntoBag).ToArray();
        PackingResult result=new(false,Array.Empty<PackingMove>(),"loadout_intermediate_capacity_or_stock_blocked");
        for(int count=deposits.Length;count>=0;count--) {
            var chosen=deposits.Take(count).Concat(withdrawals).ToArray();
            var moves=chosen.Select((t,n)=>new PackingMove(n.ToString(),t.IntoBag,a.Key(t.Item),t.Count,a.Limit(t.Item))).ToArray();
            result=LoadoutPlan.Arrange(a.Snapshot,other,moves);
            if(result.Feasible)return(result,result.Moves.Select(m=>chosen[int.Parse(m.Id)]).ToList());
        }
        return(result,new());
    }
    private bool PrepareTaskKit(ScheduledAgentTask task) {
        if(task.spec.actor!="player"||preparedTasks.Contains(task.spec.id))return true;
        if(task.spec.tool=="work.run"&&AgentToolRegistry.Text(task.spec.args,"goal") is "store" or "withdraw" or "storage_expand")return true;
        if(task.spec.tool is "player.sleep" or "player.collect_home_gifts")return true;
        var plan=DescribeKit(task);AllocateKit(plan,Game1.player.Items.Where(i=>i!=null),out var missing);
        // Validate the whole declared shape even if a native menu is open.
        var stores=SharedStorage().ToArray();
        bool Sufficient(IEnumerable<Item> items){AllocateKit(plan,items,out var absent);return absent.Count==0;}
        var bag=Game1.player.Items.Where(i=>i!=null).ToArray();
        if(LoadoutSafety.MultipleStorage(missing.Count==0,stores.Select(s=>Sufficient(bag.Concat(s.Chest.GetItemsForPlayer().Where(i=>i!=null)))),Sufficient(bag.Concat(stores.SelectMany(s=>s.Chest.GetItemsForPlayer().Where(i=>i!=null))))))RejectKitShape(task,"multiple_storage");
        if(Game1.activeClickableMenu!=null)return true;
        bool shortage=plan.Outputs&&(plan.ExpectedOutput==null?CapacityAdapter.Of(Game1.player).FreeSlots==0:!CapacityAdapter.CanReceive(Game1.player,plan.ExpectedOutput));
        // A consuming task may already fit with a full bag. Let its ordered
        // native capacity model decide; don't demand a cosmetic empty slot.
        if(task.spec.tool is "player.craft" or "player.cook"&&missing.Count==0)shortage=CapacityAdmission(task.spec.tool,task.spec.args) is {Feasible:false};
        // Proximity is not a reason to unload. Continue a fully equipped task
        // while its predicted output fits; ordinary work owns any later full-bag detour.
        var needed=AllocateKit(plan,bag,out _);
        bool unload=plan.Departure&&!plan.KeepUnknown&&bag.Any(i=>i is not Tool&&KitStorable(i)&&!KitProtected(i)&&i.Stack>needed.GetValueOrDefault(i));
        if(missing.Count==0&&!shortage&&!unload){preparedTasks.Add(task.spec.id);return true;}
        var candidates=new List<(GameLocation Location,Vector2 Tile,Chest Chest,int Cost)>();
        var excluded=new List<string>();
        foreach(var s in SharedStorage()) {
            if(unload&&missing.Count==0&&!shortage&&s.Location.NameOrUniqueName!=plan.Origin)continue;
            if(s.Chest.GetMutex().IsLocked()){excluded.Add("storage_busy");continue;}
            var pack=PackKit(plan,s.Chest);if(!pack.Result.Feasible){excluded.Add(pack.Result.Reason);continue;}
            if(pack.Transfers.Count==0)continue;
            int cost;
            if(s.Location==Game1.currentLocation){var stand=WorkStand(s.Location,s.Tile.ToPoint());if(!stand.HasValue){excluded.Add("storage_unreachable");continue;}var path=PlayerExecutor.PreviewPath(s.Location,stand.Value);if(path==null&&stand.Value!=Game1.player.TilePoint){excluded.Add("storage_unreachable");continue;}cost=path?.Count??0;}
            else {if(PlayerExecutor.NextExit(Game1.currentLocation,s.Location.NameOrUniqueName)==null){excluded.Add("storage_route_unavailable");continue;}cost=100;}
            if(missing.Count==0&&!shortage&&!plan.Departure&&cost>6)continue;
            candidates.Add((s.Location,s.Tile,s.Chest,cost));
        }
        if(candidates.Count==0) {
            Data.Autoplay.Record("loadout_checked",AgentJson.Encode(new{task.spec.id,missing,shortage,excluded,decision=missing.Count==0?"continue_native_capacity_or_relief":"blocked_missing_kit"}));
            if(missing.Count>0)throw new InvalidOperationException("loadout_missing:"+string.Join(";",missing));
            preparedTasks.Add(task.spec.id);return true;
        }
        var best=candidates.OrderBy(c=>c.Cost).First();plan.StorageLocation=best.Location.NameOrUniqueName;plan.ChestTile=best.Tile.ToPoint();plan.Phase="to_storage";preparation=plan;
        preparationSummary="整备：去仓库存无关物资、取本次所需";
        Data.Autoplay.Record("loadout_started",AgentJson.Encode(new{task.spec.id,origin=plan.Origin,storage=plan.StorageLocation,tile=plan.ChestTile,needs=plan.Needs.Select(n=>new{n.Label,n.Count}),missing,shortage,unload,estimated_path=best.Cost,excluded}));
        return false;
    }
    private bool TickTaskPreparation() {
        var p=preparation;if(p==null)return false;
        Chest? transferChest=null;KitItem[]? transferBefore=null;bool transferring=false;
        try {
            if(p.Task.Terminal){if(p.Child.Length>0&&playerExecutor.Current?.command_id==p.Child)playerExecutor.Cancel();preparation=null;return true;}
            if(p.Task.spec.day!=Game1.Date.TotalDays||Game1.timeOfDay>p.Task.spec.deadline)throw new InvalidOperationException("loadout_parent_cancelled_or_expired");
            if((DateTime.UtcNow-p.Started).TotalSeconds>150)throw new InvalidOperationException("loadout_route_timeout");
            if(p.Child.Length>0) {
                var action=playerExecutor.Current;if(action?.command_id!=p.Child)throw new InvalidOperationException("loadout_child_replaced");
                if(action.status=="running")return true;if(action.status!="succeeded")throw new InvalidOperationException(action.error??"loadout_move_failed");p.Child="";
            }
            if(Game1.activeClickableMenu!=null||Game1.fadeToBlack||!Game1.player.CanMove)return true;
            void Move(string skill,object args){var r=JsonSerializer.SerializeToElement(playerExecutor.Start(skill,JsonSerializer.SerializeToElement(args)),AgentJson.Options);p.Child=r.GetProperty("command_id").GetString()!;}
            if(p.Phase=="return") {
                if(p.Departure){preparedTasks.Add(p.Task.spec.id);preparation=null;preparationSummary="出行整备完成，从仓库继续原行程";Data.Autoplay.Record("loadout_completed",AgentJson.Encode(new{p.Task.spec.id,departure=true}));return false;}
                if(Game1.currentLocation.NameOrUniqueName!=p.Origin){Move("player.travel",new{location=p.Origin});return true;}
                if(Game1.player.TilePoint!=p.OriginTile){Move("player.move",new{x=p.OriginTile.X,y=p.OriginTile.Y});return true;}
                preparedTasks.Add(p.Task.spec.id);preparation=null;preparationSummary="整备完成，继续原任务";Data.Autoplay.Record("loadout_completed",AgentJson.Encode(new{p.Task.spec.id}));return false;
            }
            if(Game1.currentLocation.NameOrUniqueName!=p.StorageLocation){Move("player.travel",new{location=p.StorageLocation});return true;}
            if(!Game1.currentLocation.objects.TryGetValue(p.ChestTile.ToVector2(),out var obj)||obj is not Chest chest||!OutputChest(chest))throw new InvalidOperationException("loadout_storage_changed");
            if(chest.GetMutex().IsLocked())throw new InvalidOperationException("loadout_storage_busy");
            if(Math.Abs(Game1.player.TilePoint.X-p.ChestTile.X)+Math.Abs(Game1.player.TilePoint.Y-p.ChestTile.Y)>1){var at=WorkStand(Game1.currentLocation,p.ChestTile)??throw new InvalidOperationException("loadout_storage_unreachable");Move("player.move",new{x=at.X,y=at.Y});return true;}
            // Revalidate before the first transfer: stock may have changed while walking.
            foreach(var t in KitTrip(p.Task))if(LoadoutSafety.Unsupported(t.spec.tool,AgentToolRegistry.Text(t.spec.args,"mode")) is {} shape)RejectKitShape(p.Task,shape);
            var pack=PackKit(p,chest);if(!pack.Result.Feasible)throw new InvalidOperationException(pack.Result.Reason);
            var before=KitInventoryEvidence(chest);transferChest=chest;transferBefore=before;transferring=true;Data.Autoplay.Record("loadout_transfer_before",AgentJson.Encode(new{p.Task.spec.id,before}));var rows=new List<object>();
            foreach(var move in pack.Transfers) {
                var src=move.IntoBag?chest.GetItemsForPlayer():Game1.player.Items;
                int index=src.IndexOf(move.Item);if(index<0||src[index].Stack<move.Count)throw new InvalidOperationException("loadout_stock_changed");
                // Preserve identity for tools/attachments. Partial ordinary stacks
                // use a native clone. Only accepted units leave their source.
                bool whole=move.Count==move.Item.Stack;var item=whole?move.Item:move.Item.getOne();item.Stack=move.Count;
                if(whole)src[index]=null;else move.Item.Stack-=move.Count;
                var remainder=move.IntoBag?Game1.player.addItemToInventory(item):chest.addItem(item);
                int left=remainder?.Stack??0;
                if(left>0){if(whole)src[index]=remainder;else move.Item.Stack+=left;}
                rows.Add(new{item=item.QualifiedItemId,quality=item.Quality,count=move.Count-left,into_bag=move.IntoBag});
                if(left>0)throw new InvalidOperationException("loadout_native_capacity_changed");
                ProbeLoadoutInterruption(rows.Count);
            }
            var after=KitInventoryEvidence(chest);
            Data.Autoplay.Record("loadout_transferred",AgentJson.Encode(new{p.Task.spec.id,before,after,transfers=rows}));
            if(!KitTotals(before).OrderBy(x=>x.Key).SequenceEqual(KitTotals(after).OrderBy(x=>x.Key)))throw new InvalidOperationException("loadout_conservation_failed");
            Data.Autoplay.Memory.Diary.Transfers(p.Task.spec.id+":loadout:"+FailureKnowledge.Hash(AgentJson.Encode(before)),JsonSerializer.SerializeToElement(rows),Game1.Date.TotalDays,Game1.timeOfDay,p.StorageLocation+":"+p.ChestTile);
            transferring=false;p.Phase="return";preparationSummary="已完成真实存取，返回原任务";
        }catch(Exception e){
            var task=p.Task;preparation=null;string reason=LoadoutSafety.Normalize(e.Message);preparationSummary="整备阻碍："+reason;
            KitItem[]? after=null;bool? conserved=null;string verificationError="";
            if(transferring) {
                try {
                    var location=Game1.getLocationFromName(p.StorageLocation);
                    if(transferBefore==null||transferChest==null||location==null||!location.objects.TryGetValue(p.ChestTile.ToVector2(),out var live)||!ReferenceEquals(live,transferChest))throw new InvalidOperationException("transfer_container_identity_unverifiable");
                    after=KitInventoryEvidence(transferChest);
                    conserved=KitTotals(transferBefore).OrderBy(x=>x.Key).SequenceEqual(KitTotals(after).OrderBy(x=>x.Key));
                }catch(Exception verify){verificationError=verify.Message;}
            }
            Data.Autoplay.Record("loadout_failed",AgentJson.Encode(new{task.spec.id,reason,phase=p.Phase,transferring,before=transferBefore,after,conserved,verification_error=verificationError,disposition=transferring&&LoadoutSafety.MustStop(reason,conserved)?"stop_conservation":"abandon_replan_actual_stock"}));
            if(transferring&&LoadoutSafety.MustStop(reason,conserved)){PauseAutoplay("loadout_conservation_failed:"+reason+":"+verificationError);return true;}
            // Accepted native transfers remain accepted. Never undo them to match
            // the plan: the next task reads actual stocks and plans from scratch.
            if(transferring)RefreshCapacityVersion();
            CompleteScheduled(task,JsonSerializer.SerializeToElement(new{status="failed",error=reason,conserved,inventory=after,resume_policy="replan_actual_stock"}));
        }
        return true;
    }
    private string labLoadoutFault="";
    private void ProbeLoadoutInterruption(int moved) {
        if(moved!=1||labLoadoutFault.Length==0)return;
        if(!Settings.EnableLab||!Context.IsWorldReady||Game1.player.Name!="AgentLab")throw new InvalidOperationException("lab_fault_gate_denied");
        string fault=labLoadoutFault;labLoadoutFault="";throw new InvalidOperationException(fault);
    }
    private sealed record KitItem(string Container,int Slot,string Id,int Quality,int Count);
    private static KitItem[] KitInventoryEvidence(Chest chest)=>Game1.player.Items.Select((i,n)=>(i,n)).Where(x=>x.i!=null).Select(x=>new KitItem("player",x.n,x.i.QualifiedItemId,x.i.Quality,x.i.Stack)).Concat(chest.GetItemsForPlayer().Select((i,n)=>(i,n)).Where(x=>x.i!=null).Select(x=>new KitItem("chest",x.n,x.i.QualifiedItemId,x.i.Quality,x.i.Stack))).ToArray();
    private static Dictionary<string,int> KitTotals(IEnumerable<KitItem> items)=>items.GroupBy(i=>i.Id+":"+i.Quality).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Count));
}
