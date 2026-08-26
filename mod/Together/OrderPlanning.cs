using System.Text.Json;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Locations;
using StardewValley.SpecialOrders;
using StardewValley.SpecialOrders.Objectives;

namespace Together;
public sealed partial class ModEntry {
    private IEnumerable<Item> OrderStock()=>Game1.player.Items.Where(i=>i!=null).Concat(SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer()).Where(i=>i!=null));
    private bool PursueOrder(ProgressPursuit pursuit,SpecialOrder order) {
        if(order.questState.Value!=SpecialOrderStatus.InProgress){PursuitState(pursuit,"waiting","订单等待原生完成、奖励或已过期");return true;}
        var p=Game1.player;var policy=Data.Autoplay.Campaign;
        foreach(var entry in order.objectives.Select((o,i)=>new{Objective=o,Index=i}).Where(e=>!e.Objective.IsComplete()&&!e.Objective.failOnCompletion.Value)) {
            var objective=entry.Objective;int remaining=Math.Max(0,objective.GetMaxCount()-objective.GetCount());var actions=new List<(string Tool,object Args)>();
            if(objective is DonateObjective donation) {
                if(p.Items.Any(i=>i!=null&&donation.GetAcceptCount(i,i.Stack)>0)||remaining==0) {QueuePursuit(pursuit,new[]{("player.order_donate",(object)new{order=order.questKey.Value,dropbox=donation.dropBox.Value})});return true;}
                var stored=OrderStock().FirstOrDefault(i=>donation.IsValidItem(i));
                if(stored!=null) {QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="withdraw",item=stored.QualifiedItemId,count=Math.Min(stored.Stack,remaining),quality=stored.Quality})});return true;}
                string? source=OrderMaterialCandidates(objective).Take(48).FirstOrDefault(i=>CanPreparePursuitItem(i,Math.Min(24,remaining)));
                if(source!=null){if(PursuitMaterials(pursuit,new[]{(source,Math.Min(24,remaining),0)},actions)&&actions.Count>0)QueuePursuit(pursuit,actions);return true;}
            } else if(objective is DeliverObjective delivery) {
                var npc=Game1.getCharacterFromName(delivery.targetName.Value);if(npc==null)continue;
                int slot=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]!=null&&delivery.OnItemDelivered(p,npc,p.Items[i],true)>0,-1);
                if(slot>=0){QueuePursuit(pursuit,new[]{("player.social",(object)new{npc=npc.Name,mode="order_deliver",order_id=order.questKey.Value,objective=entry.Index,slot,item=p.Items[slot].QualifiedItemId})});return true;}
                string? source=OrderMaterialCandidates(objective).Take(48).FirstOrDefault(i=>CanPreparePursuitItem(i,remaining));
                if(source!=null&&remaining<=999){if(PursuitMaterials(pursuit,new[]{(source,remaining,0)},actions)&&actions.Count>0)QueuePursuit(pursuit,actions);else if(actions.Count==0&&pursuit.State!="running")PursuitState(pursuit,"waiting","订单需要同一品质/属性的足量单叠，现有分散堆栈不足");return true;}
            } else if(objective is FishObjective) {
                var item=OrderMaterialCandidates(objective).Take(48).FirstOrDefault(i=>FishingLocations(i).Any());
                if(item!=null){QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="fish",item,count=Math.Min(20,remaining),order_id=order.questKey.Value,objective=entry.Index,until=2100})});return true;}
            } else if(objective is CollectObjective) {
                foreach(string item in OrderMaterialCandidates(objective).Take(48)) {
                    if(ReadyLivingSource(item) is {} source){QueuePursuit(pursuit,new[]{("work.run",(object)new{goal=source.Skill,item,location=source.Location,count=Math.Min(999,remaining),order_id=order.questKey.Value,objective=entry.Index,until=2100})});return true;}
                    string skill=item switch{"(O)388"=>"wood","(O)390"=>"stone","(O)771"=>"fiber","(O)709"=>"hardwood",_=>"resource"};
                    string? location=FindGoalResourceLocation(item,skill);
                    if(location!=null){QueuePursuit(pursuit,new[]{("work.run",(object)new{goal=skill,item,location,count=Math.Min(999,remaining),order_id=order.questKey.Value,objective=entry.Index,include_trees=true,until=2100})});return true;}
                    if(FishingLocations(item).Any()){QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="fish",item,count=Math.Min(20,remaining),order_id=order.questKey.Value,objective=entry.Index,until=2100})});return true;}
                    if(HasLivingMaterialRoute(item)&&PrepareLivingMaterial(new SharedGoal{Item=item},new GoalNode{Item=item,Required=Math.Min(remaining,24)},(t,a,_)=>actions.Add((t,a)),out _)&&actions.Count>0){QueuePursuit(pursuit,actions);return true;}
                }
            } else if(objective is ShipObjective ship) {
                var bin=Game1.getFarm().getShippingBin(p).Where(i=>i!=null&&OrderRules.Accepts(ship,i)).ToArray();
                long pending=ship.useShipmentValue.Value?bin.OfType<StardewValley.Object>().Sum(i=>(long)i.sellToStorePrice()*i.Stack):bin.Sum(i=>(long)i.Stack);
                if(pending>=remaining)continue;
                var item=OrderStock().OfType<StardewValley.Object>().Where(i=>!i.questItem.Value&&i.canBeShipped()&&OrderRules.Accepts(ship,i)&&DisposableStock(i.QualifiedItemId,i.Quality)>0).OrderByDescending(i=>i.sellToStorePrice()).FirstOrDefault();
                if(item==null)continue;
                int count=Math.Min(999,Math.Min(DisposableStock(item.QualifiedItemId,item.Quality),(int)Math.Min(999,ship.useShipmentValue.Value?(remaining-pending+Math.Max(1,item.sellToStorePrice())-1)/Math.Max(1,item.sellToStorePrice()):remaining-pending)));
                if(!PursuitMaterials(pursuit,new[]{(item.QualifiedItemId,count,item.Quality)},actions))return true;
                actions.Add(("player.ship_items",new{items=new[]{new{item=item.QualifiedItemId,count,quality=item.Quality}}}));QueuePursuit(pursuit,actions);return true;
            } else if(objective is GiftObjective gift) {
                if(policy.GiftValuePerDay<=policy.ReservedGiftValue)continue;
                foreach(var npc in Game1.characterData.Keys.Select(name=>Game1.getCharacterFromName(name)).Where(n=>n!=null&&!n.IsMonster&&!n.IsInvisible&&!n.isSleeping.Value&&n.currentLocation!=null)) {
                    var friendship=p.friendshipData.GetValueOrDefault(npc.Name);
                    if(friendship?.GiftsToday>0||friendship?.GiftsThisWeek>=2&&!npc.isBirthday()&&friendship?.IsMarried()!=true)continue;
                    if(npc.currentLocation!=Game1.currentLocation&&PlayerExecutor.NextExit(Game1.currentLocation,npc.currentLocation.NameOrUniqueName)==null)continue;
                    var candidate=p.Items.Select((item,slot)=>new{Item=item as StardewValley.Object,Slot=slot}).Where(c=>c.Item!=null&&!c.Item.bigCraftable.Value&&!c.Item.questItem.Value&&c.Item.QualifiedItemId is not ("(O)458" or "(O)460" or "(O)808" or "(O)809")&&!c.Item.GetContextTags().Any(t=>t.StartsWith("propose_roommate_"))&&OrderRules.Accepts(gift,c.Item)&&CanConsumeOne(c.Item))
                        .Select(c=>new{c.Item,c.Slot,Taste=npc.getGiftTasteForThisItem(c.Item),Value=Math.Max(0,c.Item!.sellToStorePrice())}).Where(c=>c.Taste is 0 or 2&&(c.Taste==0?GiftObjective.LikeLevels.Loved:GiftObjective.LikeLevels.Liked)>=gift.minimumLikeLevel.Value&&c.Value<=policy.GiftValueLimit&&c.Value<=policy.GiftValuePerDay-policy.ReservedGiftValue).OrderBy(c=>c.Value).FirstOrDefault();
                    if(candidate==null)continue;
                    if(QueuePursuit(pursuit,new[]{("player.social",(object)new{npc=npc.Name,mode="gift",slot=candidate.Slot,item=candidate.Item!.QualifiedItemId})}))policy.ReservedGiftValue+=candidate.Value;
                    return true;
                }
            } else if(objective is JKScoreObjective) {
                if(Game1.timeOfDay<1200||Game1.timeOfDay>2000)continue;
                QueuePursuit(pursuit,new[]{("player.arcade",(object)new{game="kart",mode="endless",target_score=objective.GetMaxCount(),seconds=1800,attempts=3})});return true;
            } else if(objective is ReachMineFloorObjective floor) {
                int travel=floor.skullCave.Value&&Game1.currentLocation.NameOrUniqueName is not ("Desert" or "SkullCave")&&Game1.currentLocation is not MineShaft?500:0;
                QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="mine_trip",region=floor.skullCave.Value?"skull":"normal",target_level=floor.GetMaxCount(),order_id=order.questKey.Value,objective=entry.Index,travel_budget=travel,keep_gold=policy.KeepGold,until=2100})},travel);return true;
            } else if(objective is SlayObjective) {
                int high=Math.Min(115,MineShaft.lowestLevelReached/5*5),start=(pursuit.Revision++%(high/5+1))*5;
                QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="mine_trip",start_level=start,target_level=Math.Min(120,start+10),order_id=order.questKey.Value,objective=entry.Index,until=2100})});return true;
            }
        }
        PursuitState(pursuit,"waiting","当前订单缺少可执行来源/条件或正在等出货结算；未覆盖的特殊限制和小游戏目标保留原生进度");return true;
    }
    private sealed class OrderCandidateCache {
        internal string Epoch="";internal int Day,Revision,Scan,Offset;
        internal string[] Catalog=Array.Empty<string>();internal List<string> Matches=new();
    }
    private readonly Dictionary<OrderObjective,OrderCandidateCache> orderCandidateCache=new();
    private IEnumerable<string> OrderMaterialCandidates(OrderObjective objective) {
        var seen=new HashSet<string>();
        foreach(var item in OrderStock())if(OrderRules.Accepts(objective,item)&&seen.Add(item.QualifiedItemId))yield return item.QualifiedItemId;
        if(!orderCandidateCache.TryGetValue(objective,out var cache)||cache.Epoch!=agentSaveEpoch||cache.Day!=Game1.Date.TotalDays||cache.Revision!=Knowledge.Revision) {
            if(orderCandidateCache.Count>32)orderCandidateCache.Clear();
            orderCandidateCache[objective]=cache=new(){Epoch=agentSaveEpoch,Day=Game1.Date.TotalDays,Revision=Knowledge.Revision,Catalog=ItemRegistry.GetObjectTypeDefinition().GetAllData().Select(d=>d.QualifiedItemId).ToArray()};
        }
        // Incremental catalog matching on the main thread; don't instantiate
        // every modded object on every decision tick. Actual stock is immediate.
        var clock=System.Diagnostics.Stopwatch.StartNew();int visited=0;
        while(cache.Scan<cache.Catalog.Length&&visited++<256&&clock.ElapsedMilliseconds<12) {
            string id=cache.Catalog[cache.Scan++];var item=ItemRegistry.Create(id);
            if(OrderRules.Accepts(objective,item))cache.Matches.Add(id);
        }
        int start=cache.Matches.Count==0?0:cache.Offset%cache.Matches.Count;
        cache.Offset=cache.Matches.Count==0?0:(start+48)%cache.Matches.Count;
        for(int i=0;i<cache.Matches.Count;i++){string id=cache.Matches[(start+i)%cache.Matches.Count];if(seen.Add(id))yield return id;}
    }
}
