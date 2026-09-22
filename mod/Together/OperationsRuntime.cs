using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private string operationWindowStamp="";
    private string launchingIntent="";
    private object[] OperationCommitments()=>Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).GroupBy(t=>t.spec.intent_id).Select(g=>(object)new{intent_id=g.Key,cash=IntentCash(g),tasks=g.Select(t=>t.spec.id).ToArray()}).ToArray();
    private int IntentCash(IEnumerable<ScheduledAgentTask> tasks)=>(int)Math.Min(int.MaxValue,tasks.Where(t=>!t.Terminal).Sum(t=>(long)Math.Max(0,NativeCosts.PurchaseCap(t.spec.tool,t.spec.args)-NativeCommandSpent(t.command_id))));
    private int OtherCommittedCash(string intent) {
        var mine=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal&&t.spec.intent_id==intent).ToArray();
        bool development=Data.Business.Activity==Data.Business.PendingAsset&&mine.Any(t=>Data.Business.Tasks.Contains(t.spec.id));
        return (int)Math.Min(int.MaxValue,Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal&&t.spec.intent_id!=intent).GroupBy(t=>t.spec.intent_id).Sum(g=>(long)IntentCash(g))+Data.Business.UnverifiedCash.Values.Sum(v=>(long)v)+(development?0:UnscheduledDevelopmentCash()));
    }
    private bool OperationActorOccupied(string actor)=>WorkActorBusy(actor)||(actor=="player"&&playerExecutor.Busy)||Data.Autoplay.Schedule.Covers(actor,Game1.Date.TotalDays,Game1.timeOfDay);
    private bool FarmerHarvestCreditNeeded()=>Game1.player.questLog.OfType<StardewValley.Quests.ItemHarvestQuest>().Any(q=>!q.completed.Value);

    private bool ServiceAvailableNow(string subject){var w=ServiceWindow(subject);return w.Reason=="available"&&Game1.timeOfDay>=w.Open&&Game1.timeOfDay<w.Close;}
    private string ServiceSubject(string tool,JsonElement args) {
        if(tool=="player.social")return Game1.getCharacterFromName(AgentToolRegistry.Text(args,"npc"))?.currentLocation?.NameOrUniqueName??"";
        if(tool is not ("player.service" or "player.procure" or "player.travel" or "player.acquire_animal" or "player.upgrade_house"))return "";
        return AgentToolRegistry.Text(args,"location",tool=="player.acquire_animal"?"AnimalShop":"");
    }
    internal object ServiceHours(string location) {
        var w=ServiceWindow(location);bool known=Game1.locations.Any(l=>l.doors.Pairs.Any(p=>{var a=l.GetTilePropertySplitBySpaces("Action","Buildings",p.Key.X,p.Key.Y);return a.Length>=6&&a[0]=="LockedDoorWarp"&&a[3]==location;}));
        return new{location,entry_hours_known=known,opens=known&&w.Reason=="available"?(int?)w.Open:null,closes=known?(int?)w.Close:null,now=Game1.timeOfDay,can_enter_now=known?(bool?)(w.Reason=="available"&&Game1.timeOfDay>=w.Open&&Game1.timeOfDay<w.Close):null,reason=w.Reason,closed_today=w.Reason!="available",recheck_at=w.Reason=="available"&&Game1.timeOfDay<w.Open?(int?)w.Open:null,note="原生地图门禁与今日关闭条件；能进门不保证店员在柜台，实际报价与服务仍在现场核验"};
    }
    internal object[] KnownServiceHours()=>new[]{"SeedShop","FishShop","Blacksmith","ScienceHouse","AnimalShop","Saloon","JojaMart","Hospital","AdventureGuild"}.Where(n=>Game1.getLocationFromName(n)!=null).Select(ServiceHours).ToArray();
    private string? RouteDoorAccess(GameLocation location,Warp edge) {
        if(edge.X<0||edge.Y<0||edge.X>=location.Map.Layers[0].LayerWidth||edge.Y>=location.Map.Layers[0].LayerHeight)return null;
        var action=location.GetTilePropertySplitBySpaces("Action","Buildings",edge.X,edge.Y);
        if(action.Length<6||action[0]!="LockedDoorWarp")return null;
        var window=ServiceWindow(edge.TargetName);
        if(window.Reason!="available")return "door_"+window.Reason;
        if(Game1.timeOfDay<window.Open||Game1.timeOfDay>=window.Close)return "door_closed:"+window.Open+"-"+window.Close;
        if(action.Length>=8&&int.TryParse(action[7],out int required)&&(Game1.player.friendshipData.GetValueOrDefault(action[6])?.Points??0)<required)return "door_friendship_required:"+action[6];
        return null; // Native performAction remains authoritative for event-specific conditions.
    }
    private (int Open,int Close,string Reason,string Conditions) ServiceWindow(string subject) {
        int open=600,close=2600;string reason="available";
        var doors=Game1.locations.SelectMany(l=>l.doors.Pairs.Select(p=>(Location:l,Action:l.GetTilePropertySplitBySpaces("Action","Buildings",p.Key.X,p.Key.Y))))
            .Where(d=>d.Action.Length>=6&&d.Action[0]=="LockedDoorWarp"&&d.Action[3]==subject).ToArray();
        if(doors.Length>0) {
            var d=doors[0];int.TryParse(d.Action[4],out open);int.TryParse(d.Action[5],out close);
            bool key=Game1.player.HasTownKey&&d.Location.InValleyContext();
            if(subject=="FishShop"&&Game1.player.mailReceived.Contains("willyHours"))open=800;
            if(key){open=600;close=2600;}
            if(d.Location.IsGreenRainingHere()&&Game1.year==1&&d.Location is not (StardewValley.Locations.Beach or StardewValley.Locations.Forest)&&subject!="AdventureGuild"){open=600;close=2600;}
            if(GameLocation.AreStoresClosedForFestival()&&d.Location.InValleyContext())reason="festival_closed";
            else if(subject=="SeedShop"&&Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)=="Wed"&&!Utility.HasAnyPlayerSeenEvent("191393")&&!key)reason="weekly_closed";

        }
        string owner=subject switch{"SeedShop"=>"Pierre","FishShop"=>"Willy","AnimalShop"=>"Marnie","Blacksmith"=>"Clint","ScienceHouse"=>"Robin",_=>""};
        var npc=owner.Length>0?Game1.getCharacterFromName(owner):null;
        string conditions=FailureKnowledge.Hash(AgentJson.Encode(new{day=Game1.Date.TotalDays,subject,open,close,reason,owner_location=npc?.currentLocation?.NameOrUniqueName,owner_tile=npc==null?null:new[]{npc.TilePoint.X,npc.TilePoint.Y},island=owner.Length>0&&Game1.IsVisitingIslandToday(owner)}));
        return (open,close,reason,conditions);
    }
    private void PrepareOperation(AgentTaskSpec spec) {
        if(spec.tool=="work.run") {
            string goal=AgentToolRegistry.Text(spec.args,"goal"),normalized=ResourceRules.NormalizeGoal(goal,AgentToolRegistry.Text(spec.args,"item"));
            if(normalized!=goal){var copy=spec.args.Deserialize<Dictionary<string,JsonElement>>()!;copy["goal"]=JsonSerializer.SerializeToElement(normalized);spec.args=JsonSerializer.SerializeToElement(copy);}
        }
        ValidateToolDeclaration(spec.tool,spec.args);
        CheckKnownFailure(new ScheduledAgentTask{spec=spec});
        if(SinglePlayerMode&&spec.actor!="player")throw new InvalidOperationException("stage_a_native_player_only_pending_stage_b");
        if(spec.tool=="work.run"&&WorkCapabilities.Validate(spec.actor,AgentToolRegistry.Text(spec.args,"goal")) is {} unavailable)throw new InvalidOperationException(unavailable);
        if(spec.id.StartsWith("routine-")){spec.source="daily_care";spec.priority=80;}
        else if(spec.id.StartsWith("cleanup-")){spec.source="farm_cleanup";spec.priority=20;}
        else if(spec.id.StartsWith("farm-")||spec.id.StartsWith("invest-")){spec.source="investment";spec.priority=60;}
        else if(spec.id.StartsWith("business-")){spec.source="production";spec.priority=65;}
        if(spec.source=="goal")spec.priority=70;
        if(spec.tool=="player.sleep"){spec.priority=90;spec.sleep_review_day=Game1.Date.TotalDays;spec.sleep_review_time=Game1.timeOfDay;spec.sleep_review_progress=Data.Autoplay.VerifiedActions;spec.sleep_review_basis=SleepDecisionBasis();}
        if(spec.actor!="player"&&spec.tool=="work.run"&&AgentToolRegistry.Text(spec.args,"goal")=="harvest"&&FarmerHarvestCreditNeeded()) {
            var args=spec.args.Deserialize<Dictionary<string,JsonElement>>()!;args["actor_id"]=JsonSerializer.SerializeToElement("player");spec.args=JsonSerializer.SerializeToElement(args);spec.actor="player";
            spec.purpose="由Farmer执行原生收获，核验任务归属；"+spec.purpose;
        }
        string subject=ServiceSubject(spec.tool,spec.args);
        if(subject.Length>0)spec.not_before=Math.Max(spec.not_before,Math.Min(2600,ServiceWindow(subject).Open));
        Data.Autoplay.Record("intent_proposed",AgentJson.Encode(new{spec.id,spec.intent_id,spec.source,spec.actor,spec.tool,spec.purpose,spec.after,spec.not_before,spec.deadline,spec.priority}));
    }
    private void PrepareServiceWindows() {
        Data.Autoplay.Schedule.Prepare=PrepareOperation;Data.Autoplay.Schedule.Finished=CaptureScheduledOutcome;
        string stamp=$"{agentSaveEpoch}:{Game1.Date.TotalDays}:{Game1.timeOfDay}:{Data.Autoplay.Schedule.Revision}";
        if(operationWindowStamp==stamp)return;operationWindowStamp=stamp;
        foreach(var task in Data.Autoplay.Schedule.Tasks.Where(t=>t.state=="queued"&&t.spec.day==Game1.Date.TotalDays).ToArray()) {
            string subject=ServiceSubject(task.spec.tool,task.spec.args);if(subject.Length==0)continue;
            var window=ServiceWindow(subject);
            var blocked=Data.Autoplay.Operations.Blocking(subject,window.Conditions,Game1.Date.TotalDays,Game1.timeOfDay);
            string? reason=window.Reason!="available"?window.Reason:window.Open>Game1.timeOfDay?"before_open":Game1.timeOfDay>=window.Close?"after_close":blocked?.Reason;
            if(reason!=null) {
                int next=reason=="before_open"?window.Open:2600;
                if(!OperationsPolicy.ServiceWindowFitsToday(Game1.timeOfDay,next,window.Close,task.spec.deadline)) {
                    // No native action ran. End this attempt, retaining its intent
                    // for fresh planning, rather than inventing a 26:00 opening.
                    var receipt=new{status="blocked",stop_reason="service_window_unavailable_today",reason,subject,opens=window.Open,closes=window.Close,task.spec.deadline,executed=false,completed=0,note="本次未执行；今天已无有效服务窗口。请改派独立工作或另日按真实条件重新安排。"};
                    LearnServiceConstraint(task,"blocked","service_window_unavailable_today");
                    Data.Autoplay.Schedule.Finish(task,"blocked","service_window_unavailable_today",AgentJson.Encode(receipt));
                    Data.Autoplay.Record("service_window_blocked",AgentJson.Encode(new{task.spec.id,task.spec.intent_id,receipt}));WakeAgent("service_window_unavailable_today");continue;
                }
                if(task.wait_reason!=reason)Data.Autoplay.Record("intent_deferred",AgentJson.Encode(new{task.spec.id,task.spec.intent_id,subject,reason,next,condition=window.Conditions}));
                task.wait_reason=reason;task.spec.not_before=Math.Max(task.spec.not_before,next);
            }else if(task.wait_reason!=null) {
                Data.Autoplay.Record("constraint_released",AgentJson.Encode(new{task.spec.id,subject,previous=task.wait_reason,condition=window.Conditions}));
                task.wait_reason=null;task.spec.not_before=Math.Min(task.spec.not_before,Game1.timeOfDay);
            }
        }
    }
    private bool AdmitOperation(ScheduledAgentTask task) {
        launchingIntent=task.spec.intent_id;
        string subject=ServiceSubject(task.spec.tool,task.spec.args);
        if(subject.Length>0) {
            var w=ServiceWindow(subject);var blocked=Data.Autoplay.Operations.Blocking(subject,w.Conditions,Game1.Date.TotalDays,Game1.timeOfDay);
            if(w.Reason!="available"||Game1.timeOfDay<w.Open||Game1.timeOfDay>=w.Close||blocked!=null){task.wait_reason=blocked?.Reason??"service_window";task.spec.not_before=Math.Max(Game1.timeOfDay,w.Open);return false;}
        }
        Data.Autoplay.Record("lease_acquired",AgentJson.Encode(new{task.spec.id,task.spec.intent_id,task.spec.actor,task.spec.purpose,location=Game1.currentLocation.NameOrUniqueName,tile=new[]{Game1.player.TilePoint.X,Game1.player.TilePoint.Y},Game1.player.Stamina,free_slots=CapacityAdapter.Of(Game1.player).FreeSlots}));
        return true;
    }
    private void ValidateNativeOperation(string tool,JsonElement args) {
        ValidateToolDeclaration(tool,args);
        if(!AutoplayRunning)return;
        GuardCapacity("player",tool,args);
        if(NativeCosts.PurchaseCap(tool,args)>0) {
            string intent=Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.state=="running"&&t.spec.actor=="player")?.spec.intent_id??launchingIntent;
            int need=NativeCosts.PurchaseCap(tool,args);
            if(need>Math.Max(0,Game1.player.Money-OtherCommittedCash(intent)-AgentToolRegistry.Number(args,"keep_gold",0)))throw new InvalidOperationException("cash_committed_to_other_approved_work");
        }
        string subject=ServiceSubject(tool,args);if(subject.Length==0)return;
        var w=ServiceWindow(subject);
        if(w.Reason!="available"||Game1.timeOfDay<w.Open||Game1.timeOfDay>=w.Close)throw new InvalidOperationException("shop_closed");
        if(Data.Autoplay.Operations.Blocking(subject,w.Conditions,Game1.Date.TotalDays,Game1.timeOfDay)!=null)throw new InvalidOperationException("service_conditions_unchanged");
    }
    private void LearnServiceConstraint(ScheduledAgentTask task,string state,string? error) {
        string subject=ServiceSubject(task.spec.tool,task.spec.args);if(subject.Length==0)return;
        if(state=="succeeded"){Data.Autoplay.Operations.Constraints.RemoveAll(c=>c.Subject==subject);return;}
        if(error is not ("native_service_did_not_open_or_wrong_shop" or "native_service_unavailable_check_hours_and_owner" or "service_branch_unavailable_read_menu" or "shop_closed" or "service_conditions_unchanged" or "service_window_unavailable_today"))return;
        var window=ServiceWindow(subject);
        var fact=new ServiceConstraint{Subject=subject,Reason=error=="service_window_unavailable_today"?window.Reason:error,Condition=window.Conditions,Day=Game1.Date.TotalDays,RetryTime=Game1.timeOfDay<window.Open?window.Open:2600,Evidence=task.spec.id};
        Data.Autoplay.Operations.Observe(fact);Data.Autoplay.Record("constraint_created",AgentJson.Encode(fact));
    }
}
