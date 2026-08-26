using StardewValley;
using StardewValley.Monsters;
using StardewValley.SpecialOrders;
using StardewValley.SpecialOrders.Objectives;

namespace Together;
internal static class OrderRules {
    internal static SpecialOrder? Find(string id)=>Game1.player.team.specialOrders.FirstOrDefault(o=>o.questKey.Value==id&&o.questState.Value==SpecialOrderStatus.InProgress);
    internal static bool Tags(IEnumerable<string> sets,Item item,bool empty=false) {
        var rules=sets.ToArray();return rules.Length==0?empty:rules.Any(set=>set.Split(',').All(group=>ItemContextTagManager.DoAnyTagsMatch(group.Split('/'),item.GetContextTags())));
    }
    internal static bool Accepts(OrderObjective objective,Item item)=>objective switch {
        DonateObjective o=>o.IsValidItem(item),CollectObjective o=>Tags(o.acceptableContextTagSets,item),
        FishObjective o=>Tags(o.acceptableContextTagSets,item),ShipObjective o=>Tags(o.acceptableContextTagSets,item),
        DeliverObjective o=>Tags(o.acceptableContextTagSets,item,true),GiftObjective o=>Tags(o.acceptableContextTagSets,item,true),_=>false
    };
    internal static bool Matches(SlayObjective o,Monster monster)=>!(o.ignoreFarmMonsters.Value&&monster.currentLocation?.Name=="Farm")&&o.targetNames.Any(monster.Name.Contains);
    internal static object Describe(OrderObjective o,int index)=>new {
        index,type=o.GetType().Name,current=o.GetCount(),required=o.GetMaxCount(),complete=o.IsComplete(),fail_on_completion=o.failOnCompletion.Value,description=o.GetDescription(),
        conditions=o switch {
            DonateObjective d=>(object)new{dropbox=d.dropBox.Value,location=d.GetDropboxLocationName(),tags=d.acceptableContextTagSets.ToArray(),confirmed=d.confirmed.Value},
            DeliverObjective d=>new{npc=d.targetName.Value,tags=d.acceptableContextTagSets.ToArray(),single_stack_required=true},
            CollectObjective d=>new{tags=d.acceptableContextTagSets.ToArray(),fresh_collection=true},
            FishObjective d=>new{tags=d.acceptableContextTagSets.ToArray(),native_catch=true},
            ShipObjective d=>new{tags=d.acceptableContextTagSets.ToArray(),value=d.useShipmentValue.Value,overnight=true},
            SlayObjective d=>new{names=d.targetNames.ToArray(),ignore_farm=d.ignoreFarmMonsters.Value},
            GiftObjective d=>new{tags=d.acceptableContextTagSets.ToArray(),minimum_taste=d.minimumLikeLevel.Value.ToString()},
            ReachMineFloorObjective d=>new{region=d.skullCave.Value?"skull":"normal"},_=>new{}
        }
    };
}
