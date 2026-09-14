using System.Diagnostics;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private string RecruitForAgent(string name) {
        if(SinglePlayerMode)throw new InvalidOperationException("single_player_actor_required");
        var result=Request(name,"recruit");if(result.GetProperty("status").GetString()!="succeeded")throw new InvalidOperationException("native_companion_recruitment_failed");
        var actor=Actor(World(),name)??throw new InvalidOperationException("recruited_companion_not_observed");
        string id=actor.GetProperty("id").GetString()!;Person(name).DailyCompanion=true;agentKnownActors.Add(id);
        if(Data.Business.Enabled) {
            var routine=Data.Autoplay.Routine;foreach(string goal in new[]{"water","harvest","pet"})routine.Assignments[goal]=id;
            routine.Version++;routine.SubmittedDay=-1;
        }
        Persist();return id;
    }
    private PlayerExecutor playerExecutor=null!;
    private AgentToolRegistry agentTools=null!;
    private Task<ModelReply>? agentPending;
    private CancellationTokenSource? agentCancellation;
    private DateTime agentNext;
    private DateTime agentModelNotBefore;
    private int agentLastContextCharacters;
    private double agentLastPackMs;
    private readonly AgentDecisionPacing decisionPacing=new();
    private readonly Dictionary<string,(string Location,int X,int Y)> agentClaims=new();
    private int agentGeneration;
    private string agentSaveEpoch=Guid.NewGuid().ToString("N");
    private DateTime agentRequestedWait;
    private readonly AgentFailureTracker agentFailures=new();
    private readonly Stopwatch agentWatch=new();
    private double agentLastLatency;
    private bool agentStarting;
    public bool AutoplayRunning=>Data.Autoplay.Status=="running";
    private void SetupAutoplay() {
        playerExecutor=new(){OpportunisticItemAllowed=item=>!KitProtected(item),RouteObserved=route=>Data.Autoplay.Record("route_segment",AgentJson.Encode(new{route,task=Data.Autoplay.Schedule.Tasks.Where(t=>t.state=="running").Select(t=>new{t.spec.id,t.spec.tool,t.spec.purpose}),work=semanticJobs.Values.Where(j=>j.status=="running").Select(j=>new{j.command_id,j.goal,j.phase,j.ChildKind,j.Storing,j.location})})),NativeFinished=a=>{try{RecordNativePurchases(a);ObserveQualityReceipt(a);OnNativeWorkFinished(a.command_id);}catch(Exception e){PauseAutoplay("quality_evidence_failed:"+e.Message);}},ValidateOperation=ValidateNativeOperation,RecruitCompanion=RecruitForAgent,ApplyProfession=menu=>ApplyProfessionPolicy(menu)||SurvivalProfession(menu),ApplyNightPolicy=ApplyFamilyNightPolicy,NativeSleepRequested=CaptureQualitySleep,ValidateConsumption=ValidatePlayerConsumption,PlacementProtected=IsPlacementProtected,FacilityPlaced=GoalFacilityPlaced,FacilityCosts=GoalFacilityCosts,ShopOpened=()=>ObserveShop(JsonSerializer.SerializeToElement(new{}))};agentTools=new(this,playerExecutor);
        FishingInput.Install(ModManifest.UniqueID,playerExecutor);
        ArcadeInput.Install(ModManifest.UniqueID,playerExecutor);
        // Release our path controller before the next native update can trigger the same warp again.
        Helper.Events.GameLoop.UpdateTicking+=(_,_)=>playerExecutor.ObserveNativeTransition();
        Helper.Events.GameLoop.Saved+=(_,_)=>{playerExecutor.Saved();Data.Autoplay.Survival.LastSavedDay=Game1.Date.TotalDays;if(AutoplayRunning)SurvivalRecord("native_saved",new{day=Game1.Date.TotalDays,Data.Autoplay.NativeSleepRequestedDay,save=Game1.uniqueIDForThisGame});businessWriter.Flush();};
        Helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{memoryArchive?.Flush();businessWriter.Flush();};
        Helper.Events.GameLoop.DayStarted+=(_,_)=>{
            if(playerExecutor.DayStarted())Data.Autoplay.ReconcileSleep(Game1.Date.TotalDays);
            FinalizeQualityDay();SurvivalNewDay();
            if(AutoplayRunning) {Data.Autoplay.Record("day_started",AgentJson.Encode(AgentSnapshot()));WakeAgent("new_day");agentNext=DateTime.UtcNow.AddMilliseconds(250);}
        };
        Helper.Events.Input.ButtonPressed+=(_,e)=>{
            if(AutoplayRunning && e.Button==SButton.F10){Helper.Input.Suppress(e.Button);PauseAutoplay("玩家按 F10 暂停接管");}
            else if(AutoplayRunning && e.Button is SButton.W or SButton.A or SButton.S or SButton.D or SButton.Up or SButton.Down or SButton.Left or SButton.Right or SButton.Escape)
                PauseAutoplay("玩家接回控制");
        };
        Helper.ConsoleCommands.Add("together_agent","start <goal> / resume / pause / status",(_,args)=>{
            if(!Context.IsWorldReady || args.Length==0)return;
            switch(args[0]) {
                case "start":StartAutoplay(string.Join(" ",args.Skip(1)));break;
                case "resume":StartAutoplay(Data.Autoplay.Goal);break;
                case "pause":PauseAutoplay("玩家暂停");break;
                case "speed":Notice="自主游玩固定为游戏原生时间流速。";break;
                case "status":Monitor.Log(JsonSerializer.Serialize(AutoplayDiagnostics()),LogLevel.Info);break;
            }
        });
    }
    public void StartAutoplay(string goal) {
        if(!Context.IsWorldReady || Context.IsMultiplayer || !canPersist){Notice="自主接管需要已载入的单人存档。";return;}
        if(string.IsNullOrWhiteSpace(goal) || goal.Length>2000){Notice="请给出不超过2000字的目标。";return;}
        if(AutoplayRunning)return;
        if(playerExecutor.Busy){Notice="先等待当前原生动作结束，或取消后再接管。";return;}
        generation++;pending=null;pendingGoalWork=null;pendingKnowledge=false;
        foreach(var name in Data.People.Keys.ToArray()) {
            var p=Person(name);if(p.Job is {Status:"active" or "waiting"} j){if(j.Command!=null)api?.CancelAction(j.Command);if(j.TravelCommand!=null)api?.CancelAction(j.TravelCommand);j.Command=null;j.TravelCommand=null;j.Status="paused";}
        }
        ResetAgentRuntime();playerExecutor.ClearStopped();dayReviewed=-1;
        if(Data.Autoplay.Goal!=goal || Data.Autoplay.RunId.Length==0)Data.Autoplay=new(){StartDay=Game1.Date.TotalDays,Memory=Data.Autoplay.Memory,Operations=Data.Autoplay.Operations,Failures=Data.Autoplay.Failures,Capacity=Data.Autoplay.Capacity,ProfessionChoices=Data.Autoplay.ProfessionChoices,Routine=Data.Autoplay.Routine,Campaign=Data.Autoplay.Campaign};
        AttachMemoryArchive();
        Data.Autoplay.RunId=Guid.NewGuid().ToString("N");Data.Autoplay.Survival.NewDay(Game1.Date.TotalDays);
        if(Settings.SinglePlayerAutoplay)Data.Autoplay.Survival.NativePlayerOnly=true;
        Data.Autoplay.Record("new_run","开始新的接管片段。只有本片段的 tool_result 和 action_result 才是你实际调用工具的证据，目标文字不是完成记录。");
        Data.Autoplay.Schedule.Prepare=PrepareOperation;
        Data.Autoplay.Goal=goal;Data.Autoplay.Status="running";Data.Autoplay.Detail="DeepSeek 接管；F10 或方向键随时暂停。";
        EnsureCustomPartner();
        agentKnownActors.Clear();agentKnownActors.Add("player");if(!SinglePlayerMode)foreach(var actor in World().GetProperty("actors").EnumerateArray())agentKnownActors.Add(actor.GetProperty("id").GetString()!);agentIdleSignature="";
        agentStarting=true;WakeAgent("start_or_resume");agentNext=DateTime.UtcNow;Notice=Data.Autoplay.Detail;
    }
    public void PauseAutoplay(string reason) {
        bool wasRunning=AutoplayRunning;
        ResetAgentRuntime();Data.Autoplay.Status="paused";Data.Autoplay.Survival.AutoResume=false;Data.Autoplay.Detail=reason;Notice=reason;
        try{SetResumeConsent(false);}catch(Exception e){Monitor.Log("无法保存自动恢复撤销："+e.GetType().Name,LogLevel.Error);}
        if(wasRunning && !reason.StartsWith("lab_")){agentToast="自主游玩已暂停："+FriendlyAgentReason(reason);agentToastUntil=DateTime.UtcNow.AddSeconds(8);}
    }
    private void ResetAgentRuntime() {
        CancelDecisionContinuation("runtime_reset");ResetPreparation();labEarlyStorage=false;
        maintenanceMaskKey="";maintenanceAt=DateTime.MinValue;
        Data.Maintenance.WasWorking=false;Data.Maintenance.LastMinute=-1;
        agentModelNotBefore=DateTime.MinValue;decisionPacing.Reset();
        agentGeneration++;agentLabProbe=false;agentFailures.Clear();agentRequestedWait=DateTime.MinValue;agentCancellation?.Cancel();agentCancellation?.Dispose();agentCancellation=null;agentPending=null;
        foreach(var task in Data.Autoplay.Schedule.Tasks.Where(t=>t.state=="running"&&t.spec.actor!="player"))try{if(task.command_id!=null)AgentReceipt(task.command_id,true);}catch{}
        ResetSemanticWork();Data.Autoplay.Schedule.Suspend();agentWakeReasons.Clear();agentNeedsDecision=true;agentEventSignature="";
        agentClaims.Clear();playerExecutor?.Cancel();agentTools?.Reset();
    }
    private static string FriendlyAgentReason(string reason) {
        if(reason.Contains("watering_can_empty"))return "水壶已空，需要补水或安排伙伴浇水";
        if(reason.Contains("work_effect_not_observed"))return "操作未产生预期效果，需要重新检查工具和目标";
        if(reason.Contains("path_stalled"))return "路线受阻，需要重新安排";
        if(reason.Contains("JsonException"))return "模型回复格式连续错误，计划已保留";
        return reason.Length<=48?reason:reason[..48]+"…";
    }
    private string agentToast="";
    private DateTime agentToastUntil;
    private string AgentHud() {
        if(!AutoplayRunning)return "自主游玩已暂停 · "+FriendlyAgentReason(Data.Autoplay.Detail);
        string Lane(bool player) {
            var t=Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.state=="running" && (t.spec.actor=="player")==player);
            if(t==null)return "待安排";
            string skill=AgentToolRegistry.Text(t.spec.args,"skill",t.spec.tool);
            return skill switch {"water"=>"浇水","clear_dead"=>"清枯苗","clear"=>"清理采集","mine"=>"挖矿","plant"=>"播种","till"=>"翻土","forage"=>"拾取资源","harvest"=>"收获","player.travel" or "travel"=>"赶路","player.move"=>"走向目标","player.sleep"=>"回家过夜",_=>"执行任务"};
        }
        return (agentPending!=null?$"思考中 {agentWatch.Elapsed.TotalSeconds:0}秒 · ":"执行中 · ")+$"玩家：{Lane(true)}"+(SinglePlayerMode?"":$" · 队友：{Lane(false)}")+" · F10 暂停";
    }
    private object AgentSnapshot()=>new{day=Game1.Date.TotalDays,date=Game1.Date.ToString(),time=Game1.timeOfDay,clock_rate_while_idle=AutoplaySpeed.Clock(Settings.AutoplayClockRate),game_minutes_per_real_second_while_idle=AutoplaySpeed.Clock(Settings.AutoplayClockRate)*10/7,night_warning=Game1.timeOfDay>=2200?"接近深夜，优先安排返家；不要等待到凌晨两点":null,location=Game1.currentLocation.NameOrUniqueName,
        tile=new[]{Game1.player.TilePoint.X,Game1.player.TilePoint.Y},can_move=Game1.player.CanMove,using_tool=Game1.player.UsingTool,health=Game1.player.health,stamina=Game1.player.Stamina,money=Game1.player.Money,
        menu=Game1.activeClickableMenu?.GetType().Name,event_up=Game1.eventUp,minigame=Game1.currentMinigame?.GetType().Name,player_action=playerExecutor.Current is {} action?new{action.command_id,action.skill,action.status,action.phase,action.error,action.completed}:null};
    private object AutoplayDiagnostics()=>new{state=Data.Autoplay,snapshot=AgentSnapshot(),pending=agentPending!=null,last_model_ms=agentLastLatency,waiting=Data.Autoplay.Schedule.Tasks.Where(t=>t.state=="running").Select(t=>new{t.spec.id,t.spec.actor,t.command_id}),schedule=AgentPlanRead(),work=semanticJobs.Values.TakeLast(12),decision_reasons=agentWakeReasons,
        model_context=new{last_characters=agentLastContextCharacters,core_tools=AgentToolDiscovery.CoreNames.Length,core_definitions_characters=AgentJson.Encode(AgentToolDiscovery.Core(AgentToolRegistry.Catalog)).Length,decisionPacing.QueriesWithoutProgress,not_before=agentModelNotBefore},continuation=deferredDecision==null?null:new{deferredDecision.Intent,deferredDecision.Index,deferredDecision.After},model=Settings.Model,clock_interval=Game1.gameTimeInterval,clock_rate=Settings.AutoplayClockRate,decision_delay_ms=Settings.AutoplayDecisionDelayMs,source="DeepSeek with native maintenance/end-day fallback",preparation=preparationSummary,overlay=new{overlayBuildMs,overlayOffset,overlayVisible}};
    private void TickAutoplay() {
        long stage=Stopwatch.GetTimestamp();playerExecutor.Tick();FrameStage("player_executor",ref stage);TickSemanticWork();FrameStage("semantic",ref stage);
        if(!AutoplayRunning)return;
        if(Context.IsMultiplayer){PauseAutoplay("multiplayer_not_supported");return;}
        TickSurvivalQuality();TickBusinessTelemetry();if(!AutoplayRunning)return;
        try{if(TickAutomaticMenus())return;}catch(Exception e){EnterSurvival("sleep","automatic_menu_failed:"+e.Message);}
        if(TickSurvival())return;
        TickCustomPartner();TickDailyAutomation();TickGoalAutomation();TickAgentSchedule();TickDecisionContinuation();FrameStage("schedule",ref stage);
        if(deferredDecision!=null)return;
        if(SurvivalOwnsDay)return;
        TickDailyAutomation();FrameStage("daily",ref stage);
        TickGoalAutomation();FrameStage("goal_dependencies",ref stage);
        TickFarmBusiness();TickCooperativeBusiness();FrameStage("business",ref stage);
        TickFarmInvestment();FrameStage("investment",ref stage);
        TickFarmCleanup();FrameStage("cleanup",ref stage);
        TickProgressCampaign();FrameStage("goals",ref stage);
        ObserveAgentEvents();TickOperationTelemetry();TickBusinessTelemetry();FrameStage("telemetry",ref stage);
        if(playerExecutor.Busy && playerExecutor.Current?.skill is "player.beach" or "player.crab_pots" or "player.craft" or "player.cook" or "player.buy" or "player.claim_reward" or "player.collect_reward" or "player.donate_museum" or "player.build" or "player.bundle" or "player.treasure" or "player.collect_home_gifts" or "player.walnuts" or "player.volcano_step" or "player.forge" or "player.island_upgrade" or "player.arcade" or "player.read_mail" or "player.watch_tv" or "player.transport" or "player.repair_boat" or "player.read_book" or "player.mastery" or "player.orchard" or "player.joja" or "player.ship_items" or "player.order_donate" or "player.animal" or "player.geodes" or "player.buy_animal" or "player.upgrade_house")return;
        // Queue polling/dispatch above continues during HTTP; neither actor waits for the other.
        if(agentPending is {IsCompleted:true}) {
            var task=agentPending;agentPending=null;agentLastLatency=agentWatch.Elapsed.TotalMilliseconds;
            string? unappliedReply=null;bool applying=false;
            try {
                var reply=task.GetAwaiter().GetResult();Data.Autoplay.Record("context_packed",AgentJson.Encode(new{characters=agentLastContextCharacters,background_ms=agentLastPackMs}));unappliedReply=reply.Json;Data.Tokens+=reply.Tokens;RecordUsage();RecordAgentUsage(reply);
                if(agentRequestEpoch!=agentGeneration || agentRequestDay!=Game1.Date.TotalDays || agentRequestQueueRevision!=Data.Autoplay.Schedule.Revision || DecisionBasisChanged()) {
                    Data.Autoplay.Record("stale_decision","请求期间日期/会话/现金/工具/种子/预留/任务发生相关变化；旧决策需重新核算，未执行其动作。");WakeAgent("stale_response");
                } else {
                    var turn=AgentTurn.Parse(reply.Json);applying=true;Data.Autoplay.Survival.ModelFailures=0;Data.Autoplay.Decisions++;Data.Autoplay.Plan=turn.plan;
                    Data.Autoplay.Record("decision",reply.Json);if(turn.speech.Length>0&&!SinglePlayerMode)Say(Selected,turn.speech);
                    decisionIntent="decision-"+Data.Autoplay.Decisions;
                    bool followup=false,hadToolError=false;
                    int turnRevision=Data.Autoplay.Schedule.Revision;
                    (followup,hadToolError)=ApplyDecisionCalls(turn.calls,turnRevision);
                    bool allActorsHaveWork=AgentWorkCovered()&&!NeedsAgentMenuDecision;
                    if(followup&&AgentPollingPolicy.Defer(allActorsHaveWork,hadToolError,turn.calls.Select(c=>c.tool)))followup=false;
                    if(followup)WakeAgent("tool_results");
                    bool action=turn.calls.Any(c=>AgentSchedule.Queueable(c.tool)||c.tool=="plan.submit");
                    int delay=decisionPacing.Observe(Data.Autoplay.VerifiedActions,action,hadToolError);
                    agentModelNotBefore=DateTime.UtcNow.AddSeconds(delay);
                    agentNext=DateTime.UtcNow.AddMilliseconds(AutoplaySpeed.DecisionDelay(Settings.AutoplayDecisionDelayMs));
                    if(agentRequestedWait>agentNext)agentNext=agentRequestedWait;
                    decisionIntent="";TickAgentSchedule();
                }
            }catch(Exception e){ModelUnavailable(e,unappliedReply,applying);}finally{decisionIntent="";}
        }
        if(deferredDecision!=null || !AutoplayRunning || SurvivalOwnsDay || agentLabProbe || agentPending!=null || DateTime.UtcNow<agentNext || Thinking || Game1.fadeToBlack || Game1.currentMinigame!=null)return;
        if(AgentDecisionPacing.CanDefer(AgentWorkCovered(),NeedsAgentMenuDecision)&&!agentWakeReasons.Contains("danger"))return;
        if(DateTime.UtcNow<agentModelNotBefore&&!NeedsAgentMenuDecision&&!agentWakeReasons.Contains("new_day")&&!agentWakeReasons.Contains("danger"))return;
        if(!agentNeedsDecision) {
            // Wake from a deliberate wait or a timed gap; do not poll a busy queue with paid requests.
            bool playerQueued=AgentPlayerCovered();
            if(AgentDecisionPacing.CanDefer(playerQueued,NeedsAgentMenuDecision))return;
            WakeAgent("player_needs_next_plan");
        }
        // Sleep owns the native save lifecycle; only branch menus or its result need the model.
        if(playerExecutor.Busy && playerExecutor.Current?.skill=="player.sleep"&&!playerExecutor.NeedsMenuChoice)return;
        EnsureBudget();
        if(Data.Calls>=Math.Clamp(Settings.AutoplayMaxCallsPerDay,1,2000)){EnterSurvival("routine","daily_model_call_budget_reached");return;}
        if(agentStarting){Data.Autoplay.Record("resume_observation",AgentJson.Encode(AgentSnapshot()));agentStarting=false;}
        string file=Path.IsPathRooted(Settings.ApiKeyFile)?Settings.ApiKeyFile:Path.Combine(Helper.DirectoryPath,Settings.ApiKeyFile);
        object ui=playerExecutor.OwnsFishing?new{type="executor_owned_fishing",note="玩家钓鱼由底层控杆，无需menu工具；可以安排空闲伙伴，等待真实回执。"}:agentTools.Execute("menu.read",JsonSerializer.SerializeToElement(new{}));
        FrameStage("decision_ui",ref stage);
        var inventoryPlan=InventoryPlanning();FrameStage("decision_inventory",ref stage);
        var cleanup=FarmMaintenanceSummary();FrameStage("decision_cleanup",ref stage);
        var day=AgentDay(true);FrameStage("decision_day",ref stage);
        var production=ProductionSummary();FrameStage("decision_production",ref stage);
        var opportunities=OperatingOpportunities();
        WriteBusinessLog("operating_candidates",AgentJson.Encode(opportunities));
        var context=new{operating_candidates=opportunities,commitments=OperationCommitments(),run_id=Data.Autoplay.RunId,start_day=Data.Autoplay.StartDay,verified_actions=Data.Autoplay.VerifiedActions,verified_normal_sleeps=Data.Autoplay.SleepDays,goal=Data.Autoplay.Goal,plan=Data.Autoplay.Plan,now=AgentSnapshot(),inventory_plan=inventoryPlan,farm_cleanup=cleanup,inventory=AgentToolRegistry.Inventory(),day,progression=Data.Business.Enabled?(object)new{details="progress.read按需查询，经营不逐轮发送全成就"}:AgentProgression(),business=new{production,policy=Data.Business,routine=Data.Autoplay.Routine,investment=Data.FarmInvestment,planting_commitment=new{energy=PendingFarmEnergy(),recovery_nodes=Data.Autoplay.Schedule.Tasks.Where(t=>Data.FarmInvestment.Tasks.Contains(t.spec.id)&&t.state is "failed" or "blocked").Select(t=>new{t.spec.id,t.spec.tool,t.spec.args,t.error,t.spec.after})},pending_shipping_count=Game1.getFarm().getShippingBin(Game1.player).Count,note="farm.business_status查看产能与投资依据，算法已排任务不要重复提交"},schedule=AgentPlanRead(true),companions=AgentCompanions(true),ui,deliberation=new{queries_without_progress=decisionPacing.QueriesWithoutProgress,note="优先使用本轮事实安排高层工作，不重复轮询"},decision_reasons=agentWakeReasons.ToArray(),
            recent=RecentAgentContext(),active_actors=ActiveActors.ToArray(),goals=GoalContext(),memory=AgentMemoryContext(),stamp=SnapshotStamp()};
        FrameStage("decision_context",ref stage);
        // Freeze on the game thread; no game objects or deferred enumeration cross the boundary.
        var frozen=JsonSerializer.SerializeToElement(context,AgentJson.Options);FrameStage("decision_snapshot",ref stage);
        WriteBusinessLog("model_request",AgentJson.Encode(new{snapshot_characters=frozen.GetRawText().Length,tools_characters=AgentJson.Encode(AgentToolDiscovery.Core(AgentToolRegistry.Catalog)).Length,core_tool_count=AgentToolDiscovery.CoreNames.Length,reasons=agentWakeReasons.ToArray(),decisionPacing.QueriesWithoutProgress}));
        operatingRequestBasis=FailureKnowledge.Hash(AgentJson.Encode(OperatingDecisionBasis()));
        agentRequestQueueRevision=Data.Autoplay.Schedule.Revision;agentRequestEpoch=agentGeneration;agentRequestDay=Game1.Date.TotalDays;agentNeedsDecision=false;agentWakeReasons.Clear();
        Data.Calls++;RecordUsage();agentCancellation?.Dispose();agentCancellation=new();agentWatch.Restart();
        // Key-file IO, request encoding and budget ledger IO must not run on
        // the game thread before the first HTTP await. Only immutable values cross.
        var token=agentCancellation.Token;string model=Settings.Model;
        string? trace=Settings.RecordModelTrace?Path.Combine(Helper.DirectoryPath,"logs",Game1.uniqueIDForThisGame.ToString(),agentSaveEpoch,"model-"+Data.Autoplay.RunId+".jsonl"):null;
        agentPending=Task.Run(()=>{var packing=Stopwatch.StartNew();string serialized=ContextCompression.Pack(frozen,18000);agentLastContextCharacters=serialized.Length;agentLastPackMs=packing.Elapsed.TotalMilliseconds;return AutoplayModel.Ask(file,model,serialized,token,trace);},token);
        FrameStage("decision_dispatch",ref stage);
    }
    internal (GameLocation Location,Point Tile) AgentMapOrigin(string actorId) {
        if(actorId=="player")return (Game1.currentLocation,Game1.player.TilePoint);
        var actor=World().GetProperty("actors").EnumerateArray().FirstOrDefault(a=>a.GetProperty("id").GetString()==actorId);
        if(actor.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("actor_not_recruited");
        var location=Game1.getLocationFromName(actor.GetProperty("location").GetString()!)??throw new InvalidOperationException("actor_location_unavailable");
        var tile=actor.GetProperty("tile");return (location,new(tile[0].GetInt32(),tile[1].GetInt32()));
    }
    private object RecruitmentOptions()=>SinglePlayerMode?(object)new{enabled=false,note="当前只控制玩家；原生村民仅用于正常社交，不可招募派工。"}:new {
        selected=Selected,selected_is_not_proof_of_recruitment=true,
        outdoors=Game1.locations.Where(l=>l.IsOutdoors).SelectMany(l=>l.characters.Where(n=>Game1.characterData.ContainsKey(n.Name)&&!n.IsMonster&&!n.IsInvisible&&!n.isSleeping.Value)
            .Select(n=>new{npc=n.Name,location=l.NameOrUniqueName,x=n.TilePoint.X,y=n.TilePoint.Y}))
            .OrderBy(n=>n.location==Game1.currentLocation.NameOrUniqueName?0:1).Take(8),
        note="真实室外人物位置供邀请参考，不保证满足Squad好感/人数门槛。招募成功并出现在companions后才能派工；被锁门挡住就先做农务，按开放时间再访。"
    };
    private object[] AgentCompanions(bool compact=false) {
        if(SinglePlayerMode)return Array.Empty<object>();
        var world=World();
        if(!world.TryGetProperty("actors",out var actors))return Array.Empty<object>();
        return actors.EnumerateArray().Select(a=>{
            var info=new Dictionary<string,object>();
            foreach(string key in new[]{"id","name","location","tile","task","moving","reachable_locations","travel_options","resource_sites","control_mode","cargo","cargo_slots","cargo_capacity","storable_cargo","cargo_storage","in_combat","relationship","labor"})
                if((!compact||key is not ("reachable_locations" or "travel_options" or "resource_sites" or "cargo_storage"))&&a.TryGetProperty(key,out var value))info[key]=value.Clone();
            string id=a.GetProperty("id").GetString()!;
            info["work_run_goals"]=WorkCapabilities.CompanionGoals;
            info["work_run_fishing_supported"]=false;
            info["queue"]=Data.Autoplay.Schedule.Tasks.Where(t=>t.spec.actor==id&&!t.Terminal).Select(t=>new{t.spec.id,t.state,skill=AgentToolRegistry.Text(t.spec.args,"skill"),t.spec.purpose}).ToArray();
            if(!compact&&a.TryGetProperty("candidates",out var candidates))info["candidates"]=candidates.EnumerateArray().Select(c=>{
                var tile=c.GetProperty("tile");string location=a.GetProperty("location").GetString()!;
                bool claimed=playerExecutor.ClaimsTile(location,tile[0].GetInt32(),tile[1].GetInt32()) || AgentTileBusy(location,tile[0].GetInt32(),tile[1].GetInt32());
                return new{target=c.Clone(),claimed};
            }).Where(c=>!c.claimed).Select(c=>c.target).Take(64).ToArray();
            info["planning_note"]="独立角色：有候选可直接派工；常规劳动用work.run指定location，底层自动移动并连续劳动；低层动作才需逐目标派工。follow/guard是跟随或护卫，不能作为完成生产的证据；无合适工作时说明休息或陪伴的理由。";
            return (object)info;
        }).ToArray();
    }
    internal object AgentWorld(){RefreshFacts(true);return new{snapshot=AgentSnapshot(),inventory_plan=InventoryPlanning(),farm_cleanup=FarmMaintenanceSummary(),farm=new{Facts.Day,Facts.Time,Facts.Season,Facts.Route,Facts.Money,Facts.DryCrops,Facts.RipeCrops,Facts.DeadCrops,Facts.MachinesReady,Facts.AnimalsUnpetted,Facts.FeedNeeded,Facts.HayInSilo,animals=Facts.Animals,care_locations=Facts.CareLocations,machines=Facts.Machines.Take(12),crops=Facts.Crops.Take(16),stock=Facts.Stock.Take(30),quests=Facts.Quests.Take(8),bundles=Facts.Bundles.Where(b=>!b.Complete).Take(5)},companions=AgentCompanions(),recruitment=RecruitmentOptions(),goals=GoalContext()};}
    internal object AgentCompanion(JsonElement args) {
        if(SinglePlayerMode)throw new InvalidOperationException("stage_a_native_player_only_pending_stage_b");
        if(api==null)throw new InvalidOperationException("companion_api_unavailable");
        RefreshFacts(true);
        string? contract=AgentCallContract.CompanionError(args);
        if(contract!=null)return new{status="failed",error=contract,hint="跨地图先 companion.assign(skill=travel,destination=地图名)，成功后读取 world.read 的真实候选，再用 skill=mine/forage/... + target_id 派工。destination 不是劳动目标；不要编造 target_id。"};
        var values=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(args.GetRawText())!;
        values["command_id"]=JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("N"));
        (string Location,int X,int Y)? claim=null;
        string actorId=AgentToolRegistry.Text(args,"actor_id"),targetId=AgentToolRegistry.Text(args,"target_id");
        if(targetId.Length>0) {
            foreach(var actor in World().GetProperty("actors").EnumerateArray().Where(a=>a.GetProperty("id").GetString()==actorId))
                foreach(var candidate in actor.GetProperty("candidates").EnumerateArray().Where(c=>c.GetProperty("target_id").GetString()==targetId)) {
                    var tile=candidate.GetProperty("tile");claim=(actor.GetProperty("location").GetString()!,tile[0].GetInt32(),tile[1].GetInt32());
                    if(playerExecutor.ClaimsTile(claim.Value.Location,claim.Value.X,claim.Value.Y) || AgentTileBusy(claim.Value.Location,claim.Value.X,claim.Value.Y))throw new InvalidOperationException("target_claimed_by_other_actor");
                }
        }
        var result=JsonDocument.Parse(api.StartAction(JsonSerializer.Serialize(values))).RootElement.Clone();
        if(claim.HasValue && result.GetProperty("status").GetString()=="running")agentClaims[result.GetProperty("command_id").GetString()!]=claim.Value;
        return result;
    }
    internal bool AgentTileBusy(string location,int x,int y) {
        foreach(var pair in agentClaims.ToArray()) {
            using var receipt=JsonDocument.Parse(api!.PollAction(pair.Key));
            if(receipt.RootElement.GetProperty("status").GetString()!="running"){agentClaims.Remove(pair.Key);continue;}
            if(pair.Value==(location,x,y))return true;
        }
        return false;
    }
    internal object AgentReceipt(string id,bool cancel)=>ExecutionContract.Receipt(RawAgentReceipt(id,cancel),agentSaveEpoch,Data.Autoplay.Schedule.Revision,id.StartsWith("player:")?"player":"");
    private object RawAgentReceipt(string id,bool cancel) {
        var task=Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==id);
        if(task!=null) {
            if(cancel)return AgentPlanCancel(JsonSerializer.SerializeToElement(new{ids=new[]{id}}));
            if(task.state!="running")return new{task_id=id,status=task.state,task.command_id,task.error,task.receipt};
            if(task.command_id==null)return new{task_id=id,status="failed",error="running_task_missing_receipt_replan"};
            id=task.command_id;
        }
        if(id.StartsWith("work:"))return SemanticReceipt(id,cancel);
        return id.StartsWith("player:")?(cancel?playerExecutor.Cancel(id):playerExecutor.Poll(id)):
            JsonDocument.Parse(cancel?api!.CancelAction(id):api!.PollAction(id)).RootElement.Clone();
    }
    internal int AgentWait(int seconds) {
        if(Game1.activeClickableMenu is StardewValley.Menus.DialogueBox)throw new InvalidOperationException("dialogue_requires_menu_input_before_waiting");
        if(Game1.eventUp)seconds=Math.Min(seconds,2);
        if(Game1.timeOfDay>=2200)throw new InvalidOperationException("late_night_plan_return_home_before_waiting");
        double until22=Math.Max(1,(22*60-Minute)*.7/AutoplaySpeed.Clock(Settings.AutoplayClockRate));
        int actual=Math.Clamp(seconds,1,Math.Min(60,Math.Max(1,(int)until22)));
        agentRequestedWait=DateTime.UtcNow.AddSeconds(actual);return actual;
    }
}
