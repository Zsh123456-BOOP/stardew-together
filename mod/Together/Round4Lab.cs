using System.Text.Json;
using StardewModdingAPI;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private object Round4NativeFixture(string mode) {
        if(!Settings.EnableLab||!Context.IsWorldReady||Game1.player.Name!="AgentLab")throw new InvalidOperationException("isolated_lab_required");
        if(mode=="u6_read")return new{inventory_plan=InventoryPlanning(),sales=BusinessSaleStock(),sleep_review=sleepReview,schedule=AgentPlanRead(),snapshot=AgentSnapshot()};
        if(mode=="u6_capacity_scope") {
            if(playerExecutor.Busy||WorkActorBusy("player"))throw new InvalidOperationException("lab_requires_idle");
            int free=CapacityAdapter.Of(Game1.player).FreeSlots;if(free<1||free>=12)throw new InvalidOperationException("lab_requires_partly_occupied_bag");
            string before=AgentJson.Encode(AgentToolRegistry.Inventory());
            RecordCapacityConstraint("capacity_all_candidates_infeasible","player",new[]{"fixture:larger_request_previously_infeasible"},"store",free+1);
            string? large=null,small=null;
            try{GuardCapacity("player","work.run",JsonSerializer.SerializeToElement(new{goal="store",required_free_slots=free+1}));}catch(InvalidOperationException e){large=e.Message;}
            try{GuardCapacity("player","work.run",JsonSerializer.SerializeToElement(new{goal="store",required_free_slots=free}));}catch(InvalidOperationException e){small=e.Message;}
            return new{free,large,small,inventory_unchanged=before==AgentJson.Encode(AgentToolRegistry.Inventory()),constraint=Data.Autoplay.Capacity.Constraints,
                evidence="只注入声明缓存，未修改真实物品；随后必须实际执行已满足容量的存货任务核验零移动"};
        }
        if(mode=="u6_sleep_review") {
            var task=new ScheduledAgentTask{spec=new(){id="lab-old-sleep",tool="player.sleep",source="model",day=Game1.Date.TotalDays,sleep_review_day=Game1.Date.TotalDays,sleep_review_time=Game1.timeOfDay-10,sleep_review_progress=Data.Autoplay.VerifiedActions}};
            Data.Autoplay.Schedule.Tasks.Add(task);string before=AgentJson.Encode(AgentSnapshot());
            bool reviewed=ReviewQueuedSleep(task);
            return new{reviewed,task.state,task.error,sleepReview,native_state_unchanged=before==AgentJson.Encode(AgentSnapshot()),evidence="仅模拟先前排队时刻，进入实际睡眠派发前复核；未推进游戏时间或原生睡眠"};
        }
        if(mode=="autonomy7") {
            RefreshFacts(true);var at=Game1.player.TilePoint;var l=Game1.currentLocation;var paths=new System.Collections.Generic.List<object>();
            foreach(var end in new[]{new Microsoft.Xna.Framework.Point(at.X+6,at.Y),new(at.X-6,at.Y),new(at.X,at.Y+6),new(at.X,at.Y-6)}) {
                var path=PlayerExecutor.PreviewPath(l,end);if(path!=null)paths.Add(new{end=new[]{end.X,end.Y},tiles=path.Select(p=>new[]{p.X,p.Y}),blocked=path.Where(p=>p!=at&&!PlayerExecutor.Passable(l,p)).Select(p=>new[]{p.X,p.Y})});
            }
            return new{progress=DailyProgressDigest(),planting_execution=PlantingExecutionFacts(),opportunities=OperatingOpportunities(),orders=Data.Maintenance.Orders,paths,
                tools=Game1.player.Items.OfType<Tool>().Select(t=>new{t.QualifiedItemId,energy=PlayerExecutor.SwingEnergy(t)}),snapshot=AgentSnapshot()};
        }
        if(mode=="opportunity_receipt")return new{receipt=WithBlockedAlternatives(JsonSerializer.SerializeToElement(new{status="blocked",error="fixture_observation_only"})),protection_reasons=ProtectionReasons(),overlay=overlayLines,night=NightStatus()};
        if(mode=="diary")return new{diary=Data.Autoplay.Memory.Diary,context=AgentMemoryContext()};
        if(mode=="material_autonomy") {Data.Business.Enabled=true;businessAt=DateTime.UtcNow.AddHours(1);return new{enabled=true,material_targets=Data.Operating.MaterialTargets,note="policy-only fixture; native inventory unchanged"};}
        if(mode=="diary_replay") {
            var a=playerExecutor.Current??throw new InvalidOperationException("native_receipt_required");
            string before=AgentJson.Encode(Data.Autoplay.Memory.Diary);RecordNativeDiary(a);RecordNativeDiary(a);
            return new{unchanged=before==AgentJson.Encode(Data.Autoplay.Memory.Diary)};
        }
        if(mode=="service_recovery") {
            if(playerExecutor.Busy||WorkActorBusy("player")||Game1.activeClickableMenu!=null)throw new InvalidOperationException("lab_requires_idle");
            var old=Data.FarmInvestment;string before=AgentJson.Encode(AgentToolRegistry.Inventory());
            var failed=new ScheduledAgentTask{spec=new(){id="lab-r4-service",tool="player.service",day=Game1.Date.TotalDays},state="failed",error="loadout_missing:fixture_dependency"};
            Data.Autoplay.Schedule.Tasks.Add(failed);
            Data.FarmInvestment=new(){Enabled=true,Day=Game1.Date.TotalDays,Phase="executing",Tasks=new(){failed.spec.id},ReservedToday=100};
            try{TickFarmInvestment();return new{phase=Data.FarmInvestment.Phase,recovered=Data.FarmInvestment.PurchaseRecoveryUsed,owned_only=Data.FarmInvestment.OwnedSeedsOnly,reserved=Data.FarmInvestment.ReservedToday,inventory_unchanged=before==AgentJson.Encode(AgentToolRegistry.Inventory()),evidence="AgentLab scheduling fault injection; no native stock mutation"};}
            finally{Data.FarmInvestment=old;Data.Autoplay.Schedule.Tasks.Remove(failed);}
        }
        if(mode=="selection") {
            int before=Game1.player.CurrentToolIndex;bool rejected=false;
            try{PlayerSelection.Set(Game1.player,-1);}catch(InvalidOperationException e){rejected=e.Message.StartsWith("invalid_selected_slot");}
            PlayerSelection.Neutral(Game1.player);var selected=Game1.player.CurrentToolIndex;
            return new{rejected,before,selected,valid=selected>=0&&selected<Game1.player.Items.Count,inventory=AgentToolRegistry.Inventory()};
        }
        if(mode=="read") {RefreshFacts(true);return new{selected=Game1.player.CurrentToolIndex,slots=Game1.player.Items.Count,inventory=AgentToolRegistry.Inventory(),investment=Data.FarmInvestment,ledger=new{Facts.SpentToday,Facts.PurchasedItems},overlay=overlayLines,pending_planting_energy=PendingFarmEnergy(),snapshot=AgentSnapshot(),schedule=AgentPlanRead(),farm=Round3NativeFixture("read")};}
        if(mode=="overlay") {Data.Autoplay.Plan="先购买种子 [stock_target]，再整理田块 request_id=abc123。";overlayNext=DateTime.MinValue;RefreshAgentOverlay();return new{lines=overlayLines};}
        if(mode=="net") {
            var tasks=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).Select(t=>new{t.spec.id,t.spec.tool,needs=DescribeKit(t).Needs.Select(n=>new{n.Label,n.Count})}).ToArray();return new{tasks};
        }
        throw new InvalidOperationException("unknown_round4_fixture");
    }
}
