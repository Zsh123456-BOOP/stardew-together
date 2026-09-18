using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private string operationWindowStamp="";
    private string launchingIntent="";
    private object[] OperationCommitments()=>Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).GroupBy(t=>t.spec.intent_id).Select(g=>(object)new{intent_id=g.Key,cash=IntentCash(g),tasks=g.Select(t=>t.spec.id).ToArray()}).ToArray();
    private int IntentCash(IEnumerable<ScheduledAgentTask> tasks) {
        var rows=tasks.ToArray();long sum=0;
        foreach(var t in rows.Where(t=>t.state=="queued"&&t.spec.tool is "player.buy" or "player.procure")) {
            int budget=AgentToolRegistry.Number(t.spec.args,"budget",0),unit=AgentToolRegistry.Number(t.spec.args,"max_unit_price",budget),count=AgentToolRegistry.Number(t.spec.args,"count",1);
            sum+=Math.Min((long)budget,(long)Math.Max(0,unit)*Math.Max(0,count));
        }
        // Building/animal native chains already declare one aggregate reservation.
        if(rows.Any(t=>Data.Business.Tasks.Contains(t.spec.id)))sum=Math.Max(sum,Data.Business.ActiveReservation);
        return (int)Math.Clamp(sum,0,int.MaxValue);
    }
    private int OtherCommittedCash(string intent)=>Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal&&t.spec.intent_id!=intent).GroupBy(t=>t.spec.intent_id).Sum(g=>IntentCash(g));
    private bool OperationActorOccupied(string actor)=>WorkActorBusy(actor)||(actor=="player"&&playerExecutor.Busy)||Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.actor==actor&&(t.state is "running" or "needs_review"||t.state=="queued"&&t.spec.day==Game1.Date.TotalDays&&t.spec.not_before<=Game1.timeOfDay&&t.wait_reason==null&&t.spec.after.All(id=>Data.Autoplay.Schedule.Tasks.Any(d=>d.spec.id==id&&d.state=="succeeded"))));
    private bool FarmerHarvestCreditNeeded()=>Game1.player.questLog.OfType<StardewValley.Quests.ItemHarvestQuest>().Any(q=>!q.completed.Value);

    private string ServiceSubject(string tool,JsonElement args) {
        if(tool=="player.social")return Game1.getCharacterFromName(AgentToolRegistry.Text(args,"npc"))?.currentLocation?.NameOrUniqueName??"";
        if(tool is not ("player.service" or "player.procure" or "player.travel" or "player.acquire_animal" or "player.upgrade_house"))return "";
        return AgentToolRegistry.Text(args,"location",tool=="player.acquire_animal"?"AnimalShop":"");
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
            if(reason!="available")open=2600;
        }
        string owner=subject switch{"SeedShop"=>"Pierre","FishShop"=>"Willy","AnimalShop"=>"Marnie","Blacksmith"=>"Clint","ScienceHouse"=>"Robin",_=>""};
        var npc=owner.Length>0?Game1.getCharacterFromName(owner):null;
        string conditions=FailureKnowledge.Hash(AgentJson.Encode(new{day=Game1.Date.TotalDays,subject,open,close,reason,owner_location=npc?.currentLocation?.NameOrUniqueName,owner_tile=npc==null?null:new[]{npc.TilePoint.X,npc.TilePoint.Y},island=owner.Length>0&&Game1.IsVisitingIslandToday(owner)}));
        return (open,close,reason,conditions);
    }
    private void PrepareOperation(AgentTaskSpec spec) {
        ValidateToolDeclaration(spec.tool,spec.args);
        CheckKnownFailure(new ScheduledAgentTask{spec=spec});
        if(SinglePlayerMode&&spec.actor!="player")throw new InvalidOperationException("stage_a_native_player_only_pending_stage_b");
        if(spec.tool=="work.run"&&WorkCapabilities.Validate(spec.actor,AgentToolRegistry.Text(spec.args,"goal")) is {} unavailable)throw new InvalidOperationException(unavailable);
        if(spec.id.StartsWith("routine-")){spec.source="daily_care";spec.priority=80;}
        else if(spec.id.StartsWith("cleanup-")){spec.source="farm_cleanup";spec.priority=20;}
        else if(spec.id.StartsWith("farm-")||spec.id.StartsWith("invest-")){spec.source="investment";spec.priority=60;}
        else if(spec.id.StartsWith("business-")){spec.source="production";spec.priority=65;}
        if(spec.source=="goal")spec.priority=70;
        if(spec.tool=="player.sleep"){spec.priority=90;spec.sleep_review_day=Game1.Date.TotalDays;spec.sleep_review_time=Game1.timeOfDay;spec.sleep_review_progress=Data.Autoplay.VerifiedActions;}
        if(spec.actor!="player"&&spec.tool=="work.run"&&AgentToolRegistry.Text(spec.args,"goal")=="harvest"&&FarmerHarvestCreditNeeded()) {
            var args=spec.args.Deserialize<Dictionary<string,JsonElement>>()!;args["actor_id"]=JsonSerializer.SerializeToElement("player");spec.args=JsonSerializer.SerializeToElement(args);spec.actor="player";
            spec.purpose="由Farmer执行原生收获，核验任务归属；"+spec.purpose;
        }
        string subject=ServiceSubject(spec.tool,spec.args);
        if(subject.Length>0)spec.not_before=Math.Max(spec.not_before,Math.Min(2600,ServiceWindow(subject).Open));
        Data.Autoplay.Record("intent_proposed",AgentJson.Encode(new{spec.id,spec.intent_id,spec.source,spec.actor,spec.tool,spec.purpose,spec.after,spec.not_before,spec.deadline,spec.priority}));
    }
    private void PrepareServiceWindows() {
        Data.Autoplay.Schedule.Prepare=PrepareOperation;
        string stamp=$"{agentSaveEpoch}:{Game1.Date.TotalDays}:{Game1.timeOfDay}:{Data.Autoplay.Schedule.Revision}";
        if(operationWindowStamp==stamp)return;operationWindowStamp=stamp;
        foreach(var task in Data.Autoplay.Schedule.Tasks.Where(t=>t.state=="queued"&&t.spec.day==Game1.Date.TotalDays)) {
            string subject=ServiceSubject(task.spec.tool,task.spec.args);if(subject.Length==0)continue;
            var window=ServiceWindow(subject);
            var blocked=Data.Autoplay.Operations.Blocking(subject,window.Conditions,Game1.Date.TotalDays,Game1.timeOfDay);
            string? reason=window.Open>Game1.timeOfDay?window.Reason=="available"?"before_open":window.Reason:Game1.timeOfDay>=window.Close?"after_close":blocked?.Reason;
            if(reason!=null) {
                int next=reason=="before_open"?window.Open:2600;
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
            if(Game1.timeOfDay<w.Open||Game1.timeOfDay>=w.Close||blocked!=null){task.wait_reason=blocked?.Reason??"service_window";task.spec.not_before=Math.Max(Game1.timeOfDay,w.Open);return false;}
        }
        Data.Autoplay.Record("lease_acquired",AgentJson.Encode(new{task.spec.id,task.spec.intent_id,task.spec.actor,task.spec.purpose,location=Game1.currentLocation.NameOrUniqueName,tile=new[]{Game1.player.TilePoint.X,Game1.player.TilePoint.Y},Game1.player.Stamina,free_slots=CapacityAdapter.Of(Game1.player).FreeSlots}));
        return true;
    }
    private void ValidateNativeOperation(string tool,JsonElement args) {
        ValidateToolDeclaration(tool,args);
        if(!AutoplayRunning)return;
        GuardCapacity("player",tool,args);
        if(tool is "player.buy" or "player.procure") {
            string intent=Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.state=="running"&&t.spec.actor=="player")?.spec.intent_id??launchingIntent;
            int budget=AgentToolRegistry.Number(args,"budget",0),unit=AgentToolRegistry.Number(args,"max_unit_price",budget),count=AgentToolRegistry.Number(args,"count",1);
            long need=Math.Min((long)budget,(long)Math.Max(0,unit)*Math.Max(0,count));
            if(need>Math.Max(0,Game1.player.Money-OtherCommittedCash(intent)-AgentToolRegistry.Number(args,"keep_gold",0)))throw new InvalidOperationException("cash_committed_to_other_approved_work");
        }
        string subject=ServiceSubject(tool,args);if(subject.Length==0)return;
        var w=ServiceWindow(subject);
        if(Game1.timeOfDay<w.Open||Game1.timeOfDay>=w.Close)throw new InvalidOperationException("shop_closed");
        if(Data.Autoplay.Operations.Blocking(subject,w.Conditions,Game1.Date.TotalDays,Game1.timeOfDay)!=null)throw new InvalidOperationException("service_conditions_unchanged");
    }
    private void LearnServiceConstraint(ScheduledAgentTask task,string state,string? error) {
        string subject=ServiceSubject(task.spec.tool,task.spec.args);if(subject.Length==0)return;
        if(state=="succeeded"){Data.Autoplay.Operations.Constraints.RemoveAll(c=>c.Subject==subject);return;}
        if(error is not ("native_service_did_not_open_or_wrong_shop" or "native_service_unavailable_check_hours_and_owner" or "service_branch_unavailable_read_menu" or "shop_closed" or "service_conditions_unchanged"))return;
        var window=ServiceWindow(subject);
        var fact=new ServiceConstraint{Subject=subject,Reason=error,Condition=window.Conditions,Day=Game1.Date.TotalDays,RetryTime=Game1.timeOfDay<window.Open?window.Open:2600,Evidence=task.spec.id};
        Data.Autoplay.Operations.Observe(fact);Data.Autoplay.Record("constraint_created",AgentJson.Encode(fact));
    }
}
