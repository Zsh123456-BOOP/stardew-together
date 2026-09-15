using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private string businessLogError="";
    private readonly QueuedFileLog businessWriter=new();
    private DateTime businessLogAt;
    private int businessLogDay=-1,businessLogMinute=-1;
    private void WriteBusinessLog(string kind,string value) {
        // A distinct epoch and run make reloads distinguishable from new income.
        // This trace is for diagnosis, not a source of game state or model memory.
        try {
            string dir=Path.Combine(Helper.DirectoryPath,"logs",Game1.uniqueIDForThisGame.ToString(),agentSaveEpoch);
            string path=Path.Combine(dir,$"day-{Game1.Date.TotalDays}.jsonl");
            object payload;try{payload=JsonSerializer.Deserialize<JsonElement>(value);}catch(JsonException){payload=value;}
            businessWriter.Append(path,AgentJson.Encode(new{schema=1,build=typeof(ModEntry).Assembly.ManifestModule.ModuleVersionId,utc=DateTime.UtcNow,epoch=agentSaveEpoch,run=Data.Autoplay.RunId,day=Game1.Date.TotalDays,time=Game1.timeOfDay,kind,payload})+Environment.NewLine);
            businessLogError="";
        }catch(IOException e){businessLogError=e.GetType().Name;}catch(UnauthorizedAccessException e){businessLogError=e.GetType().Name;}
    }
    private void TickBusinessTelemetry() {
        memoryArchive?.QueueFlush();businessLogError=businessWriter.LastError;
        if(AutoplayRunning&&(businessLogError.Length>0||Data.Autoplay.Memory.LastError.Length>0)){PauseAutoplay("logging_failed_preserve_evidence:"+businessLogError+Data.Autoplay.Memory.LastError);return;}
        if(!AutoplayRunning||DateTime.UtcNow<businessLogAt)return;businessLogAt=DateTime.UtcNow.AddSeconds(5);
        int minute=DailyBudget.Minutes(Game1.timeOfDay)/30;
        if(businessLogDay==Game1.Date.TotalDays&&businessLogMinute==minute)return;
        businessLogDay=Game1.Date.TotalDays;businessLogMinute=minute;
        RefreshFacts(true);
        WriteBusinessLog("business_snapshot",AgentJson.Encode(ReadBusinessLedger()));
    }
    private object ReadBusinessLedger() {
        var p=Game1.player;var farm=Game1.getFarm();
        return new{commitments=OperationCommitments(),policy=Data.Business,operating=ReadOperatingLedger(),cleanup=FarmMaintenanceSummary(),cash=p.Money,total_earned=p.totalMoneyEarned,energy=new{current=p.Stamina,base_reserve=0,pending_farm_estimate=PendingFarmEnergy(),available_new_planting=AvailablePlantingEnergy(),advisory="待办农务消耗是估算，不是重复扣除的隐藏额度；模型可对每项工作指定reserve_stamina"},
            pending_shipping=farm.getShippingBin(p).Where(i=>i!=null).Select(i=>new{item=i.QualifiedItemId,count=i.Stack,quality=i.Quality,estimated_sale=i is StardewValley.Object o?(long)o.sellToStorePrice()*i.Stack:0}),
            inventory=Facts.Stock.Select(s=>new{s.Item,s.Count,s.Quality}),crops=new{Facts.DryCrops,Facts.RipeCrops,Facts.DeadCrops,total=farm.terrainFeatures.Values.OfType<StardewValley.TerrainFeatures.HoeDirt>().Count(d=>d.crop!=null),plots=farm.terrainFeatures.Pairs.Where(t=>t.Value is StardewValley.TerrainFeatures.HoeDirt {crop:not null}).Select(t=>new{tile=new[]{(int)t.Key.X,(int)t.Key.Y},seed=((StardewValley.TerrainFeatures.HoeDirt)t.Value).crop.netSeedIndex.Value})},animals=new{count=farm.getAllFarmAnimals().Count(),Facts.AnimalsUnpetted,Facts.FeedNeeded,Facts.HayInSilo},
            buildings=farm.buildings.Select(b=>new{type=b.buildingType.Value,construction=b.daysOfConstructionLeft.Value,upgrade=b.daysUntilUpgrade.Value}),
            machines=GoalMachines().Select(m=>new{location=m.Location.NameOrUniqueName,item=m.Object.QualifiedItemId,tile=m.Object.TileLocation,ready=m.Object.readyForHarvest.Value,minutes=m.Object.MinutesUntilReady,output=m.Object.heldObject.Value?.QualifiedItemId}),
            farm_investment=InvestmentObservation(),tasks=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).Select(t=>new{t.spec.id,t.spec.intent_id,t.spec.source,t.spec.actor,t.spec.tool,t.state,t.spec.purpose,t.spec.not_before,t.wait_reason,t.error}),
            model_waiting=agentPending!=null,model_ms=agentLastLatency,menu=Game1.activeClickableMenu?.GetType().Name,event_active=Game1.eventUp,
            location=p.currentLocation.NameOrUniqueName,p.health,p.Stamina,log_error=businessLogError,log_pending=businessWriter.Pending,note="pending_shipping是估值，不能当现金；库存/在制品不重复算收入；日志不代表通过验收"};
    }
}
