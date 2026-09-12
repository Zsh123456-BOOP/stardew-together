using System.Text.Json;
using StardewValley;
using StardewValley.Tools;

namespace Together;
public sealed record OperatingOpportunity(string Id,string Purpose,string Tool,object Args,string Evidence,int Energy,int Minutes);
public sealed partial class ModEntry {
    private List<OperatingOpportunity> OperatingOpportunities() {
        var rows=new List<OperatingOpportunity>();var player=Game1.player;
        AddBusinessOpportunities(rows);
        if(Facts.RipeCrops>0)rows.Add(new("harvest","收获成熟作物并接入销售/加工","work.run",new{goal="harvest",location="Farm",count=0},$"ripe={Facts.RipeCrops}",0,20));
        if(Facts.DryCrops>0&&player.Items.Any(i=>i is WateringCan))rows.Add(new("water","完成今日照料；工具会自动补水","work.run",new{goal="water",location="Farm",count=0},$"dry={Facts.DryCrops}",Facts.DryCrops*2,30));
        if(Game1.mailbox.Count>0)rows.Add(new("mail","读取实际邮件，检查经营解锁","player.read_mail",new{},$"mail={Game1.mailbox.Count}",0,20));
        if(Facts.MachinesReady>0)rows.Add(new("production","收取已完成加工并安排补料","farm.business_status",new{},$"ready_machines={Facts.MachinesReady}",0,30));
        if(player.Items.OfType<FishingRod>().Any()&&player.Stamina>=60&&Game1.timeOfDay<1900) {
            var blocked=Data.Autoplay.Failures.Entries.Any(e=>e.Day==Game1.Date.TotalDays&&e.Reason.Contains("fishing_stalled"));
            if(!blocked) {
                string location=FishingLocations("").FirstOrDefault()?.NameOrUniqueName??"";
                if(location.Length>0)rows.Add(new("fish-income","农务之外用可用体力获得真实渔获，补给后续作","work.run",new{goal="fish",location,count=8,reserve_stamina=30,until=Math.Min(2100,Game1.timeOfDay+400)},"rod_owned;native_fishing_conditions;catch_not_guaranteed",24,90));
            }
        }
        // Optional collection is a local opportunity, never a reason to abandon
        // an approved dependency chain or travel to an arbitrary forage map.
        bool committed=AgentActorHasWork("player")||Data.SharedGoals.Any(g=>g.AutoExecute&&g.Status=="active"&&g.AutoBlockedReason.Length==0);
        if(!committed) {
            var location=Game1.currentLocation;
            foreach(var pair in location.objects.Pairs.Where(p=>p.Value.isForage()&&!p.Value.bigCraftable.Value).Take(12)) {
                var item=pair.Value;if(!CapacityAdapter.CanReceive(player,item))continue;
                var stand=WorkStand(location,pair.Key.ToPoint());if(!stand.HasValue)continue;
                var path=PlayerExecutor.PreviewPath(location,stand.Value);if(path==null&&stand.Value!=player.TilePoint||path?.Count>12)continue;
                if(item.sellToStorePrice()<=0&&!AllReservations().Any(r=>r.Item==item.QualifiedItemId))continue;
                int minutes=10+(path?.Count??0)*2;
                rows.Add(new("forage:"+location.NameOrUniqueName,"空档顺路收取可用物资","work.run",new{goal="forage",location=location.NameOrUniqueName,item=item.QualifiedItemId,count=1,until=2100},$"reachable_tiles={path?.Count??0};capacity_checked;optional",0,minutes));break;
            }
        }
        AddCapabilityOpportunities(rows);
        return rows.Where(r=>DailyBudget.Fits(Game1.timeOfDay,player.Stamina,ReturnReserve(),r.Minutes,r.Energy)).Take(14).ToList();
    }
    private object OperatingDecisionBasis()=>new {
        money=Game1.player.Money,
        goals=Data.SharedGoals.Select(g=>new{g.Id,g.Revision,g.Status,g.AutoExecute,g.Count}),
        tools=Game1.player.Items.OfType<Tool>().Select(t=>new{t.QualifiedItemId,t.UpgradeLevel}),
        seeds=Game1.player.Items.Where(i=>i?.Category==-74).GroupBy(i=>i!.QualifiedItemId).Select(g=>new{item=g.Key,count=g.Sum(i=>i!.Stack)}),
        reserved=AllReservations().Select(r=>new{r.Item,r.Count,r.Quality}),
        native_quests=Game1.player.questLog.Select(q=>new{id=NativeQuestIdentity.Id(q),complete=q.completed.Value})
    };
    private string operatingRequestBasis="";
    private bool DecisionBasisChanged()=>operatingRequestBasis!=FailureKnowledge.Hash(AgentJson.Encode(OperatingDecisionBasis()));
}
