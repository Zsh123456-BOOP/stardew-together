using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class ModEntry {
    private DateTime automaticMenuAt;
    private bool NeedsAgentMenuDecision=>AgentDecisionPacing.MenuNeedsReview(playerExecutor.Busy,playerExecutor.NeedsMenuChoice,Game1.activeClickableMenu!=null);
    private void CloseShopForDeparture(ScheduledAgentTask task) {
        if(task.spec.actor!="player"||task.spec.tool!="player.travel"||playerExecutor.Busy||Game1.activeClickableMenu is not ShopMenu shop||shop.heldItem!=null||!shop.readyToClose())return;
        // A queued departure already expresses the decision to leave. Preserve all
        // native held-item/close checks; never dismiss transaction intermediates.
        new NativeMenuTools().Close();
        Data.Autoplay.Record("shop_closed_for_departure",AgentJson.Encode(new{task.spec.id,task.spec.intent_id,destination=AgentToolRegistry.Text(task.spec.args,"location"),closed=Game1.activeClickableMenu==null}));
    }
    private bool TickAutomaticMenus() {
        if(playerExecutor.Busy)return false;
        if(ApplyFamilyNightPolicy())return true;
        if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} dialogue) {
            if(DateTime.UtcNow<automaticMenuAt)return true;automaticMenuAt=DateTime.UtcNow.AddMilliseconds(400);
            Data.Autoplay.Record("native_notice",AgentJson.Encode(new{text=dialogue.getCurrentString(),event_active=Game1.eventUp}));
            dialogue.finishTyping();dialogue.receiveLeftClick(dialogue.xPositionOnScreen+16,dialogue.yPositionOnScreen+16);return true;
        }
        if(Game1.activeClickableMenu is LevelUpMenu {isProfessionChooser:true} chooser&&(ApplyProfessionPolicy(chooser)||SurvivalProfession(chooser)))return true;
        if(Game1.activeClickableMenu is LevelUpMenu {isProfessionChooser:false,isActive:true} level&&level.CanReceiveInput()) {
            level.okButtonClicked();Data.Autoplay.Record("native_notice","已原生确认无分支技能升级");automaticMenuAt=DateTime.UtcNow.AddMilliseconds(400);return true;
        }
        return false;
    }
}
