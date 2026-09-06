using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Together;
public sealed partial class ModEntry {
    private sealed record CleanupTarget(FarmCell Tile,string Kind,string Scope,string Zone,bool ZoneTrees,int Energy);
    private Dictionary<FarmCell,string> maintenanceMask=new();
    private string maintenanceMaskKey="";
    private DateTime maintenanceAt;
    private void EnsureMaintenanceMask() {
        var farm=Game1.getFarm();string key=agentSaveEpoch+":"+Game1.Date.TotalDays+":"+string.Join(";",farm.buildings.Select(b=>$"{b.buildingType.Value},{b.tileX.Value},{b.tileY.Value}"));
        if(key==maintenanceMaskKey)return;
        var grid=new List<LayoutCell>();var anchors=PlayerExecutor.Exits(farm).Select(w=>new FarmCell(w.X,w.Y)).ToList();
        for(int y=0;y<farm.Map.Layers[0].LayerHeight;y++)for(int x=0;x<farm.Map.Layers[0].LayerWidth;x++) {
            var at=new Point(x,y);int clear=PlotClearCost(farm,at);
            grid.Add(new(new(x,y),false,PlayerExecutor.Passable(farm,at)||clear>0,false,false,false,false,0,clear));
        }
        foreach(var b in farm.buildings)if(b.humanDoor.Value.X>=0)anchors.Add(new(b.tileX.Value+b.humanDoor.Value.X,b.tileY.Value+b.humanDoor.Value.Y+1));
        maintenanceMask=ApplyFarmZoning(farm,grid,anchors);maintenanceMaskKey=key;
    }
    private (string Scope,string Zone,bool Trees) CleanupArea(FarmCell tile) {
        if(Data.FarmPolicy.Areas.Any(a=>a.Enabled&&a.Location=="Farm"&&tile.X>=a.X&&tile.X<a.X+a.Width&&tile.Y>=a.Y&&tile.Y<a.Y+a.Height))return ("protected","reserve",false);
        var explicitZone=Data.Maintenance.Zones.FirstOrDefault(z=>z.Contains(tile));
        if(explicitZone!=null)return ("zone:"+explicitZone.Id,explicitZone.Kind,explicitZone.AllowTrees);
        if(maintenanceMask.TryGetValue(tile,out var role))return (role=="service_road"?"roads":"courtyard",role=="service_road"?"roads":"courtyard",false);
        if(farmPlantPlans.Values.Any(p=>p.Epoch==agentSaveEpoch&&p.Day==Game1.Date.TotalDays&&p.Location=="Farm"&&p.Tiles.Contains(tile)))return ("fields","crop",false);
        var l=Game1.getFarm();
        for(int y=tile.Y-1;y<=tile.Y+1;y++)for(int x=tile.X-1;x<=tile.X+1;x++)if(l.terrainFeatures.GetValueOrDefault(new(x,y)) is HoeDirt {crop:not null})return ("fields","crop",false);
        return ("general","general",false);
    }
    private static bool CleanupScytheSafe(FarmCell tile) {
        // Native scythes sweep an area and can remove young trees/nearby weeds.
        // Use a single-target axe where collateral effects would leave the order
        // or its quota ambiguous. Grass and planted/decorated tiles stay intact.
        var farm=Game1.getFarm();
        for(int x=tile.X-3;x<=tile.X+3;x++)for(int y=tile.Y-3;y<=tile.Y+3;y++) {
            var at=new Vector2(x,y);if(x==tile.X&&y==tile.Y)continue;
            if(farm.objects.ContainsKey(at)||farm.terrainFeatures.ContainsKey(at))return false;
        }
        return true;
    }
    private List<CleanupTarget> CleanupTargets() {
        EnsureMaintenanceMask();var farm=Game1.getFarm();var result=new List<CleanupTarget>();
        foreach(var pair in farm.objects.Pairs) {
            var obj=pair.Value;string kind=obj.IsWeeds()?"weed":obj.IsTwig()?"twig":obj.BaseName=="Stone"?"stone":"";
            if(kind.Length==0||PlotClearCost(farm,pair.Key.ToPoint())<=0)continue;
            var tile=new FarmCell((int)pair.Key.X,(int)pair.Key.Y);var area=CleanupArea(tile);
            result.Add(new(tile,kind,area.Scope,area.Zone,area.Trees,kind=="weed"&&CleanupScytheSafe(tile)?0:Math.Max(4,obj.MinutesUntilReady*2+2)));
        }
        foreach(var pair in farm.terrainFeatures.Pairs)if(pair.Value is Tree tree&&!tree.tapped.Value) {
            var tile=new FarmCell((int)pair.Key.X,(int)pair.Key.Y);var area=CleanupArea(tile);
            result.Add(new(tile,tree.growthStage.Value>=5?"tree":"seedling",area.Scope,area.Zone,area.Trees,tree.growthStage.Value>=5?(int)Math.Ceiling(tree.health.Value*2+10):4));
        }
        return result;
    }
    private bool CleanupMatches(FarmCleanupOrder order,CleanupTarget target)=>
        (order.Scopes.Contains(target.Scope)||order.Scopes.Contains("all")||order.Scopes.Contains("fields")&&target.Zone=="crop"||order.Scopes.Contains("general")&&target.Zone=="production")&&FarmCleanupRules.Allowed(target.Kind,target.Zone,order.RemoveTrees,target.ZoneTrees);
    private Point[] CleanupLoose(FarmCleanupOrder order)=>PlayerExecutor.LooseDrops(Game1.getFarm()).Select(d=>(d.Pixel/64).ToPoint()).Distinct().Where(p=>{
        var tile=new FarmCell(p.X,p.Y);var area=CleanupArea(tile);return CleanupMatches(order,new(tile,"weed",area.Scope,area.Zone,area.Trees,0));
    }).ToArray();
    internal object ReadFarmMaintenance(JsonElement args) {
        var targets=CleanupTargets();string scope=AgentToolRegistry.Text(args,"scope");
        return new{summary=FarmMaintenanceSummary(targets),zones=Data.Maintenance.Zones,
            details=scope.Length==0?null:targets.Where(t=>t.Scope==scope).Take(32).Select(t=>new{t.Tile,t.Kind,t.Scope,estimated_energy=t.Energy}),
            note="摘要不发送全图；剩余数是当前对象，不保证都可达。牧草/果树/设备始终保留；保留林区不视为垃圾。树木只能在明确允许的crop/production分区清理。"};
    }
    private object FarmMaintenanceSummary(List<CleanupTarget>? targets=null) {
        targets??=CleanupTargets();
        var groups=targets.GroupBy(t=>new{t.Scope,t.Kind}).Select(g=>new{zone=g.Key.Scope,kind=g.Key.Kind,count=g.Count(),routine_allowed=g.Count(t=>FarmCleanupRules.Allowed(t.Kind,t.Zone,false,false)),estimated_energy=g.Sum(t=>t.Energy)}).OrderBy(g=>g.zone).ThenBy(g=>g.kind).ToArray();
        string revision=Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(AgentJson.Encode(groups))))[..12];
        return new{revision,automatic=Data.Maintenance.Enabled&&Data.Business.Enabled,areas=groups,
            loose_drops=PlayerExecutor.LooseDrops(Game1.getFarm()).GroupBy(d=>d.Item.QualifiedItemId).Select(g=>new{item=g.Key,chunks=g.Count()}),
            preserved=new{grass_tiles=Game1.getFarm().terrainFeatures.Values.Count(f=>f is Grass),fruit_trees=Game1.getFarm().terrainFeatures.Values.Count(f=>f is FruitTree),tapped_trees=Game1.getFarm().terrainFeatures.Values.Count(f=>f is Tree t&&t.tapped.Value)},
            orders=Data.Maintenance.Orders.Where(o=>o.Status!="complete"||o.Recurring).Take(8).Select(o=>new{o.Id,o.Scopes,o.Status,o.Reason,o.Completed,o.CompletedToday,o.DailyLimit,o.ReserveStamina,o.Until,allowance=CleanupAllowanceFor(o)}),
            tools="farm.cleanup下达持久整理目标；farm.maintenance读取摘要/调整日常政策；farm.zones设置保留区和用地区。无需逐格坐标。"};
    }
    internal object ConfigureFarmMaintenance(JsonElement args) {
        if(args.TryGetProperty("enabled",out var enabled)) {if(enabled.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("maintenance_boolean_required");Data.Maintenance.Enabled=enabled.GetBoolean();}
        return ReadFarmMaintenance(args);
    }
    internal object ConfigureFarmZones(JsonElement args) {
        if(!args.TryGetProperty("zones",out var values))return new{zones=Data.Maintenance.Zones};
        if(playerExecutor.Busy||WorkActorBusy("player"))throw new InvalidOperationException("wait_for_player_before_zoning_change");
        if(values.ValueKind!=JsonValueKind.Array||values.GetArrayLength()>24)throw new InvalidOperationException("invalid_zone_list");
        var farm=Game1.getFarm();var zones=new List<FarmZone>();
        foreach(var value in values.EnumerateArray()) {
            var zone=new FarmZone{Id=AgentToolRegistry.Text(value,"id"),Kind=AgentToolRegistry.Text(value,"kind"),X=AgentToolRegistry.Number(value,"x",-1),Y=AgentToolRegistry.Number(value,"y",-1),Width=AgentToolRegistry.Number(value,"width",0),Height=AgentToolRegistry.Number(value,"height",0)};
            if(value.TryGetProperty("allow_trees",out var trees)){if(trees.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("zone_boolean_required");zone.AllowTrees=trees.GetBoolean();}
            if(zone.Id.Length is <1 or >64||!FarmCleanupRules.ZoneKind(zone.Kind)||zone.X<0||zone.Y<0||zone.Width<1||zone.Height<1||zone.X+zone.Width>farm.Map.Layers[0].LayerWidth||zone.Y+zone.Height>farm.Map.Layers[0].LayerHeight||zone.AllowTrees&&zone.Kind is not("crop" or "production")||zones.Any(z=>z.Id==zone.Id||FarmCleanupRules.Overlap(z,zone)))throw new InvalidOperationException("invalid_overlapping_or_protected_tree_zone");
            zones.Add(zone);
        }
        Data.Maintenance.Zones=zones;Data.Autoplay.Record("farm_zones",AgentJson.Encode(zones));return new{zones};
    }
    internal object ConfigureCleanup(JsonElement args) {
        string id=AgentToolRegistry.Text(args,"request_id"),mode=AgentToolRegistry.Text(args,"mode","run");
        if(id.Length is <1 or >64||id=="daily-maintenance"||mode is not("run" or "pause"))throw new InvalidOperationException("invalid_cleanup_request");
        var old=Data.Maintenance.Orders.FirstOrDefault(o=>o.Id==id);
        if(mode=="pause") {
            if(old==null)throw new InvalidOperationException("cleanup_order_missing");old.Status="paused";
            if(old.TaskId.Length>0)AgentPlanCancel(JsonSerializer.SerializeToElement(new{ids=new[]{old.TaskId}}));return new{order=old};
        }
        if(args.TryGetProperty("scopes",out var raw)&&(raw.ValueKind!=JsonValueKind.Array||raw.EnumerateArray().Any(v=>v.ValueKind!=JsonValueKind.String)))throw new InvalidOperationException("cleanup_scopes_array_required");
        var scopes=raw.ValueKind==JsonValueKind.Array?raw.EnumerateArray().Select(v=>v.GetString()??"").Distinct().ToList():new(){"roads","courtyard","fields","general"};
        if(scopes.Count is <1 or >8||scopes.Any(s=>s is not("all" or "roads" or "courtyard" or "fields" or "general")&&!Data.Maintenance.Zones.Any(z=>s=="zone:"+z.Id)))throw new InvalidOperationException("unknown_cleanup_scope");
        int reserve=AgentToolRegistry.Number(args,"reserve_stamina",30),until=AgentToolRegistry.Number(args,"until",1800),limit=AgentToolRegistry.Number(args,"daily_limit",30);
        bool remove=false;if(args.TryGetProperty("remove_trees",out var trees)){if(trees.ValueKind is not(JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("cleanup_boolean_required");remove=trees.GetBoolean();}
        if(reserve is <15 or >270||until is <600 or >2200||until%100>59||limit is <1 or >120)throw new InvalidOperationException("invalid_cleanup_budget");
        if(old!=null) {
            if(!old.Scopes.OrderBy(x=>x).SequenceEqual(scopes.OrderBy(x=>x))||old.RemoveTrees!=remove||old.ReserveStamina!=reserve||old.Until!=until||old.DailyLimit!=limit)throw new InvalidOperationException("cleanup_request_id_reused_with_different_policy");
            if(old.Status=="paused"){old.Status="active";old.RetryAt=0;maintenanceAt=DateTime.MinValue;}return new{order=old,idempotent=true};
        }
        if(Data.Maintenance.Orders.Count(o=>o.Status!="complete")>=16)throw new InvalidOperationException("too_many_cleanup_orders");
        var order=new FarmCleanupOrder{Id=id,Scopes=scopes,RemoveTrees=remove,ReserveStamina=reserve,Until=until,DailyLimit=limit};Data.Maintenance.Orders.Add(order);
        Data.Maintenance.Orders.RemoveAll(o=>o.Status=="complete"&&!o.Recurring&&Data.Maintenance.Orders.IndexOf(o)<Data.Maintenance.Orders.Count-32);
        Data.Autoplay.Record("cleanup_requested",AgentJson.Encode(order));return new{order,note="已保存目标；自主运行时在已有农务后分批领取。暂停AI时只保存，不偷偷移动。"};
    }
    private bool MaintenanceProtects(GameLocation location,Point tile)=>Data.FarmPolicy.Areas.Any(a=>a.Enabled&&a.Location==location.NameOrUniqueName&&tile.X>=a.X&&tile.X<a.X+a.Width&&tile.Y>=a.Y&&tile.Y<a.Y+a.Height)
        ||location.IsFarm&&Data.Maintenance.Zones.Any(z=>z.Contains(new(tile.X,tile.Y))&&z.Kind is "woodland" or "pasture" or "reserve");
    private CleanupAllowance CleanupAllowanceFor(FarmCleanupOrder order) {
        var state=Data.Maintenance;
        if(state.BudgetDay!=Game1.Date.TotalDays){state.BudgetDay=Game1.Date.TotalDays;state.EnergyCommitted=0;state.MinutesUsed=0;state.LastMinute=-1;state.WasWorking=false;}
        // Counting owned crops must not enumerate reachable maps: MaterialLocations
        // runs A* for every destination and can block a Town frame for seconds.
        var fields=new[]{(GameLocation)Game1.getFarm()}.Concat(Game1.locations.Where(l=>l.IsGreenhouse))
            .Concat(Game1.getFarm().buildings.Select(b=>b.GetIndoors()).Where(l=>l!=null&&l.IsGreenhouse)).Distinct();
        int dry=fields.Sum(l=>l.terrainFeatures.Values.OfType<HoeDirt>().Count(d=>d.crop is not null&&!d.crop.dead.Value&&d.needsWatering()&&d.state.Value!=1));
        // Only submitted planting plans count; browsing alternative layouts does
        // not reserve the same seeds multiple times. Never trust an idle NPC to finish watering.
        var planned=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal&&t.spec.tool=="work.run"&&AgentToolRegistry.Text(t.spec.args,"goal")=="plant")
            .Select(t=>AgentToolRegistry.Text(t.spec.args,"plan_id")).Distinct().Where(farmPlantPlans.ContainsKey).Select(id=>farmPlantPlans[id]);
        int plots=planned.Where(p=>p.Day==Game1.Date.TotalDays&&p.Epoch==agentSaveEpoch)
            .SelectMany(p=>p.Tiles.Select(t=>(p.Location,t))).Distinct().Count(p=>Game1.getLocationFromName(p.Location)?.terrainFeatures.GetValueOrDefault(new(p.t.X,p.t.Y)) is not HoeDirt {crop:not null});
        // Before the investment planner runs (e.g. before the shop opens), leave
        // room for the agreed expansion, bounded by its manual-care capacity.
        if(Data.FarmInvestment.Enabled&&Data.FarmInvestment.Phase is not ("done" or "blocked"))
            plots=Math.Max(plots,Math.Min(Data.FarmInvestment.Plots,Data.FarmInvestment.ManualWaterLimit));
        return CleanupBudget.Calculate((int)Game1.player.Stamina,Game1.player.MaxStamina,order.ReserveStamina,dry,plots,state.EnergyCommitted,state.MinutesUsed);
    }
    private void TickFarmCleanup() {
        try{TickFarmCleanupCore();}catch(Exception e){maintenanceAt=DateTime.UtcNow.AddSeconds(30);Data.Autoplay.Record("cleanup_error",e is InvalidOperationException?e.Message:e.GetType().Name);WakeAgent("cleanup_requires_review");}
    }
    private void TickFarmCleanupCore() {
        if(!AutoplayRunning||DateTime.UtcNow<maintenanceAt)return;maintenanceAt=DateTime.UtcNow.AddSeconds(3);
        var state=Data.Maintenance;
        CleanupAllowanceFor(new());
        int minute=BusinessMinute;
        if(state.WasWorking&&state.LastMinute>=0)state.MinutesUsed+=Math.Max(0,minute-state.LastMinute);
        state.LastMinute=minute;state.WasWorking=semanticJobs.Values.Any(j=>j.goal=="cleanup"&&j.status=="running");
        if(state.Enabled&&Data.Business.Enabled&&!state.Orders.Any(o=>o.Id=="daily-maintenance"))state.Orders.Add(new(){Id="daily-maintenance",Recurring=true});
        foreach(var order in state.Orders) {
            FarmCleanupRules.NewDay(order,Game1.Date.TotalDays);
            if(order.Recurring&&order.Status=="complete"&&order.RetryAt<=BusinessMinute)order.Status="active";
            if(order.TaskId.Length>0) {
                var task=Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==order.TaskId);
                if(task!=null&&!task.Terminal&&task.state!="needs_review")continue;
                if(order.Status!="complete")order.Reason=task?.error??(task?.state=="succeeded"?"batch_complete":"reconcile_current_map_after_interruption");order.TaskId="";order.RetryAt=BusinessMinute+(task==null||task.state is "succeeded" or "cancelled" or "needs_review"?0:30);
            }
        }
        if(playerExecutor.Busy||WorkActorBusy("player")||Game1.activeClickableMenu!=null||Game1.eventUp||Game1.fadeToBlack||Game1.locationRequest!=null||!Game1.player.CanMove||OperationActorOccupied("player"))return;
        foreach(var order in state.Orders.Where(o=>o.Status=="active").OrderBy(o=>o.Recurring)) {
            if(order.Recurring&&(agentPending!=null||Game1.currentLocation.NameOrUniqueName!="Farm"))continue;
            if(order.Recurring&&(!state.Enabled||!Data.Business.Enabled)||order.RetryAt>BusinessMinute||order.TaskId.Length>0)continue;
            var remaining=CleanupTargets().Where(t=>CleanupMatches(order,t)).ToArray();
            if(remaining.Length==0&&order.PendingPickup.Count==0&&CleanupLoose(order).Length==0){order.Status="complete";order.Reason="current_scope_clear";order.RetryAt=BusinessMinute+60;Data.Autoplay.Record("cleanup_complete",AgentJson.Encode(order));continue;}
            if(Game1.timeOfDay>=order.Until||order.PendingPickup.Count==0&&remaining.Length>0&&FarmCleanupRules.RemainingBudget(order)==0){order.Reason="daily_budget_wait_next_day";continue;}
            var allowance=CleanupAllowanceFor(order);
            if(order.PendingPickup.Count==0&&remaining.Length>0&&allowance.Available<remaining.Min(t=>t.Energy)){order.Reason=allowance.Reason=="available"?"cleanup_insufficient_remaining_allowance":allowance.Reason;continue;}
            string id="cleanup-"+Guid.NewGuid().ToString("N");
            if(Data.Autoplay.Schedule.Tasks.Count>180)Data.Autoplay.Schedule.Archive();
            Data.Autoplay.Schedule.Submit(id,Data.Autoplay.Schedule.Revision,new(){new(){id=id,tool="work.run",args=JsonSerializer.SerializeToElement(new{goal="cleanup",cleanup_id=order.Id,count=FarmCleanupRules.RemainingBudget(order),location="Farm",reserve_stamina=order.ReserveStamina,until=order.Until}),day=Game1.Date.TotalDays,deadline=order.Until,purpose="持续整理农场；保留林区、牧草、设备和作物"}},Game1.Date.TotalDays);
            order.TaskId=id;order.Reason="queued_cleanup_batch";Data.Autoplay.Record("cleanup_dispatch",AgentJson.Encode(new{order,remaining=remaining.Length}));return;
        }
    }
    private void TickCleanupWork(SemanticJob job) {
        var order=Data.Maintenance.Orders.FirstOrDefault(o=>o.Id==job.CleanupId);
        if(order==null||order.Status!="active"||order.Recurring&&(!Data.Maintenance.Enabled||!Data.Business.Enabled)){StopSemanticWork(job,"cleanup_order_paused");return;}
        if(order.Scopes.Any(s=>s.StartsWith("zone:")&&!Data.Maintenance.Zones.Any(z=>s=="zone:"+z.Id))){StopSemanticWork(job,"cleanup_zone_removed_replan");return;}
        FarmCleanupRules.NewDay(order,Game1.Date.TotalDays);
        if(order.PendingPickup.Count>0) {
            job.PickupTiles=order.PendingPickup.Select(p=>new Point(p.X,p.Y)).ToList();
            WorkChild(job,"player.collect_drops",new{tiles=job.PickupTiles.Select(p=>new{x=p.X,y=p.Y})},"pickup_recovery");return;
        }
        var targets=CleanupTargets().Where(t=>CleanupMatches(order,t)).OrderBy(t=>FarmCleanupRules.Priority(t.Scope)).ThenBy(t=>Math.Abs(t.Tile.X-Game1.player.TilePoint.X)+Math.Abs(t.Tile.Y-Game1.player.TilePoint.Y)).ToArray();
        if(targets.Length==0){
            var loose=CleanupLoose(order).OrderBy(p=>Math.Abs(p.X-Game1.player.TilePoint.X)+Math.Abs(p.Y-Game1.player.TilePoint.Y)).Take(6).ToArray();
            if(loose.Length>0){job.PickupTiles=loose.ToList();WorkChild(job,"player.collect_drops",new{tiles=loose.Select(p=>new{x=p.X,y=p.Y})},"pickup_recovery");return;}
            order.Status="complete";order.Reason="current_scope_clear";order.RetryAt=BusinessMinute+60;StopSemanticWork(job,"cleanup_scope_clear",true);return;
        }
        if(FarmCleanupRules.RemainingBudget(order)==0){StopSemanticWork(job,"cleanup_daily_budget_reached",true);return;}
        var allowance=CleanupAllowanceFor(order);job.Reserve=allowance.Reserve;
        bool priorityWork=Data.Autoplay.Schedule.Tasks.Any(t=>!t.Terminal&&t.spec.actor=="player"&&t.spec.id!=order.TaskId&&!(t.spec.tool=="work.run"&&AgentToolRegistry.Text(t.spec.args,"goal")=="cleanup"));
        if(priorityWork||allowance.Available==0){order.Reason=priorityWork?"cleanup_yield_to_production":allowance.Reason;Data.Autoplay.Record("cleanup_yield",AgentJson.Encode(new{order.Id,order.Reason,allowance}));StopSemanticWork(job,order.Reason,true);return;}
        string reason="cleanup_no_reachable_frontier";
        var candidates=new Dictionary<FarmCell,(CleanupTarget Target,int Slot)>();
        foreach(var target in targets) {
            if(job.Excluded.Contains($"{target.Tile.X},{target.Tile.Y}")||AgentTileBusy("Farm",target.Tile.X,target.Tile.Y))continue;
            int slot=WorkSlot(i=>target.Kind=="weed"&&target.Energy==0?i is Tool t&&t.isScythe():target.Kind=="stone"?i is Pickaxe:i is Axe);
            if(slot<0){reason="cleanup_tool_missing";continue;}
            candidates[target.Tile]=(target,slot);
        }
        // Preserve the unfinished patch over storage detours. If its frontier
        // becomes unreachable, release it and try the rest on the next pass.
        var allCandidates=candidates;
        if(order.Patch is {} patch) {
            var local=candidates.Where(c=>Math.Abs(c.Key.X-patch.X)+Math.Abs(c.Key.Y-patch.Y)<=8).ToDictionary(c=>c.Key,c=>c.Value);
            if(local.Count>0)candidates=local;else order.Patch=null;
        }
        var begin=System.Diagnostics.Stopwatch.GetTimestamp();
        var route=CleanupRouting.Plan(new(Game1.player.TilePoint.X,Game1.player.TilePoint.Y),
            candidates.Values.Select(c=>new CleanupSite(c.Target.Tile,c.Target.Energy,FarmCleanupRules.Priority(c.Target.Scope))),
            p=>PlayerExecutor.Passable(Game1.getFarm(),new(p.X,p.Y)),allowance.Available,
            Math.Min(6,Math.Min(job.requested-job.completed,FarmCleanupRules.RemainingBudget(order))));
        if(route.Count>0) {
            order.Patch??=route[0].Site.Tile;
            Data.Maintenance.EnergyCommitted+=route.Sum(s=>s.Site.Energy);
            order.PendingPickup=route.Select(s=>s.Site.Tile).ToList();
            var batch=route.Select(s=>candidates[s.Site.Tile]).ToArray();
            Data.Autoplay.Record("cleanup_route",AgentJson.Encode(new{order.Id,start=new{x=Game1.player.TilePoint.X,y=Game1.player.TilePoint.Y},route,allowance,committed=Data.Maintenance.EnergyCommitted,ms=(System.Diagnostics.Stopwatch.GetTimestamp()-begin)*1000.0/System.Diagnostics.Stopwatch.Frequency}));
            WorkChild(job,"player.work",new{skill="clear",slot=batch[0].Slot,tiles=batch.Select(t=>new{x=t.Target.Tile.X,y=t.Target.Tile.Y}),
                steps=batch.Select((t,i)=>new{skill=t.Target.Kind=="tree"?"chop":t.Target.Kind=="seedling"?"prune":"clear",slot=t.Slot,stand=new{x=route[i].Stand.X,y=route[i].Stand.Y}})},"cleanup_labor",$"{batch[0].Target.Tile.X},{batch[0].Target.Tile.Y}");return;
        }
        if(candidates.Count>0&&candidates.Values.All(c=>c.Target.Energy>allowance.Available)){order.Reason="cleanup_insufficient_remaining_allowance";StopSemanticWork(job,order.Reason,true);return;}
        if(candidates!=allCandidates){order.Patch=null;job.phase="replan_frontier";return;}
        order.Reason=reason;StopSemanticWork(job,reason);
    }

    private void RecordCleanupProgress(SemanticJob job,int completed) {
        if(completed<=0)return;
        var order=Data.Maintenance.Orders.FirstOrDefault(o=>o.Id==job.CleanupId);if(order==null)return;
        order.Completed+=completed;order.CompletedToday+=completed;Data.Autoplay.Record("cleanup_progress",AgentJson.Encode(new{order.Id,order.Completed,order.CompletedToday,tile=job.Target,note="已核验的目标动作数，非掉落物数量；拾取结果另有原生回执"}));
    }
}
