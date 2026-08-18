using System.Text.Json;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private int houseLevel,housePrice,houseMaterialCount,houseMoney,houseStock,houseKeep;
    private string houseMaterial="";
    private void StartHouseUpgrade(JsonElement args) {
        houseLevel=Game1.player.HouseUpgradeLevel;
        if(houseLevel is <0 or >2||Game1.player.daysUntilHouseUpgrade.Value>=0)throw new InvalidOperationException("house_upgrade_unavailable_or_in_progress");
        (housePrice,houseMaterial,houseMaterialCount)=houseLevel switch{0=>(10000,"(O)388",450),1=>(65000,"(O)709",100),_=>(100000,"",0)};
        houseKeep=AgentToolRegistry.Number(args,"keep_gold",500);
        if(houseKeep<0||housePrice>AgentToolRegistry.Number(args,"budget",0))throw new InvalidOperationException("house_upgrade_budget_insufficient");
        CheckHouseUpgradeCost();houseMoney=Game1.player.Money;houseStock=Game1.player.Items.Where(i=>i?.QualifiedItemId==houseMaterial).Sum(i=>i.Stack);
        StartService(JsonSerializer.SerializeToElement(new{location="ScienceHouse",service="upgrade_house"}));
    }
    private void CheckHouseUpgradeCost() {
        if(Game1.player.HouseUpgradeLevel!=houseLevel||Game1.player.daysUntilHouseUpgrade.Value>=0||Game1.player.Money-housePrice<houseKeep)throw new InvalidOperationException("house_upgrade_state_or_money_changed");
        var used=new Dictionary<Item,int>();int needed=houseMaterialCount;
        foreach(var item in Game1.player.Items.Where(i=>i?.QualifiedItemId==houseMaterial)) {int take=Math.Min(needed,item.Stack);if(take>0)used[item]=take;needed-=take;}
        if(needed>0)throw new InvalidOperationException("house_upgrade_material_missing");ValidateConsumption?.Invoke(used,"","");
    }
    private void TickHouseUpgrade() {
        if(Current!.phase=="house_result") {
            if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} dialogue) {
                if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(400);dialogue.finishTyping();dialogue.receiveLeftClick(dialogue.xPositionOnScreen+16,dialogue.yPositionOnScreen+16);return;
            }
            if(Game1.activeClickableMenu==null){Finish("succeeded");return;}
            throw new InvalidOperationException("house_upgrade_result_requires_review");
        }
        if(Current.phase!="house_confirm"){TickService();return;}
        if(Game1.activeClickableMenu is not DialogueBox {isQuestion:true} question||Game1.currentLocation.lastQuestionKey!="upgrade")throw new InvalidOperationException("native_house_upgrade_offer_required");
        CheckHouseUpgradeCost();question.finishTyping();int index=Array.FindIndex(question.responses,r=>r.responseKey=="Yes");
        if(index<0||question.responseCC==null||index>=question.responseCC.Count)throw new InvalidOperationException("house_upgrade_confirmation_unavailable");
        var bounds=question.responseCC[index].bounds;question.performHoverAction(bounds.Center.X,bounds.Center.Y);question.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
        int consumed=houseStock-Game1.player.Items.Where(i=>i?.QualifiedItemId==houseMaterial).Sum(i=>i.Stack);
        if(Game1.player.daysUntilHouseUpgrade.Value<=0||houseMoney-Game1.player.Money!=housePrice||consumed!=houseMaterialCount)throw new InvalidOperationException("native_house_upgrade_not_verified");
        Current.effects.Add(new{kind="native_house_upgrade_started",from=houseLevel,to=houseLevel+1,cost=housePrice,material=houseMaterial,consumed,days=Game1.player.daysUntilHouseUpgrade.Value});Current.completed=1;Current.phase="house_result";
    }
}
