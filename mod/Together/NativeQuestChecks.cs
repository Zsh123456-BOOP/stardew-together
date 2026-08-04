using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Quests;
using StardewValley.Monsters;

namespace Together;
public sealed partial class ModEntry {
    // Only called behind the AgentLab gate. This is a native-callback contract check,
    // not evidence that a human played a fishing minigame or that NPC loot earned credit.
    private string NativeQuestContracts() {
        var results=new List<object>();var p=Game1.player;
        var npc=FindCharacter("Leah");var wrongNpc=FindCharacter("Abigail");
        var delivery=new ItemDeliveryQuest("Leah","(O)24","同行交付检查","交付实际物品","交付防风草","收到了，谢谢。") {id={Value="together-lab-delivery"}};
        p.questLog.Add(delivery);
        bool rejectsItem=!delivery.OnItemOfferedToNpc(npc,ItemRegistry.Create("(O)390"),true);
        bool rejectsNpc=!delivery.OnItemOfferedToNpc(wrongNpc,ItemRegistry.Create("(O)24"),true);
        var produce=ItemRegistry.Create("(O)24");p.addItemToInventoryBool(produce);
        var held=p.Items.First(i=>i?.QualifiedItemId=="(O)24");int before=p.Items.Where(i=>i?.QualifiedItemId=="(O)24").Sum(i=>i.Stack);
        delivery.OnItemOfferedToNpc(npc,held);
        int after=p.Items.Where(i=>i?.QualifiedItemId=="(O)24").Sum(i=>i.Stack);
        results.Add(new{kind="delivery",rejects_wrong_item=rejectsItem,rejects_wrong_npc=rejectsNpc,completed=delivery.completed.Value,consumed=before-after});
        var resource=new ResourceCollectionQuest();resource.id.Value="together-lab-resource";resource.target.Value="Leah";resource.ItemId.Value="(O)378";resource.number.Value=2;resource.targetMessage.Value="收到了";
        p.questLog.Add(resource);resource.OnItemReceived(ItemRegistry.Create("(O)390"),1);int wrongResource=resource.numberCollected.Value;
        p.addItemToInventoryBool(ItemRegistry.Create("(O)378",2));
        int received=resource.numberCollected.Value;resource.OnNpcSocialized(npc);
        results.Add(new{kind="resource_collection",wrong_item_count=wrongResource,received_count=received,completed=resource.completed.Value});
        var fish=new FishingQuest("(O)145",1,"Leah","同行钓鱼检查","原生回调契约","收到了");fish.id.Value="together-lab-fish";p.questLog.Add(fish);
        fish.OnFishCaught("(O)128",1,10);int wrongFish=fish.numberFished.Value;
        // Inventory possession alone is deliberately not a fish-caught event.
        p.addItemToInventoryBool(ItemRegistry.Create("(O)145"));int inventoryOnly=fish.numberFished.Value;
        fish.OnFishCaught("(O)145",1,10);fish.OnNpcSocialized(npc);
        results.Add(new{kind="fishing",wrong_fish_count=wrongFish,inventory_only_count=inventoryOnly,native_event_count=fish.numberFished.Value,completed=fish.completed.Value});
        var combat=new SlayMonsterQuest();combat.id.Value="together-lab-combat";combat.monsterName.Value="Green Slime";combat.numberToKill.Value=1;combat.target.Value="null";p.questLog.Add(combat);
        combat.OnMonsterSlain(Game1.currentLocation,new Monster("Bat",Vector2.Zero),false,false);int wrongMonster=combat.numberKilled.Value;
        combat.OnMonsterSlain(Game1.currentLocation,new Monster("Green Slime",Vector2.Zero),false,false);
        results.Add(new{kind="combat",wrong_monster_count=wrongMonster,native_event_count=combat.numberKilled.Value,completed=combat.completed.Value});
        foreach(var q in new Quest[]{delivery,resource,fish,combat})p.questLog.Remove(q);
        Game1.exitActiveMenu();
        return JsonSerializer.Serialize(new{scope="native game event contracts, with explicit lab fixtures; not a player gameplay recording",results});
    }
}
