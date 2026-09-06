using System.Text.Json;
using StardewValley;
using StardewValley.GameData.Machines;

namespace Together;
public sealed partial class ModEntry {
    private sealed record ProductionChoice(string Machine,string Location,string Input,int Count,int Minutes,double Margin,int Available,Dictionary<string,int> Fuel);
    private IEnumerable<ProductionChoice> BusinessProductionChoices(bool installedOnly=true,IEnumerable<StardewValley.Object>? forecastInputs=null) {
        var p=Game1.player;
        var inputs=(forecastInputs??p.Items.Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer())).OfType<StardewValley.Object>())
            .Where(o=>!o.bigCraftable.Value&&!o.questItem.Value&&o.Stack>0).GroupBy(o=>(o.QualifiedItemId,o.Quality)).Select(g=>g.First()).Take(100).ToArray();
        var machines=GoalMachines().Select(m=>(Machine:m.Object,Location:m.Location));
        if(!installedOnly)machines=machines.Concat(goalRecipes.Values.Where(r=>r.Kind=="craft"&&r.Known&&r.Item.StartsWith("(BC)")&&DataLoader.Machines(Game1.content).ContainsKey(r.Item))
            .Select(r=>(Machine:ItemRegistry.Create<StardewValley.Object>(r.Item),Location:(GameLocation)Game1.getFarm())));
        foreach(var entry in machines.DistinctBy(m=>(m.Machine.QualifiedItemId,m.Location.NameOrUniqueName))) {
            var machine=entry.Machine;var data=machine.GetMachineData();if(data==null)continue;
            foreach(var actual in inputs) {
                var input=actual.getOne();input.Stack=forecastInputs==null?Math.Min(999,DisposableStock(input.QualifiedItemId)):999;if(input.Stack<1)continue;
                if(!MachineDataUtility.TryGetMachineOutputRule(machine,data,MachineOutputTrigger.ItemPlacedInMachine,input,p,entry.Location,out var rule,out var trigger,out _,out _))continue;
                // Only declarative, fixed outputs are valued here. Random products,
                // custom callbacks and recursive machine side effects need an adapter.
                if(rule.RecalculateOnCollect||rule.OutputItem?.Count!=1)continue;var output=rule.OutputItem[0];
                if(!string.IsNullOrEmpty(output.OutputMethod)||output.RandomItemId?.Count>0||output.StackModifiers?.Count>0||output.MaxStack>Math.Max(1,output.MinStack)||output.ItemId==null||ItemRegistry.GetDataOrErrorItem(output.ItemId).IsErrorItem)continue;
                if(!GameStateQuery.CheckConditions(output.Condition,entry.Location,p,null,input,random:new Random(0)))continue;
                var product=ItemRegistry.Create(output.ItemId,Math.Max(1,output.MinStack)) as StardewValley.Object;if(product==null)continue;
                if(output.CopyPrice)product.Price=actual.Price;if(output.CopyQuality)product.Quality=actual.Quality;
                product.Price=(int)Utility.ApplyQuantityModifiers(product.Price,output.PriceModifiers,output.PriceModifierMode,entry.Location,p,product,input);
                int required=Math.Max(1,trigger.RequiredCount);var fuel=new Dictionary<string,int>();bool valid=true;
                foreach(var extra in data.AdditionalConsumedItems??new()) {
                    string id=ItemRegistry.QualifyItemId(extra.ItemId)??extra.ItemId;
                    if(ItemRegistry.GetDataOrErrorItem(id).IsErrorItem){valid=false;break;}fuel[id]=fuel.GetValueOrDefault(id)+extra.RequiredCount;
                }
                if(!valid)continue;
                double cost=(double)actual.sellToStorePrice()*required+fuel.Sum(f=>(double)((ItemRegistry.Create(f.Key) as StardewValley.Object)?.sellToStorePrice()??0)*f.Value);
                double margin=(double)product.sellToStorePrice()*product.Stack-cost;
                int minutes=rule.DaysUntilReady>0?rule.DaysUntilReady*1440:Math.Max(10,rule.MinutesUntilReady);
                if(margin>0)yield return new(machine.QualifiedItemId,entry.Location.NameOrUniqueName,input.QualifiedItemId,required,minutes,margin,input.Stack,fuel);
            }
        }
    }
    private bool RunBusinessProduction() {
        var ready=GoalMachines().FirstOrDefault(m=>m.Object.readyForHarvest.Value&&m.Object.heldObject.Value!=null);
        if(ready.Object!=null)return QueueBusiness("collect:"+ready.Location.NameOrUniqueName,new[]{("player.machine",(object)new{mode="collect",location=ready.Location.NameOrUniqueName,count=0})},"先收已完成的产品，释放真实机器产能");
        foreach(var option in BusinessProductionChoices().OrderByDescending(c=>c.Margin/Math.Max(1,c.Minutes)).ThenBy(c=>c.Input,StringComparer.Ordinal)) {
            int idle=GoalMachines().Count(m=>m.Location.NameOrUniqueName==option.Location&&m.Object.QualifiedItemId==option.Machine&&m.Object.heldObject.Value==null);
            int count=Math.Min(12,Math.Min(idle,option.Available/option.Count));
            foreach(var fuel in option.Fuel)count=Math.Min(count,DisposableStock(fuel.Key)/Math.Max(1,fuel.Value+(fuel.Key==option.Input?option.Count:0)));
            if(count<1)continue;var actions=new List<(string Tool,object Args)>();
            var needs=option.Fuel.ToDictionary(p=>p.Key,p=>p.Value*count);needs[option.Input]=needs.GetValueOrDefault(option.Input)+option.Count*count;
            if(!BusinessMaterials(needs,actions,false))continue;
            actions.Add(("player.machine",new{mode="load",location=option.Location,machine=option.Machine,item=option.Input,count}));
            if(QueueBusiness("process:"+option.Machine+":"+option.Input,actions,"按增值/机器时间排序；投入真实余量，预计每批增值 "+option.Margin.ToString("0")))return true;
        }
        return false;
    }
    private Dictionary<string,int> BusinessRawReserves() {
        var reserve=new Dictionary<string,int>();
        foreach(var group in BusinessProductionChoices().GroupBy(c=>c.Machine)) {
            int capacity=GoalMachines().Count(m=>m.Object.QualifiedItemId==group.Key);
            // One batch per machine, shared across alternative raw materials.
            foreach(var choice in group.OrderByDescending(c=>c.Margin/Math.Max(1,c.Minutes))) {
                int batches=Math.Min(capacity,Math.Max(0,choice.Available-reserve.GetValueOrDefault(choice.Input))/choice.Count);
                if(batches<=0)continue;reserve[choice.Input]=reserve.GetValueOrDefault(choice.Input)+batches*choice.Count;foreach(var fuel in choice.Fuel)reserve[fuel.Key]=reserve.GetValueOrDefault(fuel.Key)+batches*fuel.Value;capacity-=batches;if(capacity==0)break;
            }
        }
        return reserve;
    }
    private List<(string Tool,object Args)> BusinessShipment() {
        UpdateOperatingTargets();var reserves=BusinessRawReserves();var cropOutputs=Game1.cropData.Values.Select(c=>ItemRegistry.QualifyItemId(c.HarvestItemId)).ToHashSet();
        var stock=Game1.player.Items.Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer())).OfType<StardewValley.Object>()
            .Where(o=>!o.bigCraftable.Value&&!o.questItem.Value&&!o.specialItem&&ApprovedMaterialSale(o.QualifiedItemId)&&o.canBeShipped()&&o.sellToStorePrice()>0&&(BusinessRetention.Materials.Contains(o.QualifiedItemId)||cropOutputs.Contains(o.QualifiedItemId)||o.Category is -5 or -6 or -18 or -26 or -4 or -81))
            .GroupBy(o=>o.QualifiedItemId).Select(g=>new{item=g.Key,count=Math.Min(999,BusinessRetention.Sellable(g.Sum(i=>i.Stack),DisposableStock(g.Key),SaleFloor(g.Key),reserves.GetValueOrDefault(g.Key)+2)),price=g.Max(o=>o.sellToStorePrice())}).Where(x=>x.count>0).OrderByDescending(x=>(long)x.count*x.price).Take(12).ToArray();
        // Selling what's already carried releases real slots first. Never hold
        // every sale hostage to later mixed-quality warehouse withdrawals.
        var carried=stock.Select(i=>new{i.item,count=Math.Min(i.count,Game1.player.Items.Where(b=>b?.QualifiedItemId==i.item).Sum(b=>b.Stack))}).Where(i=>i.count>0).ToArray();
        if(carried.Length>0)return new(){("player.ship_items",new{items=carried})};
        if(stock.Length==0)return new();
        if(Game1.player.freeSpotsInInventory()==0)return new(){("work.run",new{goal="store",required_free_slots=1,until=2500})};
        foreach(var item in stock) {
            // One exact-quality stack per bounded batch fits one genuinely free
            // slot. Recompute stock after each native sale instead of preloading
            // several item IDs whose qualities need more slots than their IDs.
            var source=SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer()).Where(i=>i?.QualifiedItemId==item.item).OrderByDescending(i=>i.Quality).FirstOrDefault();
            if(source==null)continue;int count=Math.Min(item.count,Math.Min(source.Stack,source.maximumStackSize()));
            if(count>0)return new(){("work.run",new{goal="withdraw",item=item.item,count,quality=source.Quality,exact_quality=true,until=2500}),("player.ship_items",new{items=new[]{new{item=item.item,count}}})};
        }
        return new();
    }
    private bool RunBusinessShipping() {
        // The bin pays only overnight. Small drops should not each cause a trip.
        if(Game1.timeOfDay<1700&&Game1.player.freeSpotsInInventory()>0)return false;
        var actions=BusinessShipment();
        return actions.Count>0&&QueueBusiness("shipping-batch",actions,"集中交付当天可售产品，保留经营材料；只记原生次日收入");
    }
    private bool QueueClosingShipment(ScheduledAgentTask sleep) {
        if(!Data.Business.Enabled)return false;
        int transit=Game1.currentLocation.NameOrUniqueName is "Farm" or "FarmHouse"?30:90;
        if(DailyBudget.Minutes(Game1.timeOfDay)+transit+30>1500)return false; // retain native return/save time before 1am
        if(Data.Autoplay.Schedule.Tasks.Count(t=>t.spec.source=="closing:"+sleep.spec.id)>=24)return false;
        CheckAgentSleep(sleep.spec.args);
        var actions=BusinessShipment();if(actions.Count==0)return false;
        var tasks=actions.Select(a=>new AgentTaskSpec{id="closing-"+Guid.NewGuid().ToString("N"),tool=a.Tool,args=JsonSerializer.SerializeToElement(a.Args),day=Game1.Date.TotalDays,deadline=2500,intent_id=sleep.spec.intent_id,source="closing:"+sleep.spec.id,purpose="睡前分批交付真实可售产品；按品质容量取货，不卖承诺材料"}).ToList();
        Data.Autoplay.Schedule.InsertBefore(sleep,tasks);
        Data.Autoplay.Record("closing_shipment",AgentJson.Encode(new{sleep=sleep.spec.id,tasks}));return true;
    }
}
