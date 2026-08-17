using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private CarpenterMenu? constructionMenu;
    private CarpenterMenu.BlueprintEntry? constructionBlueprint;
    private Point constructionSite;
    private int constructionBudget,constructionReserve,constructionMoney;
    private Dictionary<string,int> constructionStock=new();
    private HashSet<Building> constructionExisting=new();
    private bool constructionClickSent;
    internal static object ReadConstruction(JsonElement args) {
        if(Game1.activeClickableMenu is not CarpenterMenu menu)throw new InvalidOperationException("native_carpenter_menu_required");
        string id=AgentToolRegistry.Text(args,"blueprint");
        var chosen=menu.Blueprints.FirstOrDefault(b=>b.Id==id);
        return new{menu.Builder,location=menu.TargetLocation.NameOrUniqueName,blueprints=menu.Blueprints.Select(b=>new{b.Id,b.DisplayName,b.BuildCost,b.BuildDays,b.IsUpgrade,b.UpgradeFrom,materials=b.BuildMaterials,size=new[]{b.TilesWide,b.TilesHigh}}),
            sites=chosen==null?null:BuildingPlanning.Sites(menu,chosen).Select(p=>new{x=p.X,y=p.Y}),note="选址保护现有作物/设备，保留出入口与建筑门的连通；仍由原生建造流程做最终校验。"};
    }
    private void StartConstruction(JsonElement args) {
        if(Game1.activeClickableMenu is not CarpenterMenu {onFarm:false,readOnly:false} menu)throw new InvalidOperationException("native_carpenter_selection_menu_required");
        constructionBlueprint=menu.Blueprints.FirstOrDefault(b=>b.Id==AgentToolRegistry.Text(args,"blueprint"))??throw new InvalidOperationException("observed_blueprint_required");
        constructionBudget=AgentToolRegistry.Number(args,"budget",0);constructionReserve=AgentToolRegistry.Number(args,"keep_gold",500);
        if(constructionBudget<0||constructionReserve<0)throw new InvalidOperationException("invalid_build_budget");
        constructionMenu=menu;menu.SetNewActiveBlueprint(constructionBlueprint);
        CheckConstructionCost();
        var sites=BuildingPlanning.Sites(menu,constructionBlueprint);
        if(sites.Count==0)throw new InvalidOperationException("no_clear_accessible_building_site_or_upgrade_target");
        constructionSite=sites[0];constructionClickSent=false;
        constructionExisting=menu.TargetLocation.buildings.ToHashSet();constructionStock=Game1.player.Items.Where(i=>i!=null).GroupBy(i=>i.QualifiedItemId).ToDictionary(g=>g.Key,g=>g.Sum(i=>i.Stack));constructionMoney=Game1.player.Money;
        var button=menu.okButton.bounds;menu.receiveLeftClick(button.Center.X,button.Center.Y);Current!.phase="construction_view";
    }
    private void CheckConstructionCost() {
        var menu=constructionMenu!;var b=constructionBlueprint!;
        if(menu.Blueprint!=b||b.BuildCost>constructionBudget||Game1.player.Money-b.BuildCost<constructionReserve||!menu.CanBuildCurrentBlueprint())throw new InvalidOperationException("construction_conditions_or_budget_changed");
        var used=new Dictionary<Item,int>();
        foreach(var ingredient in menu.ingredients) {
            int remaining=ingredient.Stack;
            foreach(var item in Game1.player.Items.Where(i=>i?.QualifiedItemId==ingredient.QualifiedItemId)) {
                int take=Math.Min(item.Stack-used.GetValueOrDefault(item),remaining);if(take>0){used[item]=used.GetValueOrDefault(item)+take;remaining-=take;}
                if(remaining==0)break;
            }
            if(remaining>0)throw new InvalidOperationException("construction_material_missing");
        }
        ValidateConsumption?.Invoke(used,"","");
    }
    private void TickConstruction() {
        var menu=constructionMenu!;var b=constructionBlueprint!;
        if(Game1.IsFading()||Game1.locationRequest!=null)return;
        if(Current!.phase=="construction_result") {
            if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} dialogue) {
                if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(400);dialogue.finishTyping();dialogue.receiveLeftClick(dialogue.xPositionOnScreen+16,dialogue.yPositionOnScreen+16);return;
            }
            if(Game1.activeClickableMenu==null){Finish("succeeded");return;}
            if(Game1.activeClickableMenu!=menu)throw new InvalidOperationException("construction_result_menu_requires_review");
            return;
        }
        if(Game1.activeClickableMenu!=menu)throw new InvalidOperationException("construction_menu_changed");
        var built=menu.TargetLocation.buildings.FirstOrDefault(x=>x.tileX.Value==constructionSite.X&&x.tileY.Value==constructionSite.Y&&
            (b.IsUpgrade?(x.upgradeName.Value==b.Id&&x.daysUntilUpgrade.Value>0||x.buildingType.Value==b.Id):!constructionExisting.Contains(x)&&x.buildingType.Value==b.Id));
        if(built!=null) {
            var spent=menu.ingredients.Select(i=>new{item=i.QualifiedItemId,expected=i.Stack,actual=constructionStock.GetValueOrDefault(i.QualifiedItemId)-Game1.player.Items.Where(x=>x?.QualifiedItemId==i.QualifiedItemId).Sum(x=>x.Stack)}).ToArray();
            if(constructionMoney-Game1.player.Money!=b.BuildCost||spent.Any(s=>s.expected!=s.actual))throw new InvalidOperationException("native_construction_consumption_not_verified");
            Current.effects.Add(new{kind="native_construction_started",blueprint=b.Id,tile=constructionSite,cost=b.BuildCost,spent,days=Math.Max(built.daysOfConstructionLeft.Value,built.daysUntilUpgrade.Value),note="真实开工，不把尚未结束的工期记作建成"});Current.completed=1;Current.phase="construction_result";return;
        }
        if(constructionClickSent)return;
        if(!menu.onFarm||menu.freeze||Game1.currentLocation!=menu.TargetLocation)return;
        int sx=constructionSite.X*64+32-Game1.viewport.X,sy=constructionSite.Y*64+32-Game1.viewport.Y;
        if(sx<128||sx>Game1.viewport.Width-192||sy<128||sy>Game1.viewport.Height-192) {
            Game1.panScreen(sx<128?-16:sx>Game1.viewport.Width-192?16:0,sy<128?-16:sy>Game1.viewport.Height-192?16:0);return;
        }
        CheckConstructionCost();
        if(!b.IsUpgrade&&!BuildingPlanning.Clear(menu.TargetLocation,b,constructionSite))throw new InvalidOperationException("building_site_changed");
        string command=Current.command_id;constructionClickSent=true;
        // Acquire the native mutex first. The nested menu RequestLock then calls
        // its action synchronously inside our scoped mouse input, not on a later frame.
        Game1.player.team.buildLock.RequestLock(()=>{
            try {
                if(!Busy||Current?.command_id!=command||Game1.activeClickableMenu!=menu)return;
                CheckConstructionCost();NativeMenuInput.ClickWorld(menu,constructionSite);
                if(!menu.freeze)Finish("failed","native_construction_rejected");
            }catch(Exception e){if(Current?.command_id==command)Finish("failed",e.Message);}
            finally {if(Game1.player.team.buildLock.IsLockHeld())Game1.player.team.buildLock.ReleaseLock();}
        },()=>{if(Busy&&Current?.command_id==command)Finish("failed","native_construction_lock_busy");});
        Current.phase="construction_wait";
    }
}
