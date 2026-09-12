using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private int qualityDay=-1,qualityMinute=-1,qualityCompleted;
    private Microsoft.Xna.Framework.Vector2 qualityPosition;
    private bool qualityProgress;
    private string qualityLocation="",qualityRun="";
    private long qualityLastTick;
    private readonly Dictionary<string,double> qualityInterval=new();
    private readonly Dictionary<string,double> transportInterval=new();
    private string QualityCategory(bool moved,bool completed) {
        if(moved)return "transport";
        if(Game1.player.UsingTool||completed)return "labor";
        if(agentPending!=null&&!playerExecutor.Busy)return "waiting_model";
        if(!playerExecutor.Busy&&Data.Autoplay.Schedule.Tasks.Any(t=>t.state=="queued"&&t.wait_reason is "crop_not_mature" or "machine_not_ready"))return "waiting_natural_growth";
        return "blocked";
    }
    private void CloseQualityInterval(QualityDay row,int minute) {
        int delta=minute-qualityMinute;if(delta<=0)return;
        double total=qualityInterval.Values.Sum();
        if(total>0)foreach(var entry in qualityInterval)row.TimeBreakdownMinutes[entry.Key]=row.TimeBreakdownMinutes.GetValueOrDefault(entry.Key)+delta*entry.Value/total;
        else row.TimeBreakdownMinutes["blocked"]=row.TimeBreakdownMinutes.GetValueOrDefault("blocked")+delta;
        if(total>0)foreach(var entry in transportInterval)row.TransportMinutes[entry.Key]=row.TransportMinutes.GetValueOrDefault(entry.Key)+delta*entry.Value/total;
        qualityInterval.Clear();transportInterval.Clear();
    }
    private void CaptureQualitySleep(int day) {
        Data.Autoplay.NativeSleepRequestedDay=day;if(!AutoplayRunning)return;
        TickSurvivalQuality();var row=Data.Autoplay.Quality.Current(day);
        row.SleepTime=Game1.timeOfDay;row.ActualAwakeMinutes=Math.Max(0,DailyBudget.Minutes(Game1.timeOfDay)-360);
        foreach(string state in new[]{"labor","transport","waiting_model","waiting_natural_growth","blocked"})row.TimeBreakdownMinutes.TryAdd(state,0);
        double missing=row.ActualAwakeMinutes-row.TimeBreakdownMinutes.Values.Sum();if(missing>0)row.TimeBreakdownMinutes["blocked"]+=missing;
        RefreshFacts(true);Data.Autoplay.Record("quality_bedtime",AgentJson.Encode(new{day,row.SleepTime,row.ActualAwakeMinutes,row.TimeBreakdownMinutes,row.WallSecondsByState,row.WallDetailSeconds,row.TransportMinutes,row.TransportWallSeconds,ledger=ReadBusinessLedger(),performance=Performance()}));
    }
    private long NativeAssetValue() {
        long Price(Item? item)=>item is StardewValley.Object o?(long)Math.Max(0,o.sellToStorePrice())*o.Stack:0;
        // Consistent liquidation floor: no projected harvest/revenue, no double
        // count of machine inputs, and no invented sale value for unsellable assets.
        return Game1.player.Items.Sum(Price)+SharedStorage().Sum(s=>s.Chest.GetItemsForPlayer().Sum(Price))+
            Game1.getFarm().getShippingBin(Game1.player).Sum(Price)+GoalMachines().Sum(m=>Price(m.Object)+(m.Object.readyForHarvest.Value?Price(m.Object.heldObject.Value):0));
    }
    private void TickSurvivalQuality() {
        if(!AutoplayRunning)return;
        int day=Game1.Date.TotalDays,minute=DailyBudget.Minutes(Game1.timeOfDay);
        var q=Data.Autoplay.Quality;
        if(qualityDay!=day||qualityRun!=Data.Autoplay.RunId) {
            qualityDay=day;qualityRun=Data.Autoplay.RunId;qualityMinute=minute;qualityProgress=false;qualityLastTick=0;qualityInterval.Clear();transportInterval.Clear();ResetDayPerformance();
            RefreshFacts(true);Data.Autoplay.Record("quality_day_start",AgentJson.Encode(new{day,time=Game1.timeOfDay,ledger=ReadBusinessLedger()}));
            q.Current(day,Game1.player.Money,NativeAssetValue());
        }
        var action=playerExecutor.Current;
        if(action is {status:"running"}&&action.skill!="player.sleep"&&(Game1.player.Position!=qualityPosition||Game1.currentLocation.NameOrUniqueName!=qualityLocation||action.completed!=qualityCompleted))qualityProgress=true;
        long now=System.Diagnostics.Stopwatch.GetTimestamp();double seconds=qualityLastTick==0?0:(now-qualityLastTick)/(double)System.Diagnostics.Stopwatch.Frequency;qualityLastTick=now;
        bool moved=Game1.player.Position!=qualityPosition||Game1.currentLocation.NameOrUniqueName!=qualityLocation;
        string accountingState=QualityCategory(moved,action!=null&&action.completed!=qualityCompleted);
        var row=q.Current(day);
        if(!row.SleepTime.HasValue&&seconds>0){qualityInterval[accountingState]=qualityInterval.GetValueOrDefault(accountingState)+seconds;row.WallSecondsByState[accountingState]=row.WallSecondsByState.GetValueOrDefault(accountingState)+seconds;}
        if(!row.SleepTime.HasValue&&seconds>0){string detail=Game1.player.UsingTool?"normal_tool_animation":moved?"moving":Game1.eventUp?"native_event":Game1.activeClickableMenu!=null?"native_menu":agentPending!=null&&!playerExecutor.Busy?"model_wait":playerExecutor.Busy?"action_pending_no_motion":"idle_no_action";row.WallDetailSeconds[detail]=row.WallDetailSeconds.GetValueOrDefault(detail)+seconds;}
        if(!row.SleepTime.HasValue&&seconds>0&&accountingState=="transport") {
            var work=semanticJobs.Values.FirstOrDefault(j=>j.actor=="player"&&j.status=="running");
            string reason=work?.Storing==true||preparation!=null?"storage_roundtrip":action?.skill=="player.travel"||Game1.currentLocation.NameOrUniqueName!=qualityLocation?"cross_map":Game1.currentLocation.IsFarm?"within_farm":"other_local";
            transportInterval[reason]=transportInterval.GetValueOrDefault(reason)+seconds;row.TransportWallSeconds[reason]=row.TransportWallSeconds.GetValueOrDefault(reason)+seconds;
        }
        qualityPosition=Game1.player.Position;qualityLocation=Game1.currentLocation.NameOrUniqueName;qualityCompleted=action?.completed??0;
        if(minute<=qualityMinute)return;
        string category=qualityProgress?"progressing_work":Game1.eventUp||Game1.activeClickableMenu!=null?"native_interaction":agentPending!=null?"waiting_model":Data.Autoplay.Survival.Mode=="sleep"?"returning_or_resting":Game1.player.UsingTool?"native_animation_pending":playerExecutor.Busy?"task_without_observed_progress":Data.Autoplay.Schedule.Tasks.Any(t=>t.state=="queued"&&t.wait_reason is "crop_not_mature" or "machine_not_ready")?"waiting_natural_growth":"business_stall_no_progress";
        if(!row.SleepTime.HasValue)CloseQualityInterval(row,minute);
        q.Sample(day,Math.Min(10,minute-qualityMinute),category);qualityMinute=minute;qualityProgress=false;
        Data.Autoplay.Record("survival_quality_sample",AgentJson.Encode(new{day,minute,state=category,capacity_version=Data.Autoplay.Capacity.Version}));
    }
    private void FinalizeQualityDay() {
        if(!AutoplayRunning)return;
        var q=Data.Autoplay.Quality;long assets=NativeAssetValue();
        foreach(var day in q.Days.Where(d=>!d.Finalized&&d.Day<Game1.Date.TotalDays)) {
            day.CashEnd=Game1.player.Money;day.AssetsEnd=assets;day.Finalized=true;
            Data.Autoplay.Record("survival_day_quality",AgentJson.Encode(new{day=day.Day,effective_labor_minutes=day.EffectiveLaborMinutes,available_minutes=day.AvailableMinutes,observed_awake_minutes=day.ObservedAwakeMinutes,verified_actions_delta=day.VerifiedActionsDelta,
                degradation_time=day.DegradationTime,degradation_reason=day.DegradationReason,capacity_constraints_active=Data.Autoplay.Capacity.Constraints,
                blocked_work=Data.Autoplay.Schedule.Tasks.Where(t=>t.state is "blocked" or "failed").Select(t=>new{t.spec.id,t.spec.tool,t.error}),cash_delta=day.CashEnd-day.CashStart,asset_delta=day.AssetsEnd-day.AssetsStart,inventory_turnover=day.InventoryTurnover,
                actual_sleep_time=day.SleepTime,actual_awake_minutes=day.ActualAwakeMinutes,time_breakdown_minutes=day.TimeBreakdownMinutes,wall_seconds=day.WallSecondsByState,transport_minutes=day.TransportMinutes,transport_wall_seconds=day.TransportWallSeconds,awake_labor_ratio=day.AwakeLaborRatio,accounting="clock interval allocated by observed wall-time states; UI/animation without observed work is blocked, detail in native events; legacy denominator retained separately",
                minute_states=day.MinutesByState,g1=q.G1,g2=q.G2,g3=day.LaborRatio,g3_threshold=(double?)null,valuation="native sale-value floor; excludes immature crops, buildings and unsellable tools"}));
        }
        foreach(var item in q.PendingSale.ToArray()) {
            int shipped=Game1.player.basicShipped.GetValueOrDefault(ItemRegistry.GetData(item.Key).ItemId);
            if(shipped-q.ShippingBaseline.GetValueOrDefault(item.Key)>=item.Value&&Game1.player.totalMoneyEarned>q.EarnedBaseline) {
                q.ConfirmedSaleDay=Game1.Date.TotalDays;q.CycleEvidence.Add("native_shipping_settled:"+item.Key+":"+item.Value+":day="+q.ConfirmedSaleDay);q.PendingSale.Remove(item.Key);
            }
        }
    }
    private void ObserveQualityReceipt(PlayerAction action) {
        if(!AutoplayRunning)return;
        Data.Autoplay.Record("native_action_timing",AgentJson.Encode(new{action.command_id,action.skill,action.status,action.error,action.phase,action.completed,navigation=JsonSerializer.SerializeToElement(action.effects,AgentJson.Options).EnumerateArray().Where(e=>e.TryGetProperty("kind",out var k)&&k.GetString()=="navigation_summary").ToArray()}));
        if(action.status!="succeeded")return;
        var q=Data.Autoplay.Quality;if(!q.Receipts.Add(action.command_id))return;
        var row=q.Current(Game1.Date.TotalDays,Game1.player.Money,NativeAssetValue());
        var effects=JsonSerializer.SerializeToElement(action.effects,AgentJson.Options);
        bool productive=action.skill is not ("player.sleep" or "player.move" or "player.travel" or "player.service" or "player.interact")&&effects.EnumerateArray().Any(e=>e.TryGetProperty("work_skill",out _)||e.TryGetProperty("kind",out var kind)&&kind.GetString()?.StartsWith("native_")==true);
        if(productive)row.VerifiedActionsDelta++;
        Data.Autoplay.Record("quality_native_receipt",AgentJson.Encode(new{action.command_id,action.skill,action.completed,productive,effects}));
        int BagCount(object? snapshot,string id) {
            var s=JsonSerializer.SerializeToElement(snapshot,AgentJson.Options);if(!s.TryGetProperty("inventory",out var bag))return 0;
            return bag.GetProperty("items").EnumerateArray().Select(i=>i.GetProperty("item")).Where(i=>i.TryGetProperty("id",out var value)&&value.GetString()==id).Sum(i=>i.GetProperty("count").GetInt32());
        }
        foreach(var effect in effects.EnumerateArray()) {
            if(effect.TryGetProperty("work_skill",out var skill)&&effect.TryGetProperty("after",out var after)&&effect.TryGetProperty("before",out var before)) {
                string tile=Game1.currentLocation.NameOrUniqueName+":"+after.GetProperty("x").GetInt32()+":"+after.GetProperty("y").GetInt32();
                if(skill.GetString()=="plant"&&after.TryGetProperty("crop",out var crop)&&crop.ValueKind==JsonValueKind.String){q.PlantedTiles[tile]="(O)"+crop.GetString();q.CycleEvidence.Add(action.command_id+":planted:"+tile);}
                if(skill.GetString()=="harvest"&&q.PlantedTiles.TryGetValue(tile,out var item)&&before.TryGetProperty("ready",out var ready)&&ready.ValueKind==JsonValueKind.True&&BagCount(action.after,item)>BagCount(action.before,item)) {
                    q.Harvested[item]=q.Harvested.GetValueOrDefault(item)+1;q.PlantedTiles.Remove(tile);q.CycleEvidence.Add(action.command_id+":harvested:"+item);row.InventoryTurnover++;
                }
            }
            if(!effect.TryGetProperty("kind",out var type))continue;
            if(type.GetString()=="native_shipment") {
                string id=effect.GetProperty("item").GetString()!;int count=Math.Min(effect.GetProperty("count").GetInt32(),q.Harvested.GetValueOrDefault(id));
                if(count>0){q.Harvested[id]-=count;q.PendingSale[id]=q.PendingSale.GetValueOrDefault(id)+count;q.ShippingBaseline[id]=Game1.player.basicShipped.GetValueOrDefault(ItemRegistry.GetData(id).ItemId);q.EarnedBaseline=Game1.player.totalMoneyEarned;q.CycleEvidence.Add(action.command_id+":shipped:"+id+":"+count);row.InventoryTurnover+=count;}
            }
            if(type.GetString()=="native_purchase"&&q.ConfirmedSaleDay>=0&&Game1.Date.TotalDays>=q.ConfirmedSaleDay&&effect.GetProperty("cost").GetInt32()>0&&effect.GetProperty("currency").GetInt32()==0&&ItemRegistry.Create(effect.GetProperty("item").GetString()!).Category==-74) {
                q.CycleCompleted=true;q.CycleEvidence.Add(action.command_id+":reinvested_seeds");Data.Autoplay.Record("production_cycle_verified",AgentJson.Encode(new{q.CycleEvidence}));
            }
        }
    }
}
