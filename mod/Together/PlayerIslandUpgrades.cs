using System.Text.Json;
using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private string islandUpgrade="";
    private int islandNutBudget,islandNutKeep,islandNutsBefore,islandNutCost;
    private bool islandPerchApproached,islandPurchaseSent;
    internal static IEnumerable<(IslandLocation Location,ParrotUpgradePerch Perch)> IslandPerches()=>Game1.locations.OfType<IslandLocation>().SelectMany(l=>l.parrotUpgradePerches.Select(p=>(l,p)));
    internal static object ReadIslandUpgrades()=>new{walnuts=Game1.netWorldState.Value.GoldenWalnuts,found=Game1.netWorldState.Value.GoldenWalnutsFound,upgrades=IslandPerches().Select(p=>new{location=p.Location.NameOrUniqueName,id=p.Perch.upgradeName.Value,tile=p.Perch.tilePosition.Value,cost=p.Perch.requiredNuts.Value,requires_mail=p.Perch.requiredMail.Value,available=p.Perch.IsAvailable(),state=p.Perch.currentState.Value.ToString(),special_payment=p.Perch.upgradeName.Value=="GoldenParrot"}),note="原生已加载地图上的鹦鹉建设；核桃收集与剧情/谜题进度另行执行"};
    private void StartIslandUpgrade(JsonElement args) {
        destination=AgentToolRegistry.Text(args,"location",origin);islandUpgrade=AgentToolRegistry.Text(args,"upgrade");islandNutBudget=AgentToolRegistry.Number(args,"budget_nuts",0);islandNutKeep=AgentToolRegistry.Number(args,"keep_nuts",0);
        if(islandNutBudget<0||islandNutKeep<0||islandUpgrade.Length==0||islandUpgrade=="GoldenParrot")throw new InvalidOperationException("observed_walnut_upgrade_and_explicit_budget_required");
        var perch=FindIslandPerch();if(perch.currentState.Value==ParrotUpgradePerch.UpgradeState.Complete){Finish("succeeded");return;}
        if(!perch.IsAvailable()||perch.currentState.Value!=ParrotUpgradePerch.UpgradeState.Idle)throw new InvalidOperationException("native_island_upgrade_unavailable_or_building");
        islandNutCost=perch.requiredNuts.Value;CheckIslandBudget();islandPurchaseSent=islandPerchApproached=false;Current!.phase="island_upgrade_travel";
    }
    private ParrotUpgradePerch FindIslandPerch()=>IslandPerches().Where(p=>p.Location.NameOrUniqueName==destination&&p.Perch.upgradeName.Value==islandUpgrade).Select(p=>p.Perch).SingleOrDefault()??throw new InvalidOperationException("observed_island_perch_not_found");
    private void CheckIslandBudget(){if(islandNutCost>islandNutBudget||Game1.netWorldState.Value.GoldenWalnuts-islandNutCost<islandNutKeep)throw new InvalidOperationException("island_walnut_budget_insufficient");}
    private void TickIslandUpgrade() {
        var perch=FindIslandPerch();
        if(islandPurchaseSent&&perch.currentState.Value==ParrotUpgradePerch.UpgradeState.Complete) {
            if(islandNutsBefore-Game1.netWorldState.Value.GoldenWalnuts!=islandNutCost)throw new InvalidOperationException("native_island_walnut_payment_not_verified");
            Current!.effects.Add(new{kind="native_island_upgrade",location=destination,upgrade=islandUpgrade,cost=islandNutCost,state=perch.currentState.Value.ToString()});Current.completed=1;Finish("succeeded");return;
        }
        if(Game1.locationRequest!=null||Game1.fadeToBlack)return;
        if(Game1.activeClickableMenu is DialogueBox dialogue) {
            if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(300);dialogue.finishTyping();
            if(!dialogue.isQuestion||Game1.currentLocation.lastQuestionKey!="UpgradePerch_"+islandUpgrade||Game1.currentLocation.NameOrUniqueName!=destination)throw new InvalidOperationException("native_island_upgrade_question_changed");
            if(islandPurchaseSent)throw new InvalidOperationException("native_island_upgrade_confirmation_repeated");CheckIslandBudget();
            int index=Array.FindIndex(dialogue.responses,r=>r.responseKey=="Yes");if(index<0||dialogue.responseCC==null||index>=dialogue.responseCC.Count)throw new InvalidOperationException("native_island_upgrade_yes_unavailable");
            islandNutsBefore=Game1.netWorldState.Value.GoldenWalnuts;islandPurchaseSent=true;NativeMenuInput.ClickMenu(dialogue,dialogue.responseCC[index].bounds);Current!.phase="island_upgrade_building";return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("island_upgrade_menu_interrupted");
        if(islandPurchaseSent||!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(!islandPerchApproached){Walk(Approach(perch.tilePosition.Value,true));islandPerchApproached=true;}
        if(!AtWalkTarget){MonitorWalk();return;}
        StopWalk();Adjacent(perch.tilePosition.Value);Face(perch.tilePosition.Value);CheckIslandBudget();islandNutsBefore=Game1.netWorldState.Value.GoldenWalnuts;
        NativeMenuInput.InteractWorld(perch.tilePosition.Value);
        if(Game1.activeClickableMenu==null){islandPurchaseSent=true;Current!.phase="island_upgrade_building";}
    }
}
