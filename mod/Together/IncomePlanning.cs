using System.Text.Json;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class ModEntry {
    private bool PursueIncome(ProgressPursuit pursuit,int achievement) {
        var rule=JsonSerializer.SerializeToElement(AchievementRules.Native(achievement));
        if(rule.GetProperty("condition_satisfied").GetBoolean()){PursuitState(pursuit,"waiting","实际收入已达到要求，等待原生成就登记");return true;}
        var policy=Data.Autoplay.Campaign;var p=Game1.player;var cropOutputs=Game1.cropData.Values.Select(c=>ItemRegistry.QualifyItemId(c.HarvestItemId)).ToHashSet();
        bool SaleItem(Item item)=>item is StardewValley.Object o&&!o.bigCraftable.Value&&!o.questItem.Value&&!o.specialItem&&o.canBeShipped()&&o.sellToStorePrice()>0&&(cropOutputs.Contains(o.QualifiedItemId)||o.Category is -5 or -6 or -18 or -26 or -4 or -81);
        if(policy.IncomeShipping) {
            var available=p.Items.Where(i=>i!=null&&SaleItem(i)).Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer()).Where(i=>i!=null&&SaleItem(i)))
                .GroupBy(i=>i.QualifiedItemId).Select(g=>new{Item=g.Key,Count=Math.Min(999,Math.Max(0,Math.Min(g.Sum(i=>i.Stack),DisposableStock(g.Key))-policy.IncomeKeepPerItem)),Carried=p.Items.Where(i=>i?.QualifiedItemId==g.Key).Sum(i=>i.Stack),Value=((StardewValley.Object)g.First()).sellToStorePrice()}).Where(g=>g.Count>0).OrderByDescending(g=>g.Carried>0).ThenByDescending(g=>(long)g.Count*g.Value).Take(1).ToArray();
            if(available.Length>0) {
                var item=available[0];var actions=new List<(string Tool,object Args)>();
                if(!PursuitMaterials(pursuit,new[]{(item.Item,item.Count,0)},actions))return true;
                actions.Add(("player.ship_items",new{items=new[]{new{item=item.Item,count=item.Count}}}));QueuePursuit(pursuit,actions);return true;
            }
            // Products still carried by a companion must be transported, not
            // treated as already available in the Farmer's inventory.
            foreach(var actor in World().GetProperty("actors").EnumerateArray()) {
                string id=actor.GetProperty("id").GetString()!;if(WorkActorBusy(id))continue;
                bool useful=actor.GetProperty("cargo").EnumerateObject().Any(c=>{int split=c.Name.LastIndexOf(':');return split>0&&SaleItem(ItemRegistry.Create(c.Name[..split]))&&c.Value.GetInt32()>policy.IncomeKeepPerItem;});
                if(useful){QueuePursuit(pursuit,new[]{("work.run",(object)new{actor_id=id,goal="store",until=2100})});return true;}
            }
        }
        var farm=Game1.getFarm();
        if(farm.terrainFeatures.Values.OfType<HoeDirt>().Any(d=>d.crop!=null&&!d.crop.dead.Value&&d.readyForHarvest())) {
            QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="harvest",location="Farm",until=2100})});return true;
        }
        if(farm.terrainFeatures.Values.OfType<HoeDirt>().Any(d=>d.crop!=null&&!d.crop.dead.Value&&d.needsWatering()&&d.state.Value!=1)) {
            QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="water",location="Farm",until=2100})});return true;
        }
        PursuitState(pursuit,"waiting",!policy.IncomeShipping?"收入目标的自动余量出货策略已关闭":!Data.FarmInvestment.Enabled?"当前没有可出货余量；通过 farm.autonomy 选择投资/照料预算后，算法每日重算经营":"等待作物/机器成熟和真实出货到账；经营政策继续投资，今天仍可推进其它目标");return true;
    }
}
