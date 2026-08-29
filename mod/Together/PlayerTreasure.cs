using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace Together;
public sealed partial class PlayerExecutor {
    private Chest? treasureChest;
    private Point treasureTile;
    private Dictionary<string,int> treasureInventory=new(),treasureExpected=new();
    private int[] treasureSpecial=Array.Empty<int>();
    private int treasureStamina,treasureClicks;
    private DateTime treasureDeadline;
    internal static IEnumerable<(Point Tile,Chest Chest)> TreasureChests(GameLocation location)=>location.overlayObjects.Select(p=>(p.Key,p.Value)).Concat(location.objects.Pairs.Select(p=>(p.Key,p.Value))).Where(p=>p.Value is Chest c&&!c.playerChest.Value&&c.GetItemsForPlayer().Any(i=>i!=null)).Select(p=>(p.Key.ToPoint(),(Chest)p.Value));
    internal static bool SpecialRewardPresent(int which)=>which switch{4=>Game1.player.hasSkullKey,6=>Game1.player.hasDarkTalisman,7=>Game1.player.hasMagicInk,5=>Game1.player.hasMagnifyingGlass,3=>Game1.player.hasSpecialCharm,_=>false};
    private void StartTreasure(IEnumerable<(Point Tile,Chest Chest)>? candidates=null) {
        treasureChest=null;treasureClicks=0;
        foreach(var chest in (candidates??TreasureChests(Game1.currentLocation)).OrderBy(c=>Vector2.DistanceSquared(c.Tile.ToVector2(),Game1.player.Tile)))try{Walk(Approach(chest.Tile,true));treasureTile=chest.Tile;treasureChest=chest.Chest;break;}catch(InvalidOperationException){}
        if(treasureChest==null)throw new InvalidOperationException("no_reachable_native_treasure_chest");
        var items=treasureChest.GetItemsForPlayer().Where(i=>i!=null).ToArray();
        treasureSpecial=items.OfType<SpecialItem>().Select(i=>i.which.Value).ToArray();
        treasureExpected=items.Where(i=>i is not SpecialItem).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));
        if(Game1.player.Items.Count(i=>i==null)<items.Count(i=>i is not SpecialItem&&i.QualifiedItemId!="(O)434"))throw new InvalidOperationException("treasure_return_inventory_space_required");
        treasureInventory=Game1.player.Items.Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));treasureStamina=Game1.player.MaxStamina;
        Current!.phase="treasure_walk";treasureDeadline=DateTime.UtcNow.AddSeconds(90);
    }
    private void TickTreasure() {
        if(Game1.currentLocation.NameOrUniqueName!=origin)throw new InvalidOperationException("treasure_location_changed");
        if(DateTime.UtcNow>treasureDeadline)throw new InvalidOperationException("native_treasure_timeout");
        if(Game1.activeClickableMenu is ItemGrabMenu menu) {
            if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(180);
            var step=NativeRewards.Step(menu);Current!.effects.Add(step.Evidence);return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("treasure_unexpected_menu");
        if(!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(treasureChest==null)throw new InvalidOperationException("treasure_reference_missing");
        if(Current!.phase=="treasure_opening"&&!treasureChest.GetItemsForPlayer().Any(i=>i!=null)) {
            bool verified=treasureSpecial.All(SpecialRewardPresent)&&treasureExpected.All(e=>e.Key=="(O)434"?Game1.player.MaxStamina>treasureStamina:Game1.player.Items.Where(i=>i?.QualifiedItemId==e.Key).Sum(i=>i.Stack)-treasureInventory.GetValueOrDefault(e.Key)>=e.Value);
            if(!verified){if(DateTime.UtcNow<nextInteraction)return;throw new InvalidOperationException("treasure_removed_but_native_reward_not_verified");}
            Current.effects.Add(new{kind="native_treasure_reward",tile=treasureTile,items=treasureExpected,special=treasureSpecial.Select(which=>new{which,present=SpecialRewardPresent(which)}),stamina_before=treasureStamina,stamina_after=Game1.player.MaxStamina});Current.completed=1;Finish("succeeded");return;
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Adjacent(treasureTile);Face(treasureTile);
        if(DateTime.UtcNow<nextInteraction)return;
        if(++treasureClicks>4)throw new InvalidOperationException("native_treasure_open_did_not_finish");
        NativeMenuInput.InteractWorld(treasureTile);Current.phase="treasure_opening";nextInteraction=DateTime.UtcNow.AddSeconds(3);
    }
}
