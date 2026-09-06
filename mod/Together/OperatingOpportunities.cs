using System.Text.Json;
using StardewValley;
using StardewValley.Tools;

namespace Together;
public sealed record OperatingOpportunity(string Id,string Purpose,string Tool,object Args,string Evidence,int Energy,int Minutes);
public sealed partial class ModEntry {
    private List<OperatingOpportunity> OperatingOpportunities() {
        var rows=new List<OperatingOpportunity>();var player=Game1.player;
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
        foreach(var location in Game1.locations.Where(l=>l.IsOutdoors&&l.objects.Values.Any(o=>o.isForage()&&!o.bigCraftable.Value)).OrderBy(l=>l==Game1.currentLocation?0:1).Take(3)) {
            if(PlayerExecutor.NextExit(Game1.currentLocation,location.NameOrUniqueName)==null&&location!=Game1.currentLocation)continue;
            rows.Add(new("forage:"+location.NameOrUniqueName,"拾取已知季节产物，按用途留用或合批出售","work.run",new{goal="forage",location=location.NameOrUniqueName,count=0,until=2100},$"observed_forage={location.objects.Values.Count(o=>o.isForage()&&!o.bigCraftable.Value)}",0,45));
        }
        return rows.Where(r=>DailyBudget.Fits(Game1.timeOfDay,player.Stamina,ReturnReserve(),r.Minutes,r.Energy)).Take(8).ToList();
    }
    private object OperatingDecisionBasis()=>new {
        money=Game1.player.Money,
        tools=Game1.player.Items.OfType<Tool>().Select(t=>new{t.QualifiedItemId,t.UpgradeLevel}),
        seeds=Game1.player.Items.Where(i=>i?.Category==-74).GroupBy(i=>i!.QualifiedItemId).Select(g=>new{item=g.Key,count=g.Sum(i=>i!.Stack)}),
        reserved=AllReservations().Select(r=>new{r.Item,r.Count,r.Quality}),
        native_quests=Game1.player.questLog.Select(q=>new{id=NativeQuestIdentity.Id(q),complete=q.completed.Value})
    };
    private string operatingRequestBasis="";
    private bool DecisionBasisChanged()=>operatingRequestBasis!=FailureKnowledge.Hash(AgentJson.Encode(OperatingDecisionBasis()));
}
