using System.Text.Json;
using StardewModdingAPI;
using StardewValley;
namespace Together;
public sealed partial class ModEntry {
    private object Round4NativeFixture(string mode) {
        if(!Settings.EnableLab||!Context.IsWorldReady||Game1.player.Name!="AgentLab")throw new InvalidOperationException("isolated_lab_required");
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
