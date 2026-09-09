using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private IEnumerable<JsonElement> CompanionCleanupCandidates(JsonElement actor,FarmCleanupOrder order) {
        if(actor.GetProperty("location").GetString()!="Farm")yield break;
        EnsureMaintenanceMask();var farm=Game1.getFarm();
        foreach(var c in actor.GetProperty("candidates").EnumerateArray().OrderBy(c=>c.TryGetProperty("route_tiles",out var cost)?cost.GetInt32():int.MaxValue)) {
            string skill=c.GetProperty("skill").GetString()!;if(skill is not ("clear" or "mine"))continue;
            var tile=c.GetProperty("tile");var at=new FarmCell(tile[0].GetInt32(),tile[1].GetInt32());
            if(!farm.objects.TryGetValue(new(at.X,at.Y),out var obj))continue;
            string kind=obj.IsWeeds()?"weed":obj.IsTwig()?"twig":obj.BaseName=="Stone"?"stone":"";
            if(kind.Length==0||MaintenanceProtects(farm,new(at.X,at.Y)))continue;
            var area=CleanupArea(at);
            if(!CleanupMatches(order,new(at,kind,area.Scope,area.Zone,area.Trees,4)))continue;
            if(playerExecutor.ClaimsTile("Farm",at.X,at.Y)||AgentTileBusy("Farm",at.X,at.Y))continue;
            yield return c;
        }
    }
    private bool TryQueueCompanionCleanup(JsonElement actor) {
        if(!Data.Maintenance.Enabled||actor.GetProperty("location").GetString()!="Farm")return false;
        int remaining=actor.GetProperty("labor").GetProperty("remaining").GetInt32();
        int allowance=Together.Shared.CompanionLabor.OptionalAllowance(remaining,Facts.DryCrops,Facts.RipeCrops,Game1.timeOfDay)/4;
        if(allowance<=0)return false;
        foreach(var order in Data.Maintenance.Orders.Where(o=>o.Status=="active"&&o.TaskId.Length==0&&o.RetryAt<=BusinessMinute&&o.Until>Game1.timeOfDay)) {
            FarmCleanupRules.NewDay(order,Game1.Date.TotalDays);
            int count=Math.Min(allowance,FarmCleanupRules.RemainingBudget(order));
            if(count<=0||!CompanionCleanupCandidates(actor,order).Any())continue;
            string id="cooperate-cleanup-"+Guid.NewGuid().ToString("N"),who=actor.GetProperty("id").GetString()!;
            var spec=new AgentTaskSpec{id=id,actor=who,tool="work.run",day=Game1.Date.TotalDays,deadline=order.Until,
                args=JsonSerializer.SerializeToElement(new{actor_id=who,goal="cleanup",cleanup_id=order.Id,location="Farm",count,until=order.Until}),
                purpose="伙伴承担已批准片区清理，混合杂草/树枝/石块，保留农务劳动额度",source="cooperative_maintenance",priority=25};
            Data.Autoplay.Schedule.Submit(id,Data.Autoplay.Schedule.Revision,new(){spec},Game1.Date.TotalDays);
            order.TaskId=id;Data.Operating.PartnerTask=id;Data.Operating.PartnerReason=spec.purpose;
            Data.Autoplay.Record("cooperative_dispatch",AgentJson.Encode(new{actor=who,goal="cleanup",order=order.Id,count,spec.purpose}));return true;
        }
        return false;
    }
    private void TickCompanionCleanup(SemanticJob job) {
        var order=Data.Maintenance.Orders.FirstOrDefault(o=>o.Id==job.CleanupId);
        if(order==null||order.Status!="active"||!Data.Maintenance.Enabled){StopSemanticWork(job,"cleanup_order_paused");return;}
        if(FarmCleanupRules.RemainingBudget(order)<=0){StopSemanticWork(job,"cleanup_daily_budget_reached",true);return;}
        var actor=WorkActor(job.actor);
        if(Together.Shared.CompanionLabor.OptionalAllowance(actor.GetProperty("labor").GetProperty("remaining").GetInt32(),Facts.DryCrops,Facts.RipeCrops,Game1.timeOfDay)<4){StopSemanticWork(job,"partner_labor_reserved_for_farm_or_exhausted");return;}
        if(actor.GetProperty("cargo_capacity").GetInt32()-CompanionCargoSlots(actor)<4){job.Storing=true;TickWorkStorage(job);return;}
        var candidates=CompanionCleanupCandidates(actor,order).Where(c=>!job.Excluded.Contains(c.GetProperty("tile")[0]+","+c.GetProperty("tile")[1])).ToArray();
        if(candidates.Length==0){StopSemanticWork(job,"companion_local_cleanup_exhausted",true);return;}
        var local=order.Patch is {} patch?candidates.Where(c=>Math.Abs(c.GetProperty("tile")[0].GetInt32()-patch.X)+Math.Abs(c.GetProperty("tile")[1].GetInt32()-patch.Y)<=8).ToArray():candidates;
        var next=(local.Length>0?local:candidates)[0];var tile=next.GetProperty("tile");order.Patch=new(tile[0].GetInt32(),tile[1].GetInt32());
        WorkChild(job,"companion.assign",new{actor_id=job.actor,skill=next.GetProperty("skill").GetString(),target_id=next.GetProperty("target_id").GetString()},"labor",$"{tile[0]},{tile[1]}");
    }
}
