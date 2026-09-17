using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Pathfinding;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Together;

// A semantic job owns its actor until completion. Child actions are native executors,
// not model turns. Runtime state is deliberately not replayed after loading a save.
public sealed class SemanticJob {
    public string command_id {get;set;}="work:"+Guid.NewGuid().ToString("N");
    public string actor {get;set;}="player";
    public string goal {get;set;}="";
    public string location {get;set;}="";
    public string status {get;set;}="running";
    public string phase {get;set;}="selecting";
    public string? error {get;set;}
    public string? stop_reason {get;set;}
    public int requested {get;set;}
    public int completed {get;set;}
    public int gained {get;set;}
    public int skipped {get;set;}
    public int deposited {get;set;}
    public int refills {get;set;}
    public string? child_id {get;set;}
    public List<object> evidence {get;set;}=new();
    internal string Item="",ChildKind="",Target="";
    internal int Day,Reserve,Until,BeforeCount,BeforeWater,Attempts;
    internal DateTime Started=DateTime.UtcNow,Next=DateTime.MinValue;
    internal HashSet<string> Excluded=new();
    internal Point? RefillTile,StorageTile,ExpansionTile;
    internal string StorageLocation="",StorageSupportId="",StorageSupportFor="";
    internal string CargoActor="";
    internal int StorageSupportAttempts;
    internal int RequiredSlots;
    internal int WaterAccessEnergy;
    internal bool Storing,CancelRequested;
    internal int ReliefDepth;
    internal string ReliefAction="";
    internal bool PickupPending;
    internal List<Point> PickupTiles=new();
    internal bool IncludeTrees;
    internal int StockTarget;
    internal int ActionLimit=999;
    internal string PlanId="";
    internal string CleanupId="";
    internal int MinimumQuality;
    internal int MineTarget,MineFloor=-1,MineStartLevel=-1;
    internal StardewValley.Quests.Quest? NativeQuest;
    internal StardewValley.SpecialOrders.Objectives.OrderObjective? OrderObjective;
    public string? order_id {get;set;}
    public int? objective {get;set;}
    public object? native_order_progress=>OrderObjective==null?null:new{current=OrderObjective.GetCount(),required=OrderObjective.GetMaxCount(),complete=OrderObjective.IsComplete()};
    public string? quest_id {get;set;}
    public object? native_quest_progress=>NativeQuest==null?null:new{current=NativeQuestIdentity.Count(NativeQuest).Current,required=NativeQuestIdentity.Count(NativeQuest).Required,completed=NativeQuest.completed.Value};
    internal string MineRegion="normal";
    internal int MineTravelBudget,MineKeepGold=500,VolcanoFailures;
    internal string MineReturnReason="";
    internal int FoodUsed,MaxFood=3,FishBaseline;
    internal string FishLocation="";
    internal bool ExactQuality;
}

