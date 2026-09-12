using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Locations;
using StardewValley.Objects;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Together;
public sealed class TogetherLabApi {
    private readonly ModEntry mod;
    public TogetherLabApi(ModEntry mod){this.mod=mod;}
    public string RunScenario(string json)=>mod.RunLabScenario(json);
    public string GetDiagnostics()=>mod.StatusJson();
}
public sealed partial class ModEntry {
    public override object GetApi()=>new TogetherLabApi(this);
    internal string RunLabScenario(string json) {
        if(!Settings.EnableLab || !Context.IsWorldReady || Context.IsMultiplayer || Game1.player.Name!="AgentLab")throw new InvalidOperationException("isolated_lab_required");
        using var doc=JsonDocument.Parse(json);var root=doc.RootElement;string scenario=root.GetProperty("scenario").GetString()!;
        string Arg(string key,string fallback="")=>root.TryGetProperty(key,out var value)?value.GetString()??fallback:fallback;
        int Num(string key,int fallback=0)=>root.TryGetProperty(key,out var value)?value.GetInt32():fallback;
        var farm=Game1.getFarm();
        switch(scenario) {
            case "round2_observation": {
                var world=AgentWorld();var raw=new{inventory_plan=InventoryPlanning(),day=AgentDay(true),schedule=AgentPlanRead(true),recent=new[]{new{data=new{tool="world.read",result=world}}}};
                var rodCapability=FishingAcquisition();var bag=CapacityAdapter.Of(Game1.player);
                var nested=CapacityRelief.Choose(bag,new[]{new CapacityCandidate("store",5,0,true,"",Array.Empty<CapacityOp>())},1);
                return AgentJson.Encode(new{world,packed=ContextCompression.Pack(raw,100),candidates=OperatingOpportunities(),fishing_dependency=rodCapability,nested,nested_is_physical_constraint=CapacityState.IsConstraint(nested.RootCause)});
            }
            case "round2_declaration": {
                var call=new AgentCall{tool=Arg("tool"),args=root.GetProperty("args").Clone()};object result;
                try{result=QueueLegacyAction(call);}catch(InvalidOperationException e){result=new{status="failed",error=e.Message};}
                var observed=JsonSerializer.SerializeToElement(result,AgentJson.Options);RecordToolAttempt(call,observed);return AgentJson.Encode(result);
            }
            case "preparation_probe":
                PauseAutoplay("lab_preparation_probe");Settings.Autonomy=false;
                Data.Business.Enabled=false;Data.FarmInvestment.Enabled=false;Data.Maintenance.Enabled=false;Data.Autoplay.Routine.Enabled=false;
                Data.Autoplay=new(){Goal="原生整备与面板验证",Status="running",StartDay=Game1.Date.TotalDays,RunId=Guid.NewGuid().ToString("N")};
                Data.Autoplay.Routine.Enabled=false;Data.Autoplay.Campaign.Enabled=false;Data.Maintenance.Orders.Clear();foreach(var goal in Data.SharedGoals)goal.AutoExecute=false;
                Data.Autoplay.Survival.NativePlayerOnly=true;agentLabProbe=true;agentNeedsDecision=false;agentStarting=false;agentNext=DateTime.UtcNow.AddHours(1);agentWakeReasons.Clear();AttachMemoryArchive();
                return JsonSerializer.Serialize(AgentPlanRead());
            case "goal_infrastructure_probe":return AgentJson.Encode(EnsureStorageGoal()??throw new InvalidOperationException("no_authorized_infrastructure_goal"));
            case "pickup_block_probe":playerExecutor.LabPickupBlock=()=>Settings.EnableLab&&Context.IsWorldReady&&Game1.player.Name=="AgentLab";return AgentJson.Encode(new{armed=true,trigger="next real loose drop observed",kind="lab fault injection"});
            case "preparation_resume":
                StartAutoplay(Data.Autoplay.Goal);Settings.Autonomy=false;agentLabProbe=true;agentNeedsDecision=false;agentStarting=false;agentNext=DateTime.UtcNow.AddHours(1);agentWakeReasons.Clear();return AgentJson.Encode(AgentPlanRead());
            case "preparation_near_full":return AgentJson.Encode(LabPackNearFull());
            case "preparation_fault":
                if(Arg("code") is not ("loadout_stock_changed" or "loadout_storage_busy" or "loadout_native_capacity_changed"))throw new InvalidOperationException("unsupported_fault_probe");
                labLoadoutFault=Arg("code");return AgentJson.Encode(new{armed=labLoadoutFault,after_native_transfer=1});
            case "preparation_shape": {
                var task=new ScheduledAgentTask{spec=new(){id="lab-shape-"+Guid.NewGuid().ToString("N"),actor="player",tool=Arg("tool"),args=root.GetProperty("args").Clone(),day=Game1.Date.TotalDays}};
                var before=Body(Game1.player);
                try{bool ready=PrepareTaskKit(task);return AgentJson.Encode(new{ready,before,after=Body(Game1.player)});}
                catch(Exception ex){return AgentJson.Encode(new{error=ex.Message,before,after=Body(Game1.player),constraints=Data.Autoplay.Capacity.Constraints});}
            }
            case "preparation_read":return JsonSerializer.Serialize(new{preparationSummary,active=preparation!=null,body=Body(Game1.player),stores=SharedStorage().Select(s=>new{location=s.Location.NameOrUniqueName,tile=new[]{(int)s.Tile.X,(int)s.Tile.Y},items=KitInventoryEvidence(s.Chest)}),overlay=new{overlayTitle,overlayOffset,overlayVisible,bounds=new[]{overlayBounds.X,overlayBounds.Y,overlayBounds.Width,overlayBounds.Height},lines=overlayLines,overlayBuildMs,overlayWheelEvents,overlayWheelHandled,cursor=new[]{overlayLastCursor.X,overlayLastCursor.Y},selected_slot=Game1.player.CurrentToolIndex},autoplay=AutoplayDiagnostics()});
            case "overlay_capture":capturePath=Path.Combine(Helper.DirectoryPath,"screenshots","panel.png");return JsonSerializer.Serialize(new{requested=true});
            case "capacity_constraints":
                Settings.Autonomy=false;
                if(Arg("mode")=="block") {
                    if(CapacityAdapter.HasSlots(Game1.player,1))throw new InvalidOperationException("probe_requires_real_full_bag");
                    RecordCapacityConstraint("capacity_no_free_slot");
                }
                if(Arg("mode")=="guard") {
                    try{GuardCapacity("player",Arg("tool"),root.GetProperty("args"));return JsonSerializer.Serialize(new{blocked=false});}
                    catch(InvalidOperationException e){return JsonSerializer.Serialize(new{blocked=true,reason=e.Message});}
                }
                RefreshCapacityVersion();return JsonSerializer.Serialize(Data.Autoplay.Capacity);
            case "capacity_native_sleep":return JsonSerializer.Serialize(playerExecutor.Start("player.sleep",JsonSerializer.SerializeToElement(new{})));
            case "capacity_stack_audit": {
                var variants=new[]{ItemRegistry.Create("(O)388"),ItemRegistry.Create("(O)388",1,2),ItemRegistry.Create("(BC)130"),ItemRegistry.Create("(O)342"),ItemRegistry.Create("(O)342")};
                variants[3].modData["Together/lab-variant"]="left";variants[4].modData["Together/lab-variant"]="right";
                return JsonSerializer.Serialize(CapacityAdapter.StackAudit(Game1.player.Items.Where(i=>i!=null).Concat(variants)));
            }
            case "capacity_read": {
                var outputs=new[]{ItemRegistry.Create("(O)388"),ItemRegistry.Create("(O)388",1,2),ItemRegistry.Create("(BC)130")};
                var adapter=new CapacityAdapter(Game1.player,outputs);
                var recipe=new CraftingRecipe("Chest",false);
                return JsonSerializer.Serialize(new{body=Body(Game1.player),capacity=adapter.Snapshot,adapter.NativeComparisons,adapter.ConservativeSplits,
                    chest=recipe.doesFarmerHaveIngredientsInInventory()?CapacityAdapter.After(Game1.player,CapacityAdapter.Ingredients(Game1.player,recipe),recipe.createItem()):null,
                    action=playerExecutor.Current,menu=Game1.activeClickableMenu?.GetType().Name});
            }
            case "dual_body_start":return StartDualBodyProbe(Arg("chain"));
            case "dual_body_read":return ReadDualBodyProbe();
            case "dual_body_metadata":return JsonSerializer.Serialize(DualBodyMetadata());
            case "agent_tool":return JsonSerializer.Serialize(agentTools.Execute(Arg("tool"),root.GetProperty("args")));
            case "agent_start":StartAutoplay(Arg("goal"));break;
            case "survival_start":
                StartAutoplay(Arg("goal"));SetResumeConsent(true);Data.Autoplay.Survival.NativePlayerOnly=true;
                Settings.Autonomy=false;SurvivalRecord("survival_acceptance_started",new{day=Game1.Date.TotalDays,scope="native_farmer_only_until_stage_b",clock_rate=1});break;
            case "survival_failure_probe":
                if(Arg("kind")=="model")ModelUnavailable(new InvalidOperationException("lab_invalid_reply"),null,false);
                else if(Arg("kind")=="local")RecordAgentFailure("lab_path_failed","player");
                else throw new InvalidOperationException("unsupported_survival_probe");
                return AgentJson.Encode(Data.Autoplay.Survival);
            case "agent_pause":PauseAutoplay("lab_pause");break;
            case "agent_ui":
                // A watchdog may stop during a native dialogue. Replacing that menu
                // would lose its callback and strand the event when play resumes.
                if(Game1.activeClickableMenu!=null && Game1.activeClickableMenu is not AutoplayMenu)
                    return JsonSerializer.Serialize(new{opened=false,reason="native_menu_preserved",menu=Game1.activeClickableMenu.GetType().Name});
                Game1.activeClickableMenu=new AutoplayMenu(this);break;
            case "agent_schedule_probe": {
                PauseAutoplay("lab_schedule_probe");Game1.exitActiveMenu();
                Data.Autoplay=new(){Goal="LAB deterministic scheduler contract",Status="running",StartDay=Game1.Date.TotalDays,RunId=Guid.NewGuid().ToString("N")};
                agentNext=DateTime.UtcNow.AddMinutes(3);agentStarting=false;agentNeedsDecision=false;agentWakeReasons.Clear();
                agentLabProbe=true;return JsonSerializer.Serialize(AgentPlanRead());
            }
            case "agent_reply_probe": {
                if(!agentLabProbe || !AutoplayRunning)throw new InvalidOperationException("schedule_probe_required");
                operatingRequestBasis=FailureKnowledge.Hash(AgentJson.Encode(OperatingDecisionBasis()));
                agentRequestQueueRevision=Data.Autoplay.Schedule.Revision;agentRequestEpoch=agentGeneration;agentRequestDay=Game1.Date.TotalDays;agentWatch.Restart();
                agentPending=Task.FromResult(new ModelReply(Arg("reply"),0));return JsonSerializer.Serialize(new{synthetic_reply=true});
            }
            case "business_recovery_fixture": {
                PauseAutoplay("lab_business_recovery_fixture");Settings.Autonomy=false;Game1.exitActiveMenu();Data.Operating=new();Data.FarmPolicy=new();
                Data.Business=new();Data.FarmInvestment=new();Data.Maintenance=new(){Enabled=false};Data.Autoplay.Routine=new();Data.SharedGoals.Clear();Data.Reservations.Clear();
                foreach(var storage in SharedStorage().ToArray())storage.Location.objects.Remove(storage.Tile);
                for(int x=44;x<=54;x++)for(int y=25;y<=32;y++){farm.objects.Remove(new(x,y));farm.terrainFeatures.Remove(new(x,y));}
                for(int i=5;i<Game1.player.Items.Count;i++)Game1.player.Items[i]=null;
                Game1.player.Items[5]=ItemRegistry.Create("(O)388",5);Game1.player.craftingRecipes.TryAdd("Chest",0);
                Game1.warpFarmer("Farm",45,27,false);Game1.player.Stamina=Game1.player.MaxStamina;
                EnsureCustomPartner();var npc=FindCharacter(PartnerName)??throw new InvalidOperationException("fixture_partner_missing");
                npc.currentLocation?.characters.Remove(npc);farm.characters.Add(npc);npc.currentLocation=farm;npc.Position=new Vector2(50,27)*64;
                var pouch=Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{PartnerName}");pouch.Clear();pouch.Add(ItemRegistry.Create("(O)388",45));
                Data.Storage=new();break;
            }
            case "business_recovery_read":return JsonSerializer.Serialize(new{wood=Game1.player.Items.Where(i=>i?.QualifiedItemId=="(O)388").Sum(i=>i.Stack),cargo=PartnerCargoCount("(O)388"),boxes=SharedStorage().Count(),free_slots=Game1.player.freeSpotsInInventory()});
            case "operating_next_batch": {
                PauseAutoplay("lab_next_batch");Game1.exitActiveMenu();
                Game1.player.addItemToInventoryBool(ItemRegistry.Create("(O)472",4));Game1.player.Stamina=270;Game1.timeOfDay=1100;
                break;
            }
            case "operating_read":return JsonSerializer.Serialize(new{districts=Data.Operating.Districts,home=FarmHome(farm),stock=TeamStock("(O)388"),boxes=SharedStorage().Select(s=>new{tile=new[]{(int)s.Tile.X,(int)s.Tile.Y}}),labor=World().GetProperty("actors").EnumerateArray().Select(a=>new{id=a.GetProperty("id"),labor=a.GetProperty("labor")})});
            case "bedtime_review_fixture": {
                PauseAutoplay("lab_bedtime");Settings.Autonomy=false;Game1.exitActiveMenu();Data.Business=new();Data.FarmInvestment=new();Data.Maintenance=new(){Enabled=false};Data.FarmPolicy=new();Data.Autoplay.Routine=new();
                foreach(var location in Game1.locations)foreach(var dirt in location.terrainFeatures.Values.OfType<HoeDirt>())dirt.crop=null;
                for(int x=44;x<=48;x++)for(int y=26;y<=30;y++){farm.objects.Remove(new(x,y));farm.terrainFeatures.Remove(new(x,y));}
                farm.objects[new(47,28)]=new StardewValley.Object("313",1){TileLocation=new(47,28),HasBeenInInventory=false};
                for(int i=5;i<Game1.player.Items.Count;i++)Game1.player.Items[i]=null;
                Game1.warpFarmer("Farm",45,28,false);Game1.timeOfDay=1710;Game1.player.Stamina=23.3f;
                break;
            }
            case "fishing_continuity_fixture": {
                RunLabScenario("{\"scenario\":\"bedtime_review_fixture\"}");
                Data.Operating=new();Data.SharedGoals.Clear();Data.Reservations.Clear();Data.Partner.Enabled=false;
                for(int i=5;i<Game1.player.Items.Count;i++)Game1.player.Items[i]=null;
                Game1.player.Items[5]=ItemRegistry.Create("(T)BambooPole");
                Game1.player.Stamina=270;Game1.player.health=100;Game1.timeOfDay=1000;
                Game1.warpFarmer("Beach",30,12,false);break;
            }
            case "fishing_continuity_read":return JsonSerializer.Serialize(new{snapshot=AgentSnapshot(),rod=Game1.player.CurrentTool is FishingRod rod?new{rod.isFishing,rod.isTimingCast,rod.isCasting,rod.isReeling,rod.isNibbling,rod.hit,rod.fishCaught,rod.pullingOutOfWater,rod.castedButBobberStillInAir,rod.showingTreasure,rod.doneWithAnimation}:null});
            case "operating_followup_fixture": {
                RunLabScenario("{\"scenario\":\"bedtime_review_fixture\"}");Data.Operating=new();Data.Reservations.Clear();Data.SharedGoals.Clear();Data.Autoplay.Failures=new();
                for(int x=60;x<=72;x++)for(int y=20;y<=30;y++){farm.objects.Remove(new(x,y));farm.terrainFeatures.Remove(new(x,y));}
                foreach(var storage in SharedStorage().ToArray())storage.Location.objects.Remove(storage.Tile);
                var chest=new Chest(true){TileLocation=new(64,21)};chest.modData[WorkChestRole]="output";farm.objects[new(64,21)]=chest;
                Data.Maintenance=new(){Enabled=true,Zones=new(){new(){Id="lab-work",Kind="production",X=65,Y=24,Width=3,Height=3},new(){Id="lab-protected",Kind="reserve",X=69,Y=24,Width=2,Height=3}},
                    Orders=new(){new(){Id="lab-mixed",Scopes=new(){"zone:lab-work"},DailyLimit=6,Until=1800}}};
                for(int i=0;i<6;i++){var at=new Vector2(65+i%3,24+i/3);string id=i%3==0?"0":i%3==1?"294":"343";farm.objects[at]=new StardewValley.Object(id,1){TileLocation=at,HasBeenInInventory=false,MinutesUntilReady=1};}
                farm.objects[new(69,24)]=new StardewValley.Object("0",1){TileLocation=new(69,24),HasBeenInInventory=false};
                EnsureCustomPartner();var npc=FindCharacter(PartnerName)??throw new InvalidOperationException("fixture_partner_missing");npc.currentLocation?.characters.Remove(npc);farm.characters.Add(npc);npc.currentLocation=farm;npc.Position=new Vector2(66,23)*64;
                var pouch=Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{PartnerName}");pouch.Clear();
                Game1.warpFarmer("Farm",62,23,false);Game1.timeOfDay=1000;Game1.player.Stamina=270;Game1.player.Money=800;
                businessAt=DateTime.UtcNow.AddMinutes(30);maintenanceAt=DateTime.UtcNow.AddMinutes(30);cooperationAt=DateTime.MinValue;
                Data.Business=new(){Enabled=true,Expand=false};break;
            }
            case "operating_followup_read":return JsonSerializer.Serialize(new{snapshot=AgentSnapshot(),orders=Data.Maintenance.Orders,investment=Data.FarmInvestment,
                protected_present=farm.objects.ContainsKey(new(69,24)),remaining=farm.objects.Pairs.Count(p=>p.Key.X>=65&&p.Key.X<=67&&p.Key.Y>=24&&p.Key.Y<=25),
                crops=farm.terrainFeatures.Values.OfType<HoeDirt>().Count(d=>d.crop!=null&&!d.crop.dead.Value),ripe=farm.terrainFeatures.Values.OfType<HoeDirt>().Count(d=>d.readyForHarvest()),
                partner=World().GetProperty("actors"),calls=Data.Autoplay.Decisions});
            case "operating_failure_probe": {
                var task=new ScheduledAgentTask{spec=new(){id="failure-evidence",actor="player",tool="work.run",args=JsonSerializer.SerializeToElement(new{goal="wood",location="Farm",count=2})}};
                LearnActionResult(task,"failed","no_approved_material_demand");
                float energy=Game1.player.Stamina;Game1.player.Stamina-=4;
                task.spec.args=JsonSerializer.SerializeToElement(new{goal="wood",location="Farm",count=8,until=1800});bool blocked=false,released=true;
                try{CheckKnownFailure(task);}catch(InvalidOperationException e){blocked=e.Message.StartsWith("known_failure_conditions_unchanged");}
                Data.Autoplay.Agenda.Resources.Add(new(){Item="(O)388",Count=80,Purpose="测试新批准项目"});
                try{CheckKnownFailure(task);}catch{released=false;}
                Game1.player.Stamina=energy;Data.Autoplay.Agenda.Resources.Clear();Data.Autoplay.Failures=new();
                return JsonSerializer.Serialize(new{unrelated_energy_and_quantity_blocked=blocked,real_policy_change_released=released});
            }
            case "operating_reinvestment_fixture": {
                RunLabScenario("{\"scenario\":\"operating_followup_fixture\"}");Data.Business.Enabled=false;Data.Maintenance.Enabled=false;Data.Maintenance.Orders.Clear();Data.Partner.Enabled=false;Data.Operating=new();
                for(int x=62;x<=70;x++)for(int y=24;y<=29;y++){farm.objects.Remove(new(x,y));farm.terrainFeatures.Remove(new(x,y));}Data.Maintenance.Zones.Clear();
                for(int x=65;x<=67;x++){var crop=new Crop("472",x,26,farm);crop.currentPhase.Value=crop.phaseDays.Count-1;crop.dayOfCurrentPhase.Value=0;farm.terrainFeatures[new(x,26)]=new HoeDirt(1,farm){crop=crop};}
                Data.FarmInvestment=new(){Enabled=true,Day=Game1.Date.TotalDays,Phase="done",ReviewedCash=800,ReviewedCrops=3,ReviewedSeeds=0,BudgetPerDay=200,KeepGold=100,Plots=6,ManualWaterLimit=6};
                quoteEpoch="";seedQuotes.Clear();nextCropExpansionCheck=DateTime.MinValue;Game1.player.questLog.Clear();Game1.player.mailbox.Clear();Game1.player.Money=800;break;
            }
            case "shipping_capacity_fixture": {
                RunLabScenario("{\"scenario\":\"bedtime_review_fixture\"}");Data.Operating=new();Data.SharedGoals.Clear();Data.Reservations.Clear();Data.Business=new(){Enabled=true,Expand=false};Data.Partner.Enabled=false;
                foreach(var storage in SharedStorage().ToArray())storage.Location.objects.Remove(storage.Tile);
                for(int x=62;x<=71;x++)for(int y=16;y<=21;y++){farm.objects.Remove(new(x,y));farm.terrainFeatures.Remove(new(x,y));}
                farm.getShippingBin(Game1.player).Clear();
                Game1.player.Items[5]=ItemRegistry.Create("(T)BambooPole");
                for(int i=6;i<11;i++){var item=ItemRegistry.Create<StardewValley.Object>("(O)388");item.questItem.Value=true;Game1.player.Items[i]=item;}Game1.player.Items[11]=null;
                var chest=new Chest(true){TileLocation=new(65,17)};chest.modData[WorkChestRole]="output";
                foreach(int quality in new[]{0,1,2,4})chest.addItem(ItemRegistry.Create("(O)129",4,quality));
                chest.addItem(ItemRegistry.Create("(O)137",5,0));chest.addItem(ItemRegistry.Create("(O)137",4,2));farm.objects[new(65,17)]=chest;
                Game1.warpFarmer("Farm",66,18,false);Game1.timeOfDay=2240;
                businessAt=DateTime.UtcNow.AddMinutes(30);cooperationAt=DateTime.UtcNow.AddMinutes(30);break;
            }
            case "shipping_capacity_read":return JsonSerializer.Serialize(new{snapshot=AgentSnapshot(),free_slots=Game1.player.freeSpotsInInventory(),earned=Game1.player.totalMoneyEarned,
                stock=Game1.player.Items.Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer())).Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack)),
                shipped=Game1.player.basicShipped.Pairs.ToDictionary(p=>p.Key,p=>p.Value)});
            case "shipping_retention_fixture": {
                RunLabScenario("{\"scenario\":\"bedtime_review_fixture\"}");Data.Operating=new();Data.SharedGoals.Clear();Data.Reservations.Clear();Data.Business=new(){Enabled=true,Expand=false};Data.Partner.Enabled=false;
                foreach(var storage in SharedStorage().ToArray())storage.Location.objects.Remove(storage.Tile);
                farm.getShippingBin(Game1.player).Clear();
                string[] ids={"(O)92","(O)771","(O)24","(O)192","(O)388"};int[] counts={12,25,10,10,5};
                for(int i=0;i<ids.Length;i++)Game1.player.Items[5+i]=ItemRegistry.Create(ids[i],counts[i]);
                Game1.player.craftingRecipes.TryAdd("Torch",0);Game1.timeOfDay=1610;businessAt=DateTime.UtcNow.AddMinutes(6);cooperationAt=DateTime.UtcNow.AddMinutes(6);
                break;
            }
            case "shipping_retention_read":return JsonSerializer.Serialize(new{day=Game1.Date.TotalDays,money=Game1.player.Money,tile=new[]{Game1.player.TilePoint.X,Game1.player.TilePoint.Y},bag=Game1.player.Items.Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack)),bin=farm.getShippingBin(Game1.player).Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack))});
            case "farm_energy_fixture": {
                PauseAutoplay("lab_farm_energy");Settings.Autonomy=false;Game1.exitActiveMenu();Data.Operating=new();Data.FarmPolicy=new();Data.SharedGoals.Clear();Data.Reservations.Clear();Data.Business=new();Data.FarmInvestment=new();Data.Autoplay.Routine=new();Data.Maintenance=new(){Enabled=false};
                foreach(var dirt in farm.terrainFeatures.Values.OfType<HoeDirt>()){dirt.state.Value=1;dirt.crop=null;}
                for(int x=44;x<=58;x++)for(int y=25;y<=34;y++){farm.objects.Remove(new(x,y));farm.terrainFeatures.Remove(new(x,y));}
                Data.Maintenance.Zones=new(){new(){X=0,Y=0,Width=200,Height=27},new(){X=0,Y=31,Width=200,Height=200},new(){X=0,Y=27,Width=47,Height=4},new(){X=55,Y=27,Width=200,Height=4}};
                for(int x=47;x<=54;x++)for(int y=27;y<=30;y++)farm.objects[new(x,y)]=new StardewValley.Object("313",1){TileLocation=new(x,y),HasBeenInInventory=false};
                for(int i=5;i<Game1.player.Items.Count;i++)Game1.player.Items[i]=null;
                Game1.player.Items[5]=ItemRegistry.Create("(O)473",6);Game1.player.Items[6]=ItemRegistry.Create("(O)472",2);
                if(Game1.player.Items.OfType<WateringCan>().FirstOrDefault() is {} can)can.WaterLeft=can.waterCanMax;
                Game1.warpFarmer("Farm",46,28,false);Game1.timeOfDay=1000;Game1.player.Stamina=Num("stamina",270);
                if(Num("hold")==1){Data.FarmInvestment=new(){Enabled=true,BudgetPerDay=400,KeepGold=0,Plots=24,ManualWaterLimit=48,Day=Game1.Date.TotalDays};farm.objects[new(47,27)]=new StardewValley.Object("294",1){TileLocation=new(47,27),HasBeenInInventory=false};}
                break;
            }
            case "farm_energy_read":return JsonSerializer.Serialize(new{stamina=Game1.player.Stamina,pending=PendingFarmEnergy(),available=AvailablePlantingEnergy(),crops=farm.terrainFeatures.Pairs.Where(p=>p.Key.X>=47&&p.Key.X<=54&&p.Key.Y>=27&&p.Key.Y<=30&&p.Value is HoeDirt {crop:not null}).Select(p=>new{x=p.Key.X,y=p.Key.Y,watered=((HoeDirt)p.Value).state.Value==1}),objects=farm.objects.Pairs.Where(p=>p.Key.X>=47&&p.Key.X<=54&&p.Key.Y>=27&&p.Key.Y<=30).Select(p=>new{x=p.Key.X,y=p.Key.Y})});
            case "door_interaction_fixture": {
                PauseAutoplay("lab_door_interaction");Settings.Autonomy=false;Game1.exitActiveMenu();Data.Business=new();Data.FarmInvestment=new();Data.Maintenance=new(){Enabled=false};Data.Autoplay.Routine=new();
                var town=Game1.getLocationFromName("Town");var door=PlayerExecutor.Exits(town).First(e=>e.TargetName=="SeedShop");Game1.warpFarmer("Town",door.X,door.Y+1,false);Game1.timeOfDay=Num("time",1000);
                var npc=Game1.getCharacterFromName("Gus");npc.currentLocation?.characters.Remove(npc);town.characters.Add(npc);npc.currentLocation=town;npc.controller=null;npc.Halt();npc.Position=new Vector2(door.X,door.Y)*64;
                break;
            }
            case "loose_drop_read":return JsonSerializer.Serialize(PlayerExecutor.LooseDrops(Game1.currentLocation).Select(d=>new{item=d.Item.QualifiedItemId,position=new[]{d.Pixel.X,d.Pixel.Y},group=d.Source.GetHashCode(),native_owner=d.Source.player.Value?.UniqueMultiplayerID}).ToArray());
            case "agent_semantic_fixture": {
                PauseAutoplay("lab_semantic_fixture");Settings.Autonomy=false;Game1.exitActiveMenu();Game1.warpFarmer("Farm",62,17,false);
                Game1.player.Stamina=90;Game1.player.health=100;
                for(int i=0;i<Game1.player.Items.Count;i++)if(Game1.player.Items[i] is StardewValley.Object)Game1.player.Items[i]=null;
                foreach(var name in Data.People.Keys)Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{name}").Clear();
                for(int x=61;x<=70;x++)for(int y=17;y<=24;y++){farm.objects.Remove(new Vector2(x,y));farm.terrainFeatures.Remove(new Vector2(x,y));}
                string seed=DataLoader.Crops(Game1.content).First(c=>c.Value.Seasons.Any(s=>s.ToString().Equals(Game1.currentSeason,StringComparison.OrdinalIgnoreCase))).Key;
                for(int x=61;x<=63;x++)for(int y=18;y<=19;y++)farm.terrainFeatures[new Vector2(x,y)]=new HoeDirt(0,farm){crop=new Crop(seed,x,y,farm)};
                for(int x=67;x<=68;x++)for(int y=18;y<=23;y++)farm.objects[new Vector2(x,y)]=ItemRegistry.Create<StardewValley.Object>("(O)294");
                for(int x=64;x<=65;x++)for(int y=20;y<=23;y++){var o=ItemRegistry.Create<StardewValley.Object>("(O)343");o.MinutesUntilReady=1;farm.objects[new Vector2(x,y)]=o;}
                foreach(var can in Game1.player.Items.OfType<StardewValley.Tools.WateringCan>())can.WaterLeft=1;
                var box=new Chest(true);box.modData["stardewagent.together/chest-role"]="output";farm.objects[new Vector2(60,17)]=box;
                break;
            }
            case "agent_full_inventory_fixture": {
                PauseAutoplay("lab_full_inventory_fixture");Game1.exitActiveMenu();
                for(int i=0;i<Game1.player.Items.Count;i++)if(Game1.player.Items[i] is not Tool){Game1.player.Items[i]=ItemRegistry.Create(i==5?"(O)472":i==6?"(O)24":"(O)"+(388+i%3),i==6?5:10);}
                foreach(var name in Data.People.Keys) {var pouch=Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{name}");pouch.Clear();foreach(string id in new[]{"388","390","771","378","380","382","384","386"})pouch.Add(ItemRegistry.Create("(O)"+id,3));}
                break;
            }
            case "inventory_timing_fixture": {
                PauseAutoplay("lab_inventory_timing");Settings.Autonomy=false;Game1.exitActiveMenu();
                Data.Business=new();Data.FarmInvestment=new();Data.Maintenance=new(){Enabled=false};Data.Autoplay.Routine=new();
                for(int x=44;x<=53;x++)for(int y=26;y<=31;y++){farm.objects.Remove(new(x,y));farm.terrainFeatures.Remove(new(x,y));}
                var box=new Chest(true){TileLocation=new(44,30)};box.modData[WorkChestRole]="output";farm.objects[new(44,30)]=box;
                for(int i=5;i<Game1.player.Items.Count;i++)Game1.player.Items[i]=null;
                string[] contents={"472","388","390","771","92","330"};
                for(int i=0;i<contents.Length;i++)Game1.player.Items[i+5]=ItemRegistry.Create("(O)"+contents[i],15);
                if(Num("full")==1)Game1.player.Items[11]=ItemRegistry.Create("(O)382",15);
                for(int x=46;x<=49;x++)farm.objects[new(x,28)]=new StardewValley.Object("294",1){TileLocation=new(x,28),HasBeenInInventory=false};
                farm.objects[new(51,28)]=new StardewValley.Object("16",1){TileLocation=new(51,28),IsSpawnedObject=true};
                farm.terrainFeatures[new(50,29)]=new HoeDirt(0,farm);
                Game1.warpFarmer("Farm",45,27,false);Game1.player.Stamina=Game1.player.MaxStamina;
                Game1.player.CurrentToolIndex=5;Game1.player.netItemStowed.Value=false;Game1.player.UpdateItemStow();
                break;
            }
            case "inventory_timing_read":return JsonSerializer.Serialize(new{
                free_slots=Game1.player.freeSpotsInInventory(),stowed=Game1.player.netItemStowed.Value,held=Game1.player.ActiveObject?.QualifiedItemId,
                seeds=Game1.player.Items.Where(i=>i?.QualifiedItemId=="(O)472").Sum(i=>i.Stack),
                stored=(farm.objects.GetValueOrDefault(new(44,30)) as Chest)?.Items.Sum(i=>i?.Stack??0),
                planted=(farm.terrainFeatures.GetValueOrDefault(new(50,29)) as HoeDirt)?.crop!=null});
            case "agent_pause_notice_fixture": {
                Data.Autoplay.Status="running";PauseAutoplay("玩家按 F10 暂停接管");break;
            }
            case "agent_empty_can_fixture": {
                PauseAutoplay("lab_empty_can_fixture");Game1.exitActiveMenu();
                foreach(var can in Game1.player.Items.OfType<StardewValley.Tools.WateringCan>())can.WaterLeft=0;
                break;
            }
            case "agent_dead_crop_fixture": {
                PauseAutoplay("lab_dead_crop_fixture");Game1.exitActiveMenu();Game1.warpFarmer("Farm",66,18,false);
                for(int x=67;x<=69;x++){
                    var tile=new Vector2(x,18);farm.objects.Remove(tile);var crop=new Crop("472",x,18,farm);crop.dead.Value=x<69;
                    farm.terrainFeatures[tile]=new HoeDirt(0,farm){crop=crop};
                }
                break;
            }
            case "agent_policy_probe": {
                PauseAutoplay("lab_policy_probe");Game1.exitActiveMenu();
                Data.Autoplay.Status="running";Data.Autoplay.Detail="LAB POLICY PROBE — no model decision";
                agentNext=DateTime.UtcNow.AddSeconds(60);agentStarting=false;agentLabProbe=true;return JsonSerializer.Serialize(AgentDailyRead());
            }
            case "agent_night_notice_fixture": {
                // Replay a plain native notice for menu regression, not earned skill proof.
                var notice=new Point(0,1);if(!Game1.player.newLevels.Contains(notice))Game1.player.newLevels.Add(notice);
                break;
            }
            case "agent_resource_fixture": {
                PauseAutoplay("lab_resource_fixture");Settings.Autonomy=false;Game1.exitActiveMenu();Game1.warpFarmer("Farm",62,17,false);
                for(int x=66;x<=69;x++)for(int y=17;y<=24;y++){var tile=new Vector2(x,y);farm.terrainFeatures.Remove(tile);farm.objects.Remove(tile);}
                foreach(int x in new[]{67,68})for(int y=18;y<=23;y++)farm.objects[new Vector2(x,y)]=ItemRegistry.Create<StardewValley.Object>("(O)294");
                var stone=ItemRegistry.Create<StardewValley.Object>("(O)343");stone.MinutesUntilReady=2;farm.objects[new Vector2(63,18)]=stone;
                var forage=ItemRegistry.Create<StardewValley.Object>("(O)16");forage.IsSpawnedObject=true;farm.objects[new Vector2(63,17)]=forage;
                break;
            }
            case "agent_fixture": {
                foreach(var can in Game1.player.Items.OfType<StardewValley.Tools.WateringCan>())can.WaterLeft=can.waterCanMax;
                Game1.player.Stamina=Game1.player.MaxStamina;Game1.player.health=Game1.player.maxHealth;
                PauseAutoplay("lab_fixture");Settings.Autonomy=false;Game1.exitActiveMenu();
                Game1.warpFarmer("Farm",62,17,false);
                for(int x=61;x<=63;x++)for(int y=18;y<=19;y++){farm.objects.Remove(new Vector2(x,y));farm.terrainFeatures.Remove(new Vector2(x,y));}
                string seed=DataLoader.Crops(Game1.content).Where(c=>c.Value.Seasons.Any(s=>s.ToString().Equals(Game1.currentSeason,StringComparison.OrdinalIgnoreCase)) && ItemRegistry.GetData("(O)"+c.Key)!=null).OrderBy(c=>c.Value.DaysInPhase.Sum()).ThenBy(c=>c.Key,StringComparer.Ordinal).First().Key;
                Game1.player.Items[5]=ItemRegistry.Create("(O)"+seed,6);Game1.player.Items[6]=ItemRegistry.Create("(O)388",50);
                farm.animals.Remove(-449404285);farm.animals.Remove(-449404282);
                break;
            }
            case "cleanup_fixture": {
                PauseAutoplay("lab_cleanup_fixture");Settings.Autonomy=false;Game1.exitActiveMenu();
                Data.FarmPolicy=new();Data.Business=new();Data.FarmInvestment=new();Data.Autoplay.Routine=new();Data.Maintenance=new(){Enabled=false};
                maintenanceMaskKey="";maintenanceAt=DateTime.MinValue;
                for(int x=44;x<=55;x++)for(int y=27;y<=35;y++){var tile=new Vector2(x,y);farm.objects.Remove(tile);farm.terrainFeatures.Remove(tile);}
                foreach(int x in new[]{46,48,50,52})foreach(int y in new[]{28,30}) {
                    var tile=new Vector2(x,y);var obj=ItemRegistry.Create<StardewValley.Object>(x%3==0?"(O)294":x%3==1?"(O)343":"(O)674");
                    obj.TileLocation=tile;obj.HasBeenInInventory=false;obj.MinutesUntilReady=obj.BaseName=="Stone"?2:0;farm.objects[tile]=obj;
                }
                farm.objects[new(52,29)]=new StardewValley.Object("674",1){TileLocation=new(52,29),HasBeenInInventory=false};
                farm.terrainFeatures[new(53,29)]=new Grass(1,4);
                farm.terrainFeatures[new(50,31)]=new Tree("1",1);farm.terrainFeatures[new(52,32)]=new Tree("1",5);
                farm.terrainFeatures[new(54,34)]=new Tree("1",5);
                farm.objects[new(45,33)]=new StardewValley.Object("294",1){TileLocation=new(45,33),HasBeenInInventory=false};
                var chest=new Chest(true){TileLocation=new(44,30)};chest.modData["stardewagent.together/chest-role"]="output";farm.objects[new(44,30)]=chest;
                Game1.warpFarmer("Farm",45,27,false);Game1.player.Stamina=Game1.player.MaxStamina;
                for(int i=0;i<Game1.player.Items.Count;i++)if(Game1.player.Items[i] is not Tool)Game1.player.Items[i]=ItemRegistry.Create("(O)390",10);
                break;
            }
            case "cleanup_read": {
                var targets=farm.objects.Pairs.Where(p=>p.Key.X>=46&&p.Key.X<=52&&p.Key.Y>=28&&p.Key.Y<=32).Select(p=>new{x=p.Key.X,y=p.Key.Y,item=p.Value.QualifiedItemId}).ToArray();
                return JsonSerializer.Serialize(new{targets,young=farm.terrainFeatures.GetValueOrDefault(new(50,31)) is Tree,mature=farm.terrainFeatures.GetValueOrDefault(new(52,32)) is Tree,
                    grass=farm.terrainFeatures.GetValueOrDefault(new(53,29)) is Grass,protected_tree=farm.terrainFeatures.GetValueOrDefault(new(54,34)) is Tree,protected_twig=farm.objects.ContainsKey(new(45,33)),
                    chest=farm.objects.GetValueOrDefault(new(44,30)) is Chest,stored=(farm.objects.GetValueOrDefault(new(44,30)) as Chest)?.Items.Sum(i=>i?.Stack??0),orders=Data.Maintenance.Orders,
                    loose=PlayerExecutor.LooseDrops(farm).Count(d=>d.Pixel.X>=44*64&&d.Pixel.X<=57*64&&d.Pixel.Y>=26*64&&d.Pixel.Y<=36*64),
                    wood_carried=Game1.player.Items.Where(i=>i?.QualifiedItemId=="(O)388").Sum(i=>i.Stack)});
            }
            case "plant_bed_fixture": {
                PauseAutoplay("lab_plant_bed");Settings.Autonomy=false;Game1.exitActiveMenu();
                Data.FarmPolicy=new();Data.Business=new();Data.FarmInvestment=new();Data.Autoplay.Routine=new();
                Data.Maintenance=new(){Enabled=false};Data.Autoplay.Agenda=new();
                for(int i=0;i<Game1.player.Items.Count;i++)if(Game1.player.Items[i] is not Tool)Game1.player.Items[i]=null;
                foreach(var area in new[]{(0,0,46,100),(53,0,100,100),(46,0,7,28),(46,33,7,100)})
                    Data.FarmPolicy.Areas.Add(new(){Id=Guid.NewGuid().ToString("N"),Location="Farm",X=area.Item1,Y=area.Item2,Width=area.Item3,Height=area.Item4,Enabled=true});
                for(int x=44;x<=55;x++)for(int y=27;y<=34;y++){var tile=new Vector2(x,y);farm.objects.Remove(tile);farm.terrainFeatures.Remove(tile);}
                for(int x=46;x<=52;x++)for(int y=28;y<=32;y++)if((x+y)%2==0) {
                    var tile=new Vector2(x,y);var obj=ItemRegistry.Create<StardewValley.Object>(x%3==0?"(O)294":x%3==1?"(O)343":"(O)674");
                    obj.TileLocation=tile;obj.HasBeenInInventory=false;if(obj.BaseName=="Stone")obj.MinutesUntilReady=2;farm.objects[tile]=obj;
                }
                farm.terrainFeatures[new(54,34)]=new Tree("1",5);var bedChest=new Chest(true){TileLocation=new(44,30)};
                bedChest.modData["stardewagent.together/chest-role"]="output";farm.objects[new(44,30)]=bedChest;
                Game1.warpFarmer("Farm",45,27,false);Game1.player.Stamina=Game1.player.MaxStamina;
                Game1.player.Items[5]=ItemRegistry.Create("(O)472",15);
                foreach(var can in Game1.player.Items.OfType<StardewValley.Tools.WateringCan>())can.WaterLeft=0;
                break;
            }
            case "plant_bed_read": {
                var cells=(from x in Enumerable.Range(46,7) from y in Enumerable.Range(28,5) let tile=new Vector2(x,y)
                    let dirt=farm.terrainFeatures.GetValueOrDefault(tile) as HoeDirt
                    select new{x,y,obstacle=farm.objects.ContainsKey(tile),tilled=dirt!=null,planted=dirt?.crop!=null,watered=dirt?.state.Value==1}).ToArray();
                return JsonSerializer.Serialize(new{cells,tree=farm.terrainFeatures.GetValueOrDefault(new(54,34)) is Tree,chest=farm.objects.GetValueOrDefault(new(44,30)) is Chest});
            }
            case "agent_speed":Settings.AutoplayClockRate=AutoplaySpeed.Clock(Num("rate",2));break;
            case "goal_recipes":ReadGoalRecipes();return JsonSerializer.Serialize(goalRecipes.Values,jsonOptions);
            case "goal_suite":return GoalContracts();
            case "goal_refusal":return GoalRefusalContract();
            case "goal_add":AddSharedGoal(Arg("id"),Num("count",1));break;
            case "goal_menu":OpenGoals();break;
            case "goal_button":if(Game1.activeClickableMenu is SharedGoalsMenu goalsMenu)goalsMenu.CheckButton(Arg("label"));else throw new InvalidOperationException("goals_menu_required");break;
            case "goal_assign":AssignGoalNode(Arg("id"),Arg("node"),Arg("owner","player"));break;
            case "goal_work":RequestGoalWork(Arg("id"),Arg("node"),Num("forced")==1);break;
            case "goal_accept":AcceptProposal(Num("forced")==1);break;
            case "goal_pause":ToggleSharedGoal(Arg("id"));break;
            case "goal_options": {RefreshFacts(true);var world=World();var actor=Actor(world,Selected);return JsonSerializer.Serialize(actor.HasValue?OptionsFor(Selected,Current,SituationFor(Selected,actor.Value,world),actor.Value):new(),jsonOptions);}
            case "goal_fixture": {
                Cancel();api!.PrepareLab();Settings.Autonomy=false;Data.SharedGoals.Clear();Data.Projects.Clear();Data.FarmPolicy=new();Data.FarmHelp=true;
                Data.Knowledge.DiscoveredOnly=false;
                foreach(var c in farm.objects.Values.OfType<Chest>())c.Items.Clear();
                foreach(var name in Data.People.Keys)Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{name}").Clear();
                for(int i=0;i<Game1.player.Items.Count;i++)if(Game1.player.Items[i] is StardewValley.Object)Game1.player.Items[i]=null;
                foreach(var point in new[]{new Vector2(47,26),new Vector2(49,26)}) {var ore=ItemRegistry.Create<StardewValley.Object>("(O)751");ore.TileLocation=point;ore.MinutesUntilReady=2;farm.objects[point]=ore;}
                break;
            }
            case "knowledge_suite":return KnowledgeContracts();
            case "knowledge_query":return JsonSerializer.Serialize(Knowledge.Query(Arg("query"),Arg("id")==""?null:Arg("id")),jsonOptions);
            case "knowledge_ask":AskKnowledge(Arg("query"),Arg("id")==""?null:Arg("id"));break;
            case "knowledge_book":OpenKnowledge(Arg("query"));break;
            case "knowledge_button":
                if(Game1.activeClickableMenu is not EncyclopediaMenu bookMenu)throw new InvalidOperationException("encyclopedia_menu_required");
                bookMenu.CheckButton(Arg("label"));return JsonSerializer.Serialize(bookMenu.Evidence,jsonOptions);
            case "knowledge_invalidate":Knowledge.Invalidate();break;
            case "knowledge_mode":Data.Knowledge.DiscoveredOnly=Arg("value")!="all";CancelKnowledge();break;
            case "knowledge_pin":PinKnowledge(Arg("id"));break;
            case "knowledge_note":Data.Knowledge.Note(Arg("id"),Arg("text"));break;
            case "knowledge_export":return JsonSerializer.Serialize(new{ready=Knowledge.Ready,status=Knowledge.Status,entries=Knowledge.Index.Entries.Count,answer=KnowledgeAnswer,packet=LastKnowledge,book=Data.Knowledge},jsonOptions);
            case "forget":ClearMemories();break;
            case "performance_reset":measuredFrames.Clear();break;
            case "event_fixture": {
                var tile=Game1.player.TilePoint;
                Game1.currentLocation.startEvent(new Event($"none/-1000 -1000/farmer {tile.X} {tile.Y} 2/pause 5000/end"));break;
            }
            case "night_continue":
                if(Game1.activeClickableMenu is StardewValley.Menus.ShippingMenu shipping)shipping.receiveLeftClick(shipping.okButton.bounds.Center.X,shipping.okButton.bounds.Center.Y);
                else if(Game1.activeClickableMenu is StardewValley.Menus.LevelUpMenu level)level.receiveKeyPress(Microsoft.Xna.Framework.Input.Keys.Escape);
                break;
            case "model_name":Settings.Model=Arg("value","deepseek-flash");break;
            case "resume":Resume();break;
            case "habit":ConfirmHabit(Num("index"));break;
            case "fresh_person":
                Cancel();Data.People[Selected]=new Companion{EnergyDay=Game1.Date.TotalDays,DailyCompanion=true};break;
            case "profile":Current.Profile.Likes=Arg("likes",Current.Profile.Likes);Current.Profile.Dislikes=Arg("dislikes",Current.Profile.Dislikes);break;
            case "quest_contract":return NativeQuestContracts();
            case "mine_fixture": {
                Cancel();Settings.Autonomy=false;
                var mine=MineShaft.GetMine("UndergroundMine1");var ladder=mine.tileBeneathLadder+new Vector2(1,0);
                mine.objects.Remove(ladder);mine.createLadderAt(ladder);
                var npc=FindCharacter("Abigail")??throw new InvalidOperationException("fixture_target_missing");
                Game1.warpCharacter(npc,mine,mine.tileBeneathLadder+new Vector2(0,2));
                break;
            }
            case "combat_fixture": {
                var npc=FindCharacter("Abigail")??throw new InvalidOperationException("fixture_target_missing");var location=npc.currentLocation;
                var monster=new StardewValley.Monsters.GreenSlime(npc.Position+new Vector2(128,0),1){Health=12};
                location.characters.Add(monster);Game1.warpFarmer(location.NameOrUniqueName,npc.TilePoint.X,npc.TilePoint.Y+2,false);
                break;
            }
            case "configure":
                if(root.TryGetProperty("policy",out var policy))Data.FarmPolicy=JsonSerializer.Deserialize<FarmPolicy>(policy)??new();
                Data.FarmHelp=Data.FarmPolicy.Enabled;break;
            case "production": {
                Cancel();api!.PrepareLab();Settings.Autonomy=false;Data.Projects.Clear();Data.FarmPolicy=new();Data.FarmHelp=true;
                Data.FarmPolicy.Areas.Add(new(){Id="lab-plot",X=45,Y=28,Width=2,Height=1,Seed="(O)472"});
                for(int x=45;x<47;x++){farm.objects.Remove(new Vector2(x,28));farm.terrainFeatures.Remove(new Vector2(x,28));}
                var supplies=(Chest)farm.objects[new Vector2(44,26)];supplies.Items.Add(ItemRegistry.Create("(O)472",9));
                farm.piecesOfHay.Value=8;
                var coop=farm.buildings.FirstOrDefault(b=>b.modData.ContainsKey("together-lab"));
                if(coop==null) {
                    coop=new Building("Coop",new Vector2(58,20));coop.modData["together-lab"]="true";coop.daysOfConstructionLeft.Value=0;
                    farm.buildings.Add(coop);
                }
                if(coop.GetIndoors() is AnimalHouse house) {
                    house.objects.Clear();house.animals.Clear();house.animalsThatLiveHere.Clear();
                    var animal=new FarmAnimal("White Chicken",-449404284,Game1.player.UniqueMultiplayerID){Position=new Vector2(3,4)*64};
                    animal.home=coop;animal.currentLocation=house;
                    house.animals.Add(animal.myID.Value,animal);house.animalsThatLiveHere.Add(animal.myID.Value);
                }
                break;
            }
            case "business_fixture": {
                RunLabScenario(JsonSerializer.Serialize(new{scenario="production"}));
                farm.animals.Remove(-449404282);
                Data.Business=new();Data.FarmInvestment=new();Data.Autoplay.Routine=new();Data.SharedGoals.Clear();Data.Projects.Clear();
                Game1.player.Money=20000;Game1.timeOfDay=1000;Game1.player.Stamina=Game1.player.MaxStamina;
                foreach(int i in Enumerable.Range(0,Game1.player.Items.Count))if(Game1.player.Items[i] is StardewValley.Object)Game1.player.Items[i]=null;
                foreach(var supply in new[]{("(O)398",20),("(O)24",20),("(O)176",20),("(BC)105",1)})Game1.player.addItemToInventoryBool(ItemRegistry.Create(supply.Item1,supply.Item2));
                foreach(var spec in new[]{(48,25,"(BC)12"),(49,25,"(BC)15"),(50,25,"(BC)24")}) {
                    var tile=new Vector2(spec.Item1,spec.Item2);farm.terrainFeatures.Remove(tile);var machine=ItemRegistry.Create<StardewValley.Object>(spec.Item3);machine.TileLocation=tile;farm.objects[tile]=machine;
                }
                var treeTile=new Vector2(52,25);farm.objects.Remove(treeTile);farm.terrainFeatures[treeTile]=new Tree("1",5);
                break;
            }
            case "animal_products": {
                var type=Arg("type","White Cow");var animal=new FarmAnimal(type,-449404285,Game1.player.UniqueMultiplayerID){Position=new Vector2(46,25)*64};
                animal.age.Value=20;animal.currentProduce.Value=type=="Sheep"?"440":"184";animal.currentLocation=farm;animal.wasPet.Value=true;
                farm.animals[-449404285]=animal;break;
            }
            case "forage_fixture": {
                var item=ItemRegistry.Create<StardewValley.Object>("(O)18");item.TileLocation=new Vector2(48,29);item.IsSpawnedObject=true;farm.objects[item.TileLocation]=item;break;
            }
            case "clear_fixture": {
                Data.FarmPolicy.ClearDesignatedPlots=true;
                foreach(var entry in new[]{(45,"294"),(46,"674")}) {var item=ItemRegistry.Create<StardewValley.Object>(entry.Item2);item.TileLocation=new Vector2(entry.Item1,28);farm.objects[item.TileLocation]=item;}
                break;
            }
            case "economy": {
                Cancel();api!.PrepareLab();Settings.Autonomy=false;Data.Projects.Clear();Data.FarmPolicy=new(){DailyBudget=200,KeepGold=500};Data.FarmHelp=true;
                Data.FarmPolicy.Shopping.Add(new(){Id="lab-seeds",Item="(O)472",Shop="SeedShop",Count=2,MaxUnitPrice=50});
                Game1.player.Money=1000;Game1.player.modData.Remove("stardewagent.together/economy");
                var sale=new Chest(true){TileLocation=new Vector2(52,28)};sale.modData["stardewagent.together/chest-role"]="sell";
                sale.Items.Add(ItemRegistry.Create("(O)24",3));farm.objects[sale.TileLocation]=sale;
                break;
            }
            case "warp_actor": {
                var npc=FindCharacter(Arg("npc","Abigail"));string location=Arg("location","Farm");
                var destination=location.StartsWith("UndergroundMine")?MineShaft.GetMine(location):Game1.getLocationFromName(location);
                if(npc==null || destination==null)throw new InvalidOperationException("fixture_target_missing");
                Game1.warpCharacter(npc,destination,new Vector2(Num("x",44),Num("y",23)));break;
            }
            case "warp_player":Game1.warpFarmer(Arg("location","Farm"),Num("x",44),Num("y",23),false);break;
            case "town_key":Game1.player.HasTownKey=Num("enabled",0)!=0;break;
            case "day":Game1.dayOfMonth=Math.Clamp(Num("value",10),1,28);break;
            case "full_pouch": {
                var inventory=Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{Selected}");inventory.Clear();
                for(int i=0;i<12;i++)inventory.Add(ItemRegistry.Create("(W)0"));break;
            }
            case "gift_fixture": {
                var inventory=Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{Selected}");
                inventory.Add(ItemRegistry.Create("(O)145"));break;
            }
            case "full_output":foreach(var chest in farm.objects.Values.OfType<Chest>().Where(c=>c.modData.TryGetValue("stardewagent.together/chest-role",out var role)&&role=="output")) {
                chest.Items.Clear();for(int i=0;i<36;i++)chest.Items.Add(ItemRegistry.Create("(W)0"));
            }break;
            case "reservation": {
                Data.Projects.RemoveAll(p=>p.Kind=="lab-reservation");
                Data.Projects.Add(new(){Kind="lab-reservation",Title="验收预留",Needs=new(){new(){Item=Arg("item","(O)472"),Count=Num("count",9),Quality=Num("quality")}}});break;
            }
            case "wet_fields":foreach(var dirt in farm.terrainFeatures.Values.OfType<HoeDirt>())dirt.state.Value=HoeDirt.watered;break;
            case "obstacle": {
                var location=Game1.getLocationFromName(Arg("location","Farm"));var tile=new Vector2(Num("x"),Num("y"));
                if(Num("remove")==1)location.objects.Remove(tile);
                else {var stone=ItemRegistry.Create<StardewValley.Object>("(O)343");stone.TileLocation=tile;location.objects[tile]=stone;}break;
            }
            case "budget_limit":Settings.MaxCallsPerDay=Math.Clamp(Num("value",24),1,100);break;
            case "time":Game1.timeOfDay=Num("value",1000);break;
            case "weather":Game1.isRaining=Num("raining",1)!=0;break;
            case "auto":Settings.Autonomy=Num("enabled",1)!=0;Settings.AutoIntervalSeconds=Num("interval",30);autoAt=DateTime.MinValue;foreach(var p in Data.People.Values)p.Life.LastDecisionMinute=-1000;break;
            case "model":Send(Arg("message"));break;
            case "cancel":Cancel();break;
            case "select":Select(Arg("npc","Abigail"));break;
            case "preset":SetPreset(Arg("name","农场伙伴"));break;
            case "social":SetSocialMode(Arg("mode","normal"));break;
            case "challenge":StartChallenge();break;
            case "project":if(Arg("kind")=="farm")AddFarmProject();else if(Arg("kind")=="bundle")AddBundleProject();else AddProgressProject(Arg("kind"));break;
            case "plan": {
                var steps=JsonSerializer.Deserialize<List<Step>>(root.GetProperty("steps"))??new();
                if(steps.Count==0 || steps.Any(s=>!Decision.Labels.ContainsKey(s.skill)))throw new InvalidOperationException("invalid_lab_plan");
                StartFor(Selected,new(){decision="accept",title="真实场景检查",speech="开始检查",steps=steps},Num("forced")==1);break;
            }
            case "budget":Data.Calls=Num("calls",Settings.MaxCallsPerDay);RecordUsage();break;
            case "supply_remove":foreach(var chest in farm.objects.Values.OfType<Chest>().Where(c=>c.modData.TryGetValue("stardewagent.together/chest-role",out var role)&&role=="supplies"))chest.Items.Clear();break;
            case "bed": {
                var house=Utility.getHomeOfFarmer(Game1.player);var spot=house.GetPlayerBedSpot();
                Game1.warpFarmer(house.NameOrUniqueName,spot.X,spot.Y,false);break;
            }
            case "sleep": {
                if(Game1.currentLocation is not FarmHouse)throw new InvalidOperationException("sleep_fixture_requires_home");
                Game1.player.isInBed.Value=true;
                farm.animals.Remove(-449404285);
                farm.animals.Remove(-449404282); // remove only the homeless petting fixture
                Game1.currentLocation.answerDialogueAction("Sleep_Yes",null);break;
            }
            case "panel":Game1.activeClickableMenu=new CompanionMenu(this,Num("tab",5),Num("page"),Num("choice"));break;
            case "close":if(Game1.activeClickableMenu is CompanionMenu or EncyclopediaMenu or SharedGoalsMenu)Game1.exitActiveMenu();break;
            default:throw new InvalidOperationException("unknown_lab_scenario");
        }
        RefreshFacts(true);return StatusJson();
    }
}
