using System.Text.Json;
using StardewValley;
using StardewValley.TerrainFeatures;
namespace Together;
public sealed partial class ModEntry {
    private DateTime cooperationAt;
    internal object ConfigureOperating(JsonElement args) {
        var p=Data.Operating;string direction=AgentToolRegistry.Text(args,"direction",p.Direction);
        if(direction is not("balanced" or "cashflow" or "low_labor"))throw new InvalidOperationException("invalid_operating_direction");
        int player=AgentToolRegistry.Number(args,"player_water_limit",p.PlayerWaterLimit),partner=AgentToolRegistry.Number(args,"partner_water_limit",p.PartnerWaterLimit);
        if(player is <0 or >96||partner is <0 or >96)throw new InvalidOperationException("invalid_water_capacity");
        p.Direction=direction;p.PlayerWaterLimit=player;p.PartnerWaterLimit=partner;
        p.Reason=AgentToolRegistry.Text(args,"reason",p.Reason);if(p.Reason.Length>500)p.Reason=p.Reason[..500];
        Data.FarmInvestment.Priority=direction=="low_labor"?"low_labor":"income";
        return ReadOperatingLedger();
    }
    private int AccessibleStock(string item)=>Game1.player.Items.Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer())).Where(i=>i?.QualifiedItemId==item).Sum(i=>i.Stack);
    private static int CargoItem(JsonElement actor,string item)=>actor.GetProperty("cargo").EnumerateObject().Where(v=>v.Name.StartsWith(item+":",StringComparison.Ordinal)).Sum(v=>v.Value.GetInt32());
    private object ReadOperatingLedger() {
        var actors=World().GetProperty("actors").EnumerateArray().ToArray();var p=Data.Operating;
        return new{policy=p,partner=Data.Partner,capabilities=new{player="完整Farmer工具",partner=new[]{"water","harvest","forage","wood:twigs_only","fiber","stone","resource","store","pet"}},
            seed_cash_available=OperatingMath.CashForSeeds(Game1.player.Money,Data.Business.KeepGold,Data.Business.DailyBudget,Data.Business.ReservedToday+Data.FarmInvestment.ReservedToday,p.DevelopmentCashHeld),
            materials=p.MaterialTargets.Select(t=>new{item=t.Key,target=t.Value,accessible=AccessibleStock(t.Key),in_transit=actors.Sum(a=>CargoItem(a,t.Key)),to_gather=OperatingMath.GatherDeficit(t.Value,AccessibleStock(t.Key),actors.Sum(a=>CargoItem(a,t.Key))),to_deliver=OperatingMath.DeliveredDeficit(t.Value,AccessibleStock(t.Key))}),
            note="伙伴货袋在送达前不可制作消费；储備是目标不授予物品。NPC劳动预算不是Farmer体力。"};
    }
    private void UpdateOperatingTargets() {
        var p=Data.Operating;var b=Data.Business;p.MaterialTargets.Clear();p.DevelopmentCashHeld=0;
        // Storage is an explicit production prerequisite, not an unlimited timber quota.
        if(!SharedStorage().Any())p.MaterialTargets["(O)388"]=50;
        if(b.PendingAsset.Length>0) {
            var option=BusinessDevelopmentOptions().FirstOrDefault(o=>o.Id==b.PendingAsset);
            if(option!=null&&option.Gaps.Length==0) {
                p.DevelopmentCashHeld=Math.Min(option.Cash,Math.Max(0,Game1.player.Money-b.KeepGold));
                if(option.Kind=="building"&&DataLoader.Buildings(Game1.content).TryGetValue(option.Item,out var building))
                    foreach(var m in building.BuildMaterials??new())p.MaterialTargets[ItemRegistry.QualifyItemId(m.ItemId)??m.ItemId]=m.Amount;
                if(option.Kind=="machine") {
                    var recipe=goalRecipes.Values.FirstOrDefault(r=>r.Kind=="craft"&&r.Known&&r.Item==option.Item);
                    if(recipe!=null)foreach(var m in recipe.Inputs)p.MaterialTargets[m.Item]=Math.Max(p.MaterialTargets.GetValueOrDefault(m.Item),m.Count);
                }
            }
        }
        foreach(var goal in Data.SharedGoals.Where(g=>g.Status=="active"))foreach(var n in goal.Nodes.Where(n=>n.Kind=="gather"&&n.ToPrepare>0))p.MaterialTargets[n.Item]=Math.Max(p.MaterialTargets.GetValueOrDefault(n.Item),n.ToPrepare+AccessibleStock(n.Item));
    }
    private void TickCooperativeBusiness() {
        try {TickCooperativeCore();}
        catch(Exception e){Data.Operating.PartnerReason="cooperation_blocked:"+e.Message;cooperationAt=DateTime.UtcNow.AddSeconds(30);Data.Autoplay.Record("cooperation_error",Data.Operating.PartnerReason);WakeAgent("cooperation_requires_review");}
    }
    private void TickCooperativeCore() {
        if(!AutoplayRunning||!Data.Business.Enabled||DateTime.UtcNow<cooperationAt||Game1.eventUp||Game1.fadeToBlack||Game1.activeClickableMenu!=null)return;
        cooperationAt=DateTime.UtcNow.AddSeconds(3);var p=Data.Operating;
        UpdateOperatingTargets();
        var actors=World().GetProperty("actors").EnumerateArray().ToArray();var partner=actors.FirstOrDefault(a=>a.GetProperty("name").GetString()==PartnerName);
        bool present=partner.ValueKind==JsonValueKind.Object;
        Data.FarmInvestment.ManualWaterLimit=OperatingMath.WaterCapacity(p.PlayerWaterLimit,Game1.player.MaxStamina,40,present?p.PartnerWaterLimit:0);
        Data.FarmInvestment.Plots=Math.Clamp(Data.FarmInvestment.ManualWaterLimit,1,96);
        if(!present){p.PartnerReason="伙伴不可用，保留玩家独立经营";return;}
        string actor=partner.GetProperty("id").GetString()!;
        if(p.AllocationDay!=Game1.Date.TotalDays){
            p.AllocationDay=Game1.Date.TotalDays;
            foreach(string key in new[]{"water","harvest","pet"})Data.Autoplay.Routine.Assignments[key]=actor;
            foreach(var key in p.RetryAfter.Where(r=>r.Value<BusinessMinute).Select(r=>r.Key).ToArray())p.RetryAfter.Remove(key);
        }
        if(Data.Autoplay.Schedule.Tasks.Any(t=>t.spec.actor==actor&&!t.Terminal)||WorkActorBusy(actor))return;
        if(p.PartnerTask.Length>0) {
            var ended=Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==p.PartnerTask);
            if(ended?.state=="failed")p.RetryAfter[AgentToolRegistry.Text(ended.spec.args,"goal")+":"+AgentToolRegistry.Text(ended.spec.args,"location")]=BusinessMinute+60;
            p.PartnerTask="";
        }
        bool Queue(string goal,string location,int count,string why) {
            if(p.RetryAfter.GetValueOrDefault(goal+":"+location)>BusinessMinute)return false;
            string id="cooperate-"+Guid.NewGuid().ToString("N");
            var spec=new AgentTaskSpec{id=id,actor=actor,tool="work.run",args=JsonSerializer.SerializeToElement(new{actor_id=actor,goal,location,count,until=2100}),purpose=why,day=Game1.Date.TotalDays,deadline=2100};
            Data.Autoplay.Schedule.Submit(id,Data.Autoplay.Schedule.Revision,new(){spec},Game1.Date.TotalDays);p.PartnerTask=id;p.PartnerReason=why;
            Data.Autoplay.Record("cooperative_dispatch",AgentJson.Encode(new{actor,goal,location,count,why}));return true;
        }
        bool closesGap=p.MaterialTargets.Any(t=>AccessibleStock(t.Key)<t.Value&&AccessibleStock(t.Key)+CargoItem(partner,t.Key)>=t.Value);
        string delivery=StorageTiming.DeliveryReason(partner.GetProperty("storable_cargo").GetInt32(),partner.GetProperty("cargo_capacity").GetInt32()-CompanionCargoSlots(partner),closesGap,Game1.timeOfDay>=2030);
        if(delivery.Length>0&&SharedStorage().Any()&&Queue("store","Farm",0,delivery))return;
        if(Game1.timeOfDay>=2100){p.PartnerReason="结束劳动，等待共同过夜";return;}
        if(partner.TryGetProperty("labor",out var labor)&&labor.GetProperty("remaining").GetInt32()<4){
            p.PartnerReason="伙伴今日劳动预算耗尽；剩余必要农务由玩家承担";
            var routine=Data.Autoplay.Routine;
            if(routine.Assignments.Values.Contains(actor)){foreach(var key in routine.Assignments.Where(v=>v.Value==actor).Select(v=>v.Key).ToArray())routine.Assignments[key]="player";routine.Version++;routine.SubmittedDay=-1;}
            return;
        }
        bool PlayerDoing(string goal)=>Data.Autoplay.Schedule.Tasks.Any(t=>!t.Terminal&&t.spec.actor=="player"&&t.spec.tool=="work.run"&&AgentToolRegistry.Text(t.spec.args,"goal")==goal);
        var farm=Game1.getFarm();
        if(!PlayerDoing("harvest")&&farm.terrainFeatures.Values.OfType<HoeDirt>().Any(d=>d.crop!=null&&!d.crop.dead.Value&&d.readyForHarvest())&&Queue("harvest","Farm",0,"先收成熟作物，释放田地与原料"))return;
        if(!PlayerDoing("water")&&farm.terrainFeatures.Values.OfType<HoeDirt>().Any(d=>d.crop!=null&&!d.crop.dead.Value&&d.needsWatering()&&d.state.Value!=1)&&Queue("water","Farm",0,"完成真实缺水农务，让玩家处理采购与建设"))return;
        foreach(var t in p.MaterialTargets) {
            string goal=t.Key switch{"(O)388"=>"wood","(O)390"=>"stone","(O)771"=>"fiber",_=>""};if(goal==""||PlayerDoing(goal))continue;
            int missing=OperatingMath.GatherDeficit(t.Value,AccessibleStock(t.Key),actors.Sum(a=>CargoItem(a,t.Key)));if(missing<=0)continue;
            bool exists=farm.objects.Values.Any(o=>goal=="wood"?o.IsTwig():goal=="stone"?o.BaseName=="Stone":o.IsWeeds());
            if(exists&&Queue(goal,"Farm",Math.Min(30,missing),"为已批准经营目标补齐材料："+t.Key))return;
        }
        p.PartnerReason="当前经营分工已完成，等待新目标或玩家互动";
    }
}