public sealed partial class ModEntry {
    private readonly Dictionary<string,SemanticJob> semanticJobs=new();
    private readonly Dictionary<string,DateTime> workFinishedAt=new();
    private void OnNativeWorkFinished(string id) {
        workFinishedAt[id]=DateTime.UtcNow;
        foreach(var job in semanticJobs.Values.Where(j=>j.status=="running"&&j.child_id==id))job.Next=DateTime.MinValue;
        foreach(var key in workFinishedAt.Keys.Take(Math.Max(0,workFinishedAt.Count-64)).ToArray())workFinishedAt.Remove(key);
    }
    internal bool WorkActorBusy(string actor)=>semanticJobs.Values.Any(j=>(j.actor==actor||j.CargoActor==actor)&&j.status=="running");
    internal object StartSemanticWork(JsonElement args) {
        string actor=AgentToolRegistry.Text(args,"actor_id","player"),goal=AgentToolRegistry.Text(args,"goal");
        if(goal is not ("cleanup" or "storage_expand" or "fish" or "volcano_trip" or "mine_trip" or "milk" or "shear" or "animal_collect" or "pet" or "feed" or "tend" or "collect" or "process" or "withdraw" or "plant" or "resource" or "hardwood" or "stone" or "wood" or "fiber" or "water" or "refill" or "harvest" or "forage" or "clear_dead" or "store"))throw new InvalidOperationException("unsupported_work_goal");
        if(actor=="player"&&goal is "tend" or "collect" or "process")throw new InvalidOperationException("this_batch_skill_currently_requires_companion");
        if(actor!="player" && goal is "storage_expand" or "fish" or "volcano_trip" or "mine_trip" or "milk" or "shear" or "animal_collect" or "hardwood" or "withdraw" or "plant" or "refill" or "clear_dead")throw new InvalidOperationException("goal_requires_player");
        var origin=AgentMapOrigin(actor);
        if(WorkActorBusy(actor)||actor=="player"&&playerExecutor.Busy)throw new InvalidOperationException("actor_busy");
        int count=AgentToolRegistry.Number(args,"count",goal is "resource" or "hardwood" or "stone" or "wood" or "fiber"?20:0);
        int reserve=AgentToolRegistry.Number(args,"reserve_stamina",DailyBudget.EnergyReserve),until=AgentToolRegistry.Number(args,"until",2200);
        if(count<0||count>999||goal is "resource" or "hardwood" or "stone" or "wood" or "fiber"&&count==0||reserve<0||reserve>270||until<600||until>(goal is "withdraw" or "store"?2500:2300)||until%100>59)throw new InvalidOperationException("invalid_work_limits");
        string location=AgentToolRegistry.Text(args,"location",origin.Location.NameOrUniqueName);
        if(PlayerExecutor.LoadedLocation(location)==null)throw new InvalidOperationException("unknown_location");
        var job=new SemanticJob{actor=actor,goal=goal,location=location,requested=count,Day=Game1.Date.TotalDays,Reserve=reserve,Until=until,Item=goal switch{"hardwood"=>"(O)709","resource"=>AgentToolRegistry.Text(args,"item"),"stone"=>"(O)390","wood"=>"(O)388","fiber"=>"(O)771",_=>""}};
        job.RequiredSlots=OperationsPolicy.RequiredFreeSlots(goal,AgentToolRegistry.Number(args,"required_free_slots",0));
        if(job.RequiredSlots is <0 or >12)throw new InvalidOperationException("invalid_required_free_slots");
        string questId=AgentToolRegistry.Text(args,"quest_id");
        if(questId.Length>0) {
            job.NativeQuest=NativeQuestIdentity.Find(questId)??throw new InvalidOperationException("active_native_quest_required");job.quest_id=questId;
            bool matches=job.NativeQuest switch {
                StardewValley.Quests.FishingQuest q=>goal=="fish"&&q.ItemId.Value==AgentToolRegistry.Text(args,"item"),
                StardewValley.Quests.ResourceCollectionQuest q=>goal=="mine_trip"||goal is "wood" or "stone" or "fiber" or "hardwood" or "resource"&&q.ItemId.Value==job.Item,
                StardewValley.Quests.SlayMonsterQuest=>goal=="mine_trip",
                _=>false
            };
            if(actor!="player"||!matches||job.NativeQuest.completed.Value)throw new InvalidOperationException("quest_work_goal_or_actor_mismatch");
        }
        string orderId=AgentToolRegistry.Text(args,"order_id");
        if(orderId.Length>0) {
            var order=OrderRules.Find(orderId)??throw new InvalidOperationException("active_special_order_required");int index=AgentToolRegistry.Number(args,"objective",-1);
            if(actor!="player"||questId.Length>0||index<0||index>=order.objectives.Count)throw new InvalidOperationException("invalid_work_order_objective");
            job.OrderObjective=order.objectives[index];job.order_id=orderId;job.objective=index;
            string item=AgentToolRegistry.Text(args,"item",job.Item);
            bool matches=job.OrderObjective switch {
                StardewValley.SpecialOrders.Objectives.SlayObjective or StardewValley.SpecialOrders.Objectives.ReachMineFloorObjective=>goal=="mine_trip",
                StardewValley.SpecialOrders.Objectives.FishObjective o=>goal=="fish"&&item.Length>0&&OrderRules.Accepts(o,ItemRegistry.Create(item)),
                StardewValley.SpecialOrders.Objectives.CollectObjective o=>goal is "fish" or "wood" or "stone" or "resource" or "hardwood" or "fiber" or "harvest" or "forage"&&item.Length>0&&OrderRules.Accepts(o,ItemRegistry.Create(item)),
                _=>false
            };
            if(!matches||job.OrderObjective.failOnCompletion.Value)throw new InvalidOperationException("order_goal_mismatch_or_prohibited_objective");
        }
        if(goal=="fish") {
            job.Item=AgentToolRegistry.Text(args,"item");job.FishLocation=AgentToolRegistry.Text(args,"location");job.requested=AgentToolRegistry.Number(args,"count",3);
            if(job.requested is <1 or >100||job.Item.Length>0&&!DataLoader.Fish(Game1.content).ContainsKey(job.Item.StartsWith("(O)")?job.Item[3..]:job.Item))throw new InvalidOperationException("fish_trip_requires_valid_item_and_count");
            job.FishBaseline=FishingRules.Caught(job.Item);
            if(job.FishLocation.Length==0)job.FishLocation=FishingLocations(job.Item).FirstOrDefault()?.NameOrUniqueName??location;
        }
        if(goal=="volcano_trip") {
            job.MineTarget=AgentToolRegistry.Number(args,"target_level",10);job.MineTravelBudget=AgentToolRegistry.Number(args,"travel_budget",0);job.MineKeepGold=AgentToolRegistry.Number(args,"keep_gold",0);
            if(job.MineTarget is <1 or >10||job.MineTravelBudget<0||job.MineKeepGold<0)throw new InvalidOperationException("invalid_volcano_target_or_budget");job.requested=0;
        }
        if(goal=="mine_trip") {
            job.MineStartLevel=AgentToolRegistry.Number(args,"start_level",-1);
            job.MineRegion=AgentToolRegistry.Text(args,"region","normal");job.MineTarget=AgentToolRegistry.Number(args,"target_level",job.MineRegion=="skull"?25:Math.Min(120,(StardewValley.Locations.MineShaft.lowestLevelReached/5+1)*5));
            job.MineTravelBudget=AgentToolRegistry.Number(args,"travel_budget",0);job.MineKeepGold=AgentToolRegistry.Number(args,"keep_gold",0);
            if(job.MineStartLevel < -1||job.MineStartLevel>=job.MineTarget||job.MineStartLevel>StardewValley.Locations.MineShaft.lowestLevelReached||job.MineStartLevel>=0&&job.MineStartLevel%5!=0||job.MineRegion is not ("normal" or "skull")||job.MineTarget<1||job.MineTarget>(job.MineRegion=="skull"?1000:120)||job.MineTravelBudget<0||job.MineKeepGold<0)throw new InvalidOperationException("invalid_mine_target_or_budget");job.requested=0;
        }
        if(goal=="resource"&&!ResourceRules.Nodes.Values.Contains(job.Item))throw new InvalidOperationException("resource_item_has_no_known_native_node_route");
        job.IncludeTrees=!args.TryGetProperty("include_trees",out var trees)||trees.ValueKind==JsonValueKind.True;
        if(goal is "forage" or "harvest") {
            if(goal=="harvest"&&AgentToolRegistry.Text(args,"item") is {Length:>0} harvested&&ItemRegistry.Create(harvested) is StardewValley.Object {Category:-74})throw new InvalidOperationException("harvest_cannot_plant_use_farm_plan_then_plant");
            job.Item=AgentToolRegistry.Text(args,"item");job.MinimumQuality=AgentToolRegistry.Number(args,"quality",0);
            if(job.MinimumQuality is not (0 or 1 or 2 or 4)||job.Item.Length>0&&ItemRegistry.GetDataOrErrorItem(job.Item).IsErrorItem)throw new InvalidOperationException("invalid_target_gather_item_or_quality");
        }
        if(goal=="withdraw") {
            job.ExactQuality=args.TryGetProperty("exact_quality",out var exact)&&exact.ValueKind==JsonValueKind.True;
            if(goal=="harvest"&&AgentToolRegistry.Text(args,"item") is {Length:>0} harvested&&ItemRegistry.Create(harvested) is StardewValley.Object {Category:-74})throw new InvalidOperationException("harvest_cannot_plant_use_farm_plan_then_plant");
            job.Item=AgentToolRegistry.Text(args,"item");job.MinimumQuality=AgentToolRegistry.Number(args,"quality",0);
            if(job.MinimumQuality is not (0 or 1 or 2 or 4))throw new InvalidOperationException("invalid_minimum_quality");
            if(job.Item.Length==0||ItemRegistry.GetDataOrErrorItem(job.Item).IsErrorItem||count<1)throw new InvalidOperationException("withdraw_item_and_positive_count_required");
        }
        if(goal=="cleanup") {
            job.CleanupId=AgentToolRegistry.Text(args,"cleanup_id");
            var order=Data.Maintenance.Orders.FirstOrDefault(o=>o.Id==job.CleanupId&&o.Status=="active")??throw new InvalidOperationException("active_cleanup_order_required");
            if(semanticJobs.Values.Any(j=>j.status=="running"&&j.CleanupId==job.CleanupId))throw new InvalidOperationException("cleanup_order_owned_by_another_actor");
            job.location="Farm";job.Reserve=Math.Max(reserve,order.ReserveStamina);job.Until=Math.Min(until,order.Until);job.requested=Math.Min(count>0?count:FarmCleanupRules.RemainingBudget(order),FarmCleanupRules.RemainingBudget(order));
        }
        if(goal=="plant") {
            job.PlanId=AgentToolRegistry.Text(args,"plan_id");
            if(!farmPlantPlans.TryGetValue(job.PlanId,out var plan)||plan.Epoch!=agentSaveEpoch||plan.Day!=Game1.Date.TotalDays)throw new InvalidOperationException("read_farm_plan_first");
            job.location=plan.Location;job.requested=0;
            if(PlayerExecutor.LoadedLocation(plan.Location) is {} field&&!field.IsGreenhouse)District(field).Commit(plan.PreparationTiles.Count>0?plan.PreparationTiles:plan.Tiles);
        }
        job.MaxFood=Math.Clamp(AgentToolRegistry.Number(args,"max_food",3),0,10);
        semanticJobs.Add(job.command_id,job);
        foreach(var old in semanticJobs.Values.Where(j=>j.status!="running").Take(Math.Max(0,semanticJobs.Count-96)).ToArray())semanticJobs.Remove(old.command_id);
        return job;
    }
    private void StopSemanticWork(SemanticJob j,string reason,bool met=false) {
        bool yield=RecoveryPolicy.CanWait(reason)&&reason is not("no_matching_targets" or "remaining_targets_unreachable")&&!reason.StartsWith("capacity_");
        j.status=met?"succeeded":yield?"partial":"failed";j.stop_reason=reason;j.error=met||yield?null:reason;j.phase="finished";
        if(yield&&j.goal=="cleanup"&&Data.Maintenance.Orders.FirstOrDefault(o=>o.Id==j.CleanupId) is {} order){order.Status="paused";order.Reason=reason;}

    }
    private object SemanticReceipt(string id,bool cancel) {
        if(!semanticJobs.TryGetValue(id,out var job))throw new InvalidOperationException("work_receipt_unavailable_replan");
        if(cancel&&job.status=="running") {
            job.CancelRequested=true;
            if(job.child_id!=null) {
                PreserveCleanupPickup(job);
                var receipt=JsonSerializer.SerializeToElement(AgentReceipt(job.child_id,true),AgentJson.Options);
                if(receipt.GetProperty("status").GetString()=="running"){job.phase="cancelling_native_action";return job;}
                // Cancellation may arrive between a native hit and the next semantic
                // poll. Preserve verified partial work instead of replenishing quota.
                if(job.ChildKind=="cleanup_labor"&&receipt.TryGetProperty("completed",out var completed)) {
                    job.completed+=completed.GetInt32();RecordCleanupProgress(job,completed.GetInt32());
                }
                job.child_id=null;
            }
            job.status="cancelled";job.stop_reason="cancelled_by_controller";job.phase="finished";
        }
        return job;
    }
    private void PreserveCleanupPickup(SemanticJob job) {
        if(job.child_id!=playerExecutor.Current?.command_id||job.ChildKind is not ("cleanup_labor" or "pickup_recovery"))return;
        var order=Data.Maintenance.Orders.FirstOrDefault(o=>o.Id==job.CleanupId);if(order==null)return;
        order.PendingPickup=order.PendingPickup.Concat(playerExecutor.PendingDropTiles().Select(p=>new FarmCell(p.X,p.Y))).Distinct().Take(128).ToList();
        job.PickupTiles=order.PendingPickup.Select(p=>new Point(p.X,p.Y)).ToList();
    }
    private void ResetSemanticWork() {
        foreach(var j in semanticJobs.Values.Where(j=>j.status=="running").ToArray())try{SemanticReceipt(j.command_id,true);}catch{j.status="cancelled";}
    }
    private JsonElement WorkActor(string id)=>World().GetProperty("actors").EnumerateArray().First(a=>a.GetProperty("id").GetString()==id);
    private static int CompanionCargoSlots(JsonElement actor)=>actor.TryGetProperty("cargo_slots",out var slots)?slots.GetInt32():actor.GetProperty("cargo").EnumerateObject().Count();
    private int WorkCount(SemanticJob j)=>j.goal=="cleanup"&&j.actor!="player"?WorkActor(j.actor).GetProperty("cargo").EnumerateObject().Sum(c=>c.Value.GetInt32()):j.OrderObjective!=null?j.OrderObjective.GetCount():j.NativeQuest!=null?NativeQuestIdentity.Count(j.NativeQuest).Current:j.Item.Length==0?0:j.actor=="player"?Game1.player.Items.Where(i=>i?.QualifiedItemId==j.Item&&i.Quality>=j.MinimumQuality).Sum(i=>i.Stack):WorkActor(j.actor).GetProperty("cargo").EnumerateObject().Where(p=>p.Name.StartsWith(j.Item+":")&&int.TryParse(p.Name[(p.Name.LastIndexOf(':')+1)..],out int quality)&&quality>=j.MinimumQuality).Sum(p=>p.Value.GetInt32());
    private void WorkChild(SemanticJob j,string tool,object args,string kind,string target="") {
        if(j.actor=="player"&&playerExecutor.Current is {} ended&&workFinishedAt.Remove(ended.command_id,out var finished))Data.Autoplay.Record("work_handoff",AgentJson.Encode(new{j.command_id,from=ended.command_id,to=tool,delay_ms=(DateTime.UtcNow-finished).TotalMilliseconds}));
        j.ChildKind=kind;j.Target=target;j.BeforeCount=WorkCount(j);j.Attempts++;
        var json=JsonSerializer.SerializeToElement(args);
        if(tool=="player.work") {var enriched=System.Text.Json.Nodes.JsonNode.Parse(json.GetRawText())!.AsObject();enriched["reserve_stamina"]=j.Reserve;json=JsonSerializer.SerializeToElement(enriched);}
        if(tool=="player.work"&&json.TryGetProperty("tiles",out var tiles))j.PickupTiles=tiles.EnumerateArray().Select(t=>new Point(t.GetProperty("x").GetInt32(),t.GetProperty("y").GetInt32())).ToList();
        var result=JsonSerializer.SerializeToElement(tool=="companion.assign"?AgentCompanion(json):playerExecutor.Start(tool,json),AgentJson.Options);
        if(!result.TryGetProperty("command_id",out var id))throw new InvalidOperationException(result.TryGetProperty("error",out var e)?e.GetString():"work_child_not_started");
        j.child_id=id.GetString();j.phase=kind;
    }
    private void TickSemanticWork() {
        if(!Context.IsWorldReady)return;
        foreach(var j in semanticJobs.Values.Where(j=>j.status=="running").ToArray()) {
            bool nativeCompleted=j.actor=="player"&&j.child_id!=null&&playerExecutor.Current?.command_id==j.child_id&&playerExecutor.Current.status!="running";
            if(DateTime.UtcNow<j.Next&&!nativeCompleted)continue;j.Next=DateTime.UtcNow.AddMilliseconds(250);
            try{TickSemanticJob(j);}catch(Exception e){if(j.child_id!=null)try{AgentReceipt(j.child_id,true);}catch{}StopSemanticWork(j,e is InvalidOperationException?e.Message:"work_"+e.GetType().Name);}
        }
    }
    private void TickSemanticJob(SemanticJob j) {
        if(j.CancelRequested){SemanticReceipt(j.command_id,true);return;}
        // Deadlines own the child too. Polling a running child must not bypass
        // until/day bounds for a whole day. Cancellation preserves native effects.
        var deadline=FishingContinuity.Deadline(j.Day,Game1.Date.TotalDays,j.Until,Game1.timeOfDay);
        if(deadline!=null) {
            if(j.child_id!=null) {
                PreserveCleanupPickup(j);
                var stopped=JsonSerializer.SerializeToElement(AgentReceipt(j.child_id,true),AgentJson.Options);
                if(stopped.GetProperty("status").GetString()=="running"){j.phase="deadline_waiting_native_impact";return;}
                j.evidence.Add(new{kind="deadline_cancel",command_id=j.child_id,receipt=stopped});
                agentClaims.Remove(j.child_id);j.child_id=null;
                if(j.ChildKind is "labor" or "cleanup_labor"&&stopped.TryGetProperty("completed",out var partial)) {
                    j.completed+=partial.GetInt32();if(j.ChildKind=="cleanup_labor")RecordCleanupProgress(j,partial.GetInt32());
                }
                if(j.ChildKind=="labor")j.gained+=Math.Max(0,WorkCount(j)-j.BeforeCount);
                if(j.goal=="fish")j.gained=Math.Max(0,FishingRules.Caught(j.Item)-j.FishBaseline);
            }
            StopSemanticWork(j,deadline);return;
        }
        if(j.child_id!=null) {
            var r=JsonSerializer.SerializeToElement(AgentReceipt(j.child_id,false),AgentJson.Options);
            if(r.GetProperty("status").GetString()=="running")return;
            bool ok=r.GetProperty("status").GetString()=="succeeded";
            if(!ok)PreserveCleanupPickup(j);
            j.evidence.Add(new{command_id=j.child_id,kind=j.ChildKind,target=j.Target,status=r.GetProperty("status").GetString(),error=r.TryGetProperty("error",out var failure)?failure.GetString():null,gained=Math.Max(0,WorkCount(j)-j.BeforeCount)});
            if(j.evidence.Count>128)j.evidence.RemoveAt(0);agentClaims.Remove(j.child_id);j.child_id=null;
            if(j.ChildKind is "labor" or "pickup_recovery")j.gained+=Math.Max(0,WorkCount(j)-j.BeforeCount);
            if(j.ChildKind is "labor" or "cleanup_labor") {
                int completed=r.TryGetProperty("completed",out var amount)?amount.GetInt32():ok?1:0;
                j.completed+=completed;if(j.CleanupId.Length>0)RecordCleanupProgress(j,completed);
            }
            if(ok&&j.CleanupId.Length>0&&j.ChildKind is "cleanup_labor" or "pickup_recovery")Data.Maintenance.Orders.FirstOrDefault(o=>o.Id==j.CleanupId)?.PendingPickup.Clear();
            if(j.ChildKind=="capacity_relief") {
                if(!ok){StopSemanticWork(j,"relief_prerequisite_failed");return;}
                j.ReliefDepth=0;j.ReliefAction="";j.Storing=true;
            }
            if(!ok) {
                string error=r.TryGetProperty("error",out var e)?e.GetString()??"child_failed":"child_failed";
                if(j.goal=="fish"&&j.ChildKind is "fish_attempt" or "fish_travel") {
                    if(error is "fishing_resource_or_time_reserve_reached" or "capacity_no_free_slot"){}
                    else if(error is "no_reachable_native_cast_site" or "no_known_route" or "no_path" or "path_stalled" or "target_fish_conditions_or_native_landing_changed" or "target_fish_window_closed")j.Excluded.Add("fish:"+j.location);
                    else {StopSemanticWork(j,error);return;}
                }
                else if(j.goal=="volcano_trip"&&j.ChildKind!="volcano_retreat") {
                    if(j.ChildKind=="volcano_combat"&&error=="current_area_clear_before_requested_kills"){}
                    else if(++j.VolcanoFailures>=2||error is "volcano_watering_can_refill_required" or "volcano_cooling_needs_stamina_reserve")j.MineReturnReason="volcano_interrupted:"+error;
                }
                else if(j.goal=="mine_trip"&&j.ChildKind!="mine_exit") {if(j.ChildKind=="mine_stone"&&error is "no_path" or "path_stalled" or "resource_no_longer_present")j.Excluded.Add(j.Target);else if(j.ChildKind=="mine_combat"&&error=="current_area_clear_before_requested_kills"){}else j.MineReturnReason="mine_interrupted:"+error;}
                else if(j.ChildKind=="care_batch"&&error is "eligible_animals_exhausted" or "eligible_animal_products_exhausted" or "all_resident_animals_already_have_feed"){StopSemanticWork(j,"native_daily_care_complete",j.requested==0);return;}
                else if(error.StartsWith("capacity_")&&j.ReliefDepth>0){StopSemanticWork(j,"relief_prerequisite_failed:"+error);return;}
                else if(error.StartsWith("capacity_")&&j.actor=="player"&&j.PickupTiles.Count>0) {j.PickupPending=true;j.Storing=true;j.phase="storage_for_pickup";}
                else if(j.ChildKind is "labor" or "cleanup_labor"&&error=="protected_scythe_sweep_no_safe_stand") {
                    if(r.TryGetProperty("effects",out var effects))foreach(var effect in effects.EnumerateArray())
                        if(effect.TryGetProperty("kind",out var kind)&&kind.GetString()=="work_target_blocked")j.Excluded.Add($"{effect.GetProperty("x").GetInt32()},{effect.GetProperty("y").GetInt32()}");
                    j.skipped++;j.phase="selecting";
                }
                else if(j.ChildKind is "labor" or "cleanup_labor" && error is "no_path" or "exit_unreachable" or "path_stalled" or "work_effect_not_observed" or "resource_no_longer_present" or "target_not_available") {j.Excluded.Add(j.Target);j.skipped++;j.phase="selecting";}
                else {StopSemanticWork(j,error);return;}
            } else if(j.ChildKind is "labor" or "cleanup_labor") {j.phase="selecting";}
            else if(j.ChildKind=="pickup_recovery"){j.PickupPending=false;j.phase="selecting";}
            else if(j.ChildKind=="care_batch"){j.completed+=r.TryGetProperty("completed",out var done)?done.GetInt32():1;j.phase="selecting";}
            else if(j.ChildKind=="recovery_eat") {j.FoodUsed++;j.phase="selecting";}
            else if(j.ChildKind=="storage_deposit") {
                j.Storing=true;
                if(r.TryGetProperty("evidence",out var detail)&&detail.TryGetProperty("resource_changes",out var changes))j.deposited+=changes.EnumerateObject().Sum(p=>Math.Max(0,p.Value.GetInt32()));
            }
            else if(j.ChildKind=="refill_use") {
                var can=Game1.player.Items.OfType<WateringCan>().FirstOrDefault();
                if(can==null||can.WaterLeft<=j.BeforeWater){StopSemanticWork(j,"refill_effect_not_observed");return;}
                j.refills++;j.RefillTile=null;j.phase="selecting";
                if(j.goal=="refill"){StopSemanticWork(j,"water_replenished",true);return;}
            }
        }
        if(Game1.Date.TotalDays!=j.Day){StopSemanticWork(j,"day_changed_replan");return;}
        if(Game1.eventUp||Game1.activeClickableMenu!=null){if(j.actor=="player")StopSemanticWork(j,"interaction_requires_model");return;}
        if(Game1.fadeToBlack||Game1.locationRequest!=null||j.actor=="player"&&(!Game1.player.CanMove||Game1.player.UsingTool))return;
        if(j.PickupPending) {
            if(j.Storing){TickWorkStorage(j);return;}
            if(Game1.currentLocation.NameOrUniqueName!=j.location){WorkChild(j,"player.travel",new{location=j.location},"pickup_return");return;}
            WorkChild(j,"player.collect_drops",new{tiles=j.PickupTiles.Select(p=>new{x=p.X,y=p.Y})},"pickup_recovery");return;
        }
        if(j.OrderObjective!=null&&j.goal!="mine_trip"&&j.OrderObjective.GetCount()>=j.OrderObjective.GetMaxCount()){StopSemanticWork(j,"native_order_objective_reached",true);return;}
        if(j.NativeQuest!=null&&j.goal!="mine_trip"&&(j.NativeQuest.completed.Value||NativeQuestIdentity.Count(j.NativeQuest).Current>=NativeQuestIdentity.Count(j.NativeQuest).Required)){StopSemanticWork(j,"native_quest_objective_reached",true);return;}
        if(j.StockTarget>0&&TeamStock(j.Item)>=j.StockTarget){StopSemanticWork(j,"shared_stock_target_reached",true);return;}
        if(j.requested>0 && (j.Item.Length>0?j.gained:j.completed)>=j.requested){StopSemanticWork(j,"requested_amount_reached",true);return;}
        if(j.completed>=j.ActionLimit){StopSemanticWork(j,"planned_labor_allowance_used");return;}
        if(j.goal=="fish"){TickFishingTrip(j);return;}
        if(j.goal=="mine_trip"){TickMineTrip(j);return;}
        if(j.goal=="volcano_trip"){TickVolcanoTrip(j);return;}
        if(Game1.timeOfDay>=j.Until){StopSemanticWork(j,"time_reserve_reached");return;}
        if((DateTime.UtcNow-j.Started).TotalSeconds>600||j.Attempts>=128){StopSemanticWork(j,"work_budget_reached");return;}
        if(Game1.player.health<30){if(j.actor=="player"&&TryWorkFood(j))return;StopSemanticWork(j,"player_in_danger");return;}
        if(j.goal=="storage_expand"){TickStorageSupport(j);return;}
        if(j.Storing||j.goal=="store"){TickWorkStorage(j);return;}
        if(j.goal=="withdraw"){TickWorkWithdraw(j);return;}
        if(j.actor=="player"&&PlayerNeedsWorkStorage(j)) {j.Storing=true;TickWorkStorage(j);return;}
        if(j.actor=="player"&&j.goal is "pet" or "feed" or "milk" or "shear" or "animal_collect") {
            if(j.goal is "milk" or "shear"&&Game1.player.Stamina<j.Reserve+4){if(TryWorkFood(j))return;StopSemanticWork(j,"animal_care_energy_reserve");return;}
            WorkChild(j,"player.care",new{mode=j.goal=="animal_collect"?"collect":j.goal,count=1},"care_batch");return;
        }
        var origin=AgentMapOrigin(j.actor);
        if(origin.Location.NameOrUniqueName!=j.location) {
            WorkChild(j,j.actor=="player"?"player.travel":"companion.assign",j.actor=="player"?(object)new{location=j.location}:new{actor_id=j.actor,skill="travel",destination=j.location},"travel");return;
        }
        if(j.actor=="player"&&j.goal is "cleanup" or "resource" or "hardwood" or "stone" or "wood" or "fiber" or "harvest" or "forage"
            &&!(j.goal=="cleanup"&&Data.Maintenance.Orders.Any(o=>o.Id==j.CleanupId&&o.PendingPickup.Count>0))) {
            var loose=PlayerExecutor.LooseDrops(origin.Location).Where(d=>Vector2.DistanceSquared(d.Pixel,Game1.player.StandingPixel.ToVector2())<=256*256).Select(d=>(d.Pixel/64).ToPoint()).Distinct().Take(6).ToList();
            if(loose.Count>0) {j.PickupTiles=loose;WorkChild(j,"player.collect_drops",new{tiles=loose.Select(p=>new{x=p.X,y=p.Y})},"pickup_recovery");return;}
        }
        if(j.goal=="cleanup"){if(j.actor=="player")TickCleanupWork(j);else TickCompanionCleanup(j);}
        else if(j.goal=="plant")TickPlantWork(j);
        else if(j.actor=="player")SelectPlayerWork(j);else SelectCompanionWork(j,origin.Location);
    }
    private int WorkSlot(Func<Item,bool> predicate)=>Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is {} item&&predicate(item),-1);
    private void SelectPlayerWork(SemanticJob j) {
        var l=Game1.currentLocation;var p=Game1.player;
        if(j.goal is "water" or "refill") {
            int slot=WorkSlot(i=>i is WateringCan);if(slot<0){StopSemanticWork(j,"watering_can_missing");return;}
            var can=(WateringCan)p.Items[slot];
            if(j.goal=="refill"||can.WaterLeft==0||j.RefillTile!=null){RefillWork(j,slot,can);return;}
        }
        var candidates=new List<(Point Tile,string Skill,int Slot,float Energy,string Item)>();
        int tool=WorkSlot(i=>j.goal switch{"resource" or "stone"=>i is Pickaxe,"hardwood" or "wood"=>i is Axe,"fiber" or "clear_dead"=>i is Tool t&&t.isScythe(),"water"=>i is WateringCan,_=>false});
        if(j.goal is "resource" or "hardwood" or "stone" or "wood" or "fiber" or "clear_dead" or "water" && tool<0){StopSemanticWork(j,"required_tool_missing");return;}
        foreach(var pair in l.objects.Pairs) {
            var o=pair.Value;
            bool match=j.goal switch{"resource"=>ResourceRules.Nodes.GetValueOrDefault(o.ItemId)==j.Item,"stone"=>o.BaseName=="Stone","wood"=>o.IsTwig(),"fiber"=>o.IsWeeds(),"forage"=>o.isForage()&&!o.bigCraftable.Value&&(j.Item.Length==0||o.QualifiedItemId==j.Item),_=>false};
            if(match)candidates.Add((pair.Key.ToPoint(),j.goal=="forage"?"forage":"clear",tool,j.goal is "fiber" or "forage"?0:PlayerExecutor.ResourceEnergy(o,tool>=0?p.Items[tool] as Tool:null),j.Item.Length>0?j.Item:o.QualifiedItemId));
        }
        foreach(var pair in l.terrainFeatures.Pairs)if(pair.Value is HoeDirt d&&d.crop!=null) {
            bool match=j.goal switch{"water"=>!d.crop.dead.Value&&d.state.Value!=1&&!d.readyForHarvest(),"forage"=>d.crop.forageCrop.Value&&Together.Shared.ForageCropRules.CanHarvest(true,d.crop.whichForageCrop.Value)&&!d.crop.dead.Value&&d.readyForHarvest()&&(j.Item.Length==0||Together.Shared.ForageCropRules.HarvestId(d.crop.forageCrop.Value,d.crop.whichForageCrop.Value,ItemRegistry.QualifyItemId(d.crop.indexOfHarvest.Value)??"")==j.Item),"harvest"=>!d.crop.dead.Value&&d.readyForHarvest()&&(j.Item.Length==0||Together.Shared.ForageCropRules.HarvestId(d.crop.forageCrop.Value,d.crop.whichForageCrop.Value,ItemRegistry.QualifyItemId(d.crop.indexOfHarvest.Value)??"")==j.Item),"clear_dead"=>d.crop.dead.Value,_=>false};
            if(match)candidates.Add((pair.Key.ToPoint(),j.goal=="forage"?"harvest":j.goal,tool,j.goal=="water"?PlayerExecutor.SwingEnergy(tool>=0?p.Items[tool] as Tool:null):0,j.goal=="harvest"?"(O)"+d.crop.indexOfHarvest.Value:""));
        }
        if(j.goal=="wood"&&j.IncludeTrees)foreach(var pair in l.terrainFeatures.Pairs)
            if(pair.Value is Tree t&&t.growthStage.Value>=5&&!t.tapped.Value&&t.GetData()?.DropWoodOnChop==true)
                candidates.Add((pair.Key.ToPoint(),"chop",tool,PlayerExecutor.TreeEnergy(t,Game1.player.Items[tool] as Tool),j.Item));
        if(j.goal is "resource" or "hardwood" or "stone")foreach(var clump in l.resourceClumps) {
            var rule=ResourceRules.Clump(clump.parentSheetIndex.Value);if(rule==null||rule.Value.Output!=j.Item)continue;
            int clumpSlot=WorkSlot(i=>i is Tool t&&t.UpgradeLevel>=rule.Value.Level&&(rule.Value.Tool=="axe"?i is Axe:i is Pickaxe));
            if(clumpSlot<0)continue;
            float energy=(float)Math.Ceiling(clump.health.Value/Math.Max(1,(Game1.player.Items[clumpSlot] as Tool)!.UpgradeLevel*.75+.75))*2+4;
            foreach(var at in new[]{clump.Tile.ToPoint(),new Point((int)clump.Tile.X+clump.width.Value-1,(int)clump.Tile.Y),new Point((int)clump.Tile.X,(int)clump.Tile.Y+clump.height.Value-1),new Point((int)clump.Tile.X+clump.width.Value-1,(int)clump.Tile.Y+clump.height.Value-1)}.Distinct())
                candidates.Add((at,"break_clump",clumpSlot,energy,j.Item));
        }
        int energyReserve=j.Reserve;
        string? constraint=null;
        var eligible=candidates.Where(c=>!j.Excluded.Contains($"{c.Tile.X},{c.Tile.Y}")&&!AgentTileBusy(l.NameOrUniqueName,c.Tile.X,c.Tile.Y)&&!(j.goal is "resource" or "stone" or "wood" or "fiber" or "hardwood" or "clear_dead"&&MaintenanceProtects(l,c.Tile))).ToArray();
        var route=CleanupRouting.Plan(new(p.TilePoint.X,p.TilePoint.Y),eligible.Select(c=>new CleanupSite(new(c.Tile.X,c.Tile.Y),(int)Math.Ceiling(c.Energy))),t=>PlayerExecutor.Passable(l,new(t.X,t.Y)),Math.Max(0,(int)p.Stamina-energyReserve),1);
        if(j.goal=="wood"&&eligible.Length>0) {
            var distances=AutonomyPolicy.Distances(new(p.TilePoint.X,p.TilePoint.Y),t=>PlayerExecutor.Passable(l,new(t.X,t.Y)));
            var ranked=eligible.Where(c=>c.Energy<=p.Stamina-energyReserve).Select(c=>{
                var tile=new FarmCell(c.Tile.X,c.Tile.Y);
                var stand=new[]{new FarmCell(tile.X+1,tile.Y),new(tile.X-1,tile.Y),new(tile.X,tile.Y+1),new(tile.X,tile.Y-1)}.OrderBy(t=>distances.GetValueOrDefault(t,int.MaxValue)).First();
                int walk=distances.GetValueOrDefault(stand,int.MaxValue),yield=c.Skill=="chop"?(l.terrainFeatures[c.Tile.ToVector2()] is Tree {stump.Value:true}?5:17):1;
                return new{c,stand,walk,yield,score=AutonomyPolicy.ResourceCost(walk,c.Energy,(int)Math.Ceiling(c.Energy/Math.Max(.1f,PlayerExecutor.SwingEnergy(p.Items[c.Slot] as Tool))),yield,Math.Max(1,j.requested-j.gained))};
            }).Where(x=>x.walk<int.MaxValue).OrderBy(x=>x.score).ToArray();
            if(ranked.FirstOrDefault() is {} best){route=new(){new(new(new(best.c.Tile.X,best.c.Tile.Y),(int)Math.Ceiling(best.c.Energy)),best.stand,best.walk)};
                Data.Autoplay.Record("resource_source_choice",AgentJson.Encode(new{j.command_id,needed=j.requested-j.gained,candidates=ranked.Take(5).Select(x=>new{tile=new[]{x.c.Tile.X,x.c.Tile.Y},x.c.Skill,x.c.Energy,x.walk,x.yield,x.score}),note="base wood yield estimate only; completion uses actual pickup"}));}
        }
        if(route.Count==0&&eligible.Any(c=>c.Energy>p.Stamina-energyReserve))constraint="energy_reserve_reached";
        foreach(var step in route)foreach(var c in eligible.Where(c=>c.Tile.X==step.Site.Tile.X&&c.Tile.Y==step.Site.Tile.Y).Take(1)) {
            string key=$"{c.Tile.X},{c.Tile.Y}";if(j.Excluded.Contains(key)||j.goal is "resource" or "stone" or "wood" or "fiber" or "hardwood" or "clear_dead"&&MaintenanceProtects(l,c.Tile))continue;
            if(AgentTileBusy(l.NameOrUniqueName,c.Tile.X,c.Tile.Y)){constraint="targets_claimed_by_other_actor";continue;}
            if(c.Energy>0&&p.Stamina-c.Energy<energyReserve){constraint="energy_reserve_reached";continue;}
            if(c.Item.Length>0&&!CapacityAdapter.CanReceive(p,ItemRegistry.Create(c.Item))){constraint="capacity_no_stackable_room";continue;}
            if(WorkStand(l,c.Tile)==null){j.Excluded.Add(key);j.skipped++;continue;}
            var batch=new List<Point>{c.Tile};float cost=c.Energy;
            // Nearby compatible actions share one native work command. Walking
            // between them picks up drops naturally; one final debris sweep follows.
            if(c.Skill=="clear")foreach(var next in candidates.Where(n=>n.Tile!=c.Tile&&n.Skill==c.Skill&&n.Slot==c.Slot).OrderBy(n=>Vector2.DistanceSquared(n.Tile.ToVector2(),c.Tile.ToVector2()))) {
                if(batch.Count>=Math.Min(3,Math.Max(1,j.requested-j.gained)))break;
                if(Vector2.DistanceSquared(next.Tile.ToVector2(),batch.Last().ToVector2())>2||p.Stamina-cost-next.Energy<energyReserve||j.Excluded.Contains($"{next.Tile.X},{next.Tile.Y}")||MaintenanceProtects(l,next.Tile)||AgentTileBusy(l.NameOrUniqueName,next.Tile.X,next.Tile.Y)||WorkStand(l,next.Tile)==null)continue;
                batch.Add(next.Tile);cost+=next.Energy;
            }
            WorkChild(j,"player.work",new{skill=c.Skill,slot=c.Slot,tiles=batch.Select(p=>new{x=p.X,y=p.Y})},"labor",key);return;
        }
        if(constraint=="energy_reserve_reached"&&TryWorkFood(j))return;
        bool all=candidates.Count==0&&j.requested==0;
        StopSemanticWork(j,all?"all_current_targets_completed":constraint??(j.skipped>0?"remaining_targets_unreachable":"no_matching_targets"),all);
    }
    private bool TryWorkFood(SemanticJob j) {
        if(j.FoodUsed>=j.MaxFood || Game1.player.isEating)return false;
        // Preserve all declared project reservations and every currently missing
        // bundle/quest item; a generic food policy must not eat unique progress.
        var choices=Game1.player.Items.Select((item,slot)=>new{item=item as StardewValley.Object,slot})
            .Where(x=>x.item is {Edibility:>0} food&&!food.questItem.Value&&!food.bigCraftable.Value&&food.QualifiedItemId!="(O)434"&&NativeFoodRules.Block(food)==null&&CanConsumeOne(food))
            .OrderBy(x=>x.item!.Price/(double)Math.Max(1,x.item.Edibility)).ThenBy(x=>x.slot).ToArray();
        if(choices.Length==0)return false;
        WorkChild(j,"player.eat",new{slot=choices[0].slot},"recovery_eat");return true;
    }
    private static Point? WorkStand(GameLocation l,Point tile) {
        foreach(var at in new[]{new Point(tile.X,tile.Y+1),new Point(tile.X-1,tile.Y),new Point(tile.X+1,tile.Y),new Point(tile.X,tile.Y-1)}.OrderBy(p=>Vector2.DistanceSquared(p.ToVector2(),Game1.player.Tile))) {
            if(!PlayerExecutor.Passable(l,at))continue;
            if(at==Game1.player.TilePoint)return at;
            var path=PlayerExecutor.PreviewPath(l,at);if(path?.Count>0)return at;
        }
        return null;
    }
    private void RefillWork(SemanticJob j,int slot,WateringCan can) {
        if(can.WaterLeft>=can.waterCanMax){if(j.goal=="refill")StopSemanticWork(j,"already_full",true);else j.RefillTile=null;return;}
        if(j.RefillTile is {} water) {
            j.BeforeWater=can.WaterLeft;WorkChild(j,"player.use_tool",new{slot,x=water.X,y=water.Y},"refill_use");return;
        }
        var l=Game1.currentLocation;var sources=new List<Point>();
        for(int y=0;y<l.Map.Layers[0].LayerHeight;y++)for(int x=0;x<l.Map.Layers[0].LayerWidth;x++)if(l.CanRefillWateringCanOnTile(x,y))sources.Add(new(x,y));
        foreach(var source in sources.OrderBy(p=>Vector2.DistanceSquared(p.ToVector2(),Game1.player.Tile))) {
            var at=WorkStand(l,source);if(at==null)continue;j.RefillTile=source;
            WorkChild(j,"player.move",new{x=at.Value.X,y=at.Value.Y},"refill_move");return;
        }
        if(ClearWaterAccess(j,l,sources))return;
        StopSemanticWork(j,"no_reachable_water_source");
    }
    private void SelectCompanionWork(SemanticJob j,GameLocation l) {
        var actor=WorkActor(j.actor);string skill=j.goal is "stone" or "resource"?"mine":j.goal=="process"?"refill":j.goal is "wood" or "fiber"?"clear":j.goal;
        int laborCost=Together.Shared.CompanionLabor.Cost(skill);
        if(laborCost>0&&actor.TryGetProperty("labor",out var labor)&&labor.GetProperty("remaining").GetInt32()<laborCost){StopSemanticWork(j,"partner_labor_insufficient_before_dispatch");return;}
        // NPCs have no native Farmer stamina bar. Do not invent one; bound by time,
        // actual available skills, cargo capacity and the adapter's safety checks.
        int requiredSlots=skill switch{"mine"=>4,"harvest"=>3,"clear"=>3,"forage" or "collect" or "tend"=>1,_=>0};
        if(requiredSlots>0&&actor.GetProperty("cargo_capacity").GetInt32()-CompanionCargoSlots(actor)<requiredSlots){
            j.evidence.Add(new{kind="storage_required",reason="next_skill_output_capacity",skill,required_free_slots=requiredSlots,free_slots=actor.GetProperty("cargo_capacity").GetInt32()-CompanionCargoSlots(actor)});
            j.Storing=true;TickWorkStorage(j);return;
        }
        bool claimed=false;
        foreach(var c in actor.GetProperty("candidates").EnumerateArray().Where(c=>c.GetProperty("skill").GetString()==skill||j.goal=="forage"&&c.GetProperty("skill").GetString()=="harvest")) {
            var tile=c.GetProperty("tile");int x=tile[0].GetInt32(),y=tile[1].GetInt32();string key=$"{x},{y}";
            if(j.Excluded.Contains(key)||j.goal is "resource" or "stone" or "wood" or "fiber"&&MaintenanceProtects(l,new(x,y)))continue;
            l.objects.TryGetValue(new Vector2(x,y),out var o);
            string candidateSkill=c.GetProperty("skill").GetString()!;
            if(j.goal=="forage") {
                if(candidateSkill=="harvest") {
                    if(l.terrainFeatures.GetValueOrDefault(new Vector2(x,y)) is not HoeDirt {crop:not null} wild||!wild.crop.forageCrop.Value||!Together.Shared.ForageCropRules.CanHarvest(true,wild.crop.whichForageCrop.Value)||j.Item.Length>0&&Together.Shared.ForageCropRules.HarvestId(true,wild.crop.whichForageCrop.Value,ItemRegistry.QualifyItemId(wild.crop.indexOfHarvest.Value)??"")!=j.Item)continue;
                } else if(j.Item.Length>0&&o?.QualifiedItemId!=j.Item)continue;
            }
            if(j.goal=="harvest"&&j.Item.Length>0&&(l.terrainFeatures.GetValueOrDefault(new Vector2(x,y)) is not HoeDirt crop||crop.crop==null||ItemRegistry.QualifyItemId(crop.crop.indexOfHarvest.Value)!=j.Item))continue;
            if(j.goal=="resource"&&(o==null||ResourceRules.Nodes.GetValueOrDefault(o.ItemId)!=j.Item))continue;
            if(j.goal=="wood"&&o?.IsTwig()!=true||j.goal=="fiber"&&o?.IsWeeds()!=true)continue;
            if(playerExecutor.ClaimsTile(l.NameOrUniqueName,x,y)||AgentTileBusy(l.NameOrUniqueName,x,y)){claimed=true;continue;}
            WorkChild(j,"companion.assign",new{actor_id=j.actor,skill=candidateSkill,target_id=c.GetProperty("target_id").GetString()},"labor",key);return;
        }
        // Empty candidates may mean a capability/path/cargo restriction, not a cleared map.
        bool any=j.goal switch {
            "water"=>l.terrainFeatures.Values.OfType<HoeDirt>().Any(d=>d.crop!=null&&!d.crop.dead.Value&&d.state.Value==0&&!d.readyForHarvest()),
            "harvest"=>l.terrainFeatures.Values.OfType<HoeDirt>().Any(d=>d.readyForHarvest()&&(j.Item.Length==0||Together.Shared.ForageCropRules.HarvestId(d.crop.forageCrop.Value,d.crop.whichForageCrop.Value,ItemRegistry.QualifyItemId(d.crop.indexOfHarvest.Value)??"")==j.Item)),
            "forage"=>l.objects.Values.Any(o=>o.isForage()&&(j.Item.Length==0||o.QualifiedItemId==j.Item))||l.terrainFeatures.Values.OfType<HoeDirt>().Any(d=>d.crop is not null&&d.crop.forageCrop.Value&&Together.Shared.ForageCropRules.CanHarvest(true,d.crop.whichForageCrop.Value)&&d.readyForHarvest()&&(j.Item.Length==0||Together.Shared.ForageCropRules.HarvestId(d.crop.forageCrop.Value,d.crop.whichForageCrop.Value,ItemRegistry.QualifyItemId(d.crop.indexOfHarvest.Value)??"")==j.Item)),
            "pet"=>Game1.getFarm().getAllFarmAnimals().Any(a=>a.currentLocation==l&&!a.wasPet.Value),
            "collect"=>l.objects.Values.Any(o=>o.heldObject.Value!=null&&o.readyForHarvest.Value),_=>true};
        bool met=j.requested==0&&!any;
        StopSemanticWork(j,met?"all_current_targets_completed":claimed?"targets_claimed_by_other_actor":"no_eligible_targets_check_capability_path_or_cargo",met);
    }
}
