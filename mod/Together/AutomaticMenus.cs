using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class ModEntry {
    private DateTime automaticMenuAt;
    private bool TickAutomaticMenus() {
        if(playerExecutor.Busy)return false;
        if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} dialogue) {
            if(DateTime.UtcNow<automaticMenuAt)return true;automaticMenuAt=DateTime.UtcNow.AddMilliseconds(400);
            Data.Autoplay.Record("native_notice",AgentJson.Encode(new{text=dialogue.getCurrentString(),event_active=Game1.eventUp}));
            dialogue.finishTyping();dialogue.receiveLeftClick(dialogue.xPositionOnScreen+16,dialogue.yPositionOnScreen+16);return true;
        }
        if(Game1.activeClickableMenu is LevelUpMenu {isProfessionChooser:true} chooser&&ApplyProfessionPolicy(chooser))return true;
        if(Game1.activeClickableMenu is LevelUpMenu {isProfessionChooser:false,isActive:true} level&&level.CanReceiveInput()) {
            level.okButtonClicked();Data.Autoplay.Record("native_notice","已原生确认无分支技能升级");automaticMenuAt=DateTime.UtcNow.AddMilliseconds(400);return true;
        }
        return false;
    }
}
