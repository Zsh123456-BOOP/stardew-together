using StardewValley;
using StardewValley.Locations;

namespace Together;
internal static class NativeFoodRules {
    internal static string? Block(Item item) {
        var p=Game1.player;
        if(item is not StardewValley.Object food||food.Edibility<=0||food.questItem.Value||food.QualifiedItemId=="(O)434")return "item_not_ordinary_food";
        // Match Game1's actual interaction restrictions before eatHeldObject;
        // that lower-level animation method alone does not enforce them.
        if(p.team.SpecialOrderRuleActive("SC_NO_FOOD")&&p.currentLocation is MineShaft mine&&mine.getMineArea()==121)return "native_skull_order_forbids_food";
        if(p.hasBuff("25")&&!food.HasContextTag("ginger_item"))return "native_nausea_requires_ginger";
        return null;
    }
}
