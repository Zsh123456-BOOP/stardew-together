using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private static readonly string[] JojaProjects={"ccVault","ccBoilerRoom","ccCraftsRoom","ccPantry","ccFishTank"};
    private string jojaMode="",jojaFlag="";
    private int jojaBudget,jojaKeep,jojaMoney,jojaCost,jojaPages,jojaVisits;
    private Point? jojaCounter;
    internal static object ReadJoja() {
        var menu=Game1.activeClickableMenu as JojaCDMenu;
        return new{member=Game1.player.mailReceived.Contains("JojaMember"),membership_pending=Game1.player.hasOrWillReceiveMail("JojaMember"),community_route_completed=Game1.player.mailReceived.Contains("ccIsComplete"),projects=JojaProjects.Select((id,index)=>new{id,completed_or_pending=Utility.doesAnyFarmerHaveOrWillReceiveMail(id),cost=menu?.getPriceFromButtonNumber(index),available=menu?.checkboxes[index].name!="complete"&&menu!=null}),cinema_pending=Game1.player.hasOrWillReceiveMail("ccMovieTheater"),cinema_joja_available=Game1.player.eventsSeen.Contains("502261"),note="加入Joja会改变本存档路线；须由既定路线策略选择，单档不能假装同时完成互斥路线。"};
    }
    private void StartJoja(JsonElement args) {
        if(AgentToolRegistry.Text(args,"route")!="joja")throw new InvalidOperationException("explicit_joja_route_required");
        jojaMode=AgentToolRegistry.Text(args,"mode","project");jojaBudget=AgentToolRegistry.Number(args,"budget",0);jojaKeep=AgentToolRegistry.Number(args,"keep_gold",500);
        if(jojaBudget<0||jojaKeep<0||jojaMode is not ("membership" or "project" or "cinema"))throw new InvalidOperationException("invalid_joja_operation");
        jojaFlag=jojaMode=="membership"?"JojaMember":jojaMode=="cinema"?"ccMovieTheater":AgentToolRegistry.Text(args,"project");
        if(jojaMode=="project"&&!JojaProjects.Contains(jojaFlag))throw new InvalidOperationException("observed_joja_project_required");
        if(Utility.doesAnyFarmerHaveOrWillReceiveMail(jojaFlag)){Current!.effects.Add(new{kind="joja_already_completed_or_pending",flag=jojaFlag});Finish("succeeded");return;}
        if(jojaMode=="membership"&&(!Game1.player.eventsSeen.Contains("611439")||Game1.player.mailReceived.Contains("ccIsComplete")))throw new InvalidOperationException("native_joja_membership_unlock_unavailable");
        if(jojaMode!="membership"&&!Game1.player.mailReceived.Contains("JojaMember"))throw new InvalidOperationException("joja_membership_not_yet_effective");
        if(jojaMode=="cinema"&&!Game1.player.eventsSeen.Contains("502261"))throw new InvalidOperationException("joja_cinema_unlock_unavailable");
        if(jojaMode=="project"&&JojaProjects.Any(id=>Game1.player.mailForTomorrow.Any(m=>m.StartsWith("joja"+id[2..],StringComparison.Ordinal))))throw new InvalidOperationException("joja_previous_project_waits_for_tomorrow");
        jojaCost=jojaMode=="membership"?5000:jojaMode=="cinema"?500000:0;
        if(jojaCost>jojaBudget||Game1.player.Money-jojaCost<jojaKeep)throw new InvalidOperationException("joja_budget_insufficient");
        jojaMoney=Game1.player.Money;jojaPages=jojaVisits=0;jojaCounter=null;destination="JojaMart";Current!.phase="joja_travel";
    }
    private void TickJoja() {
        if(Game1.locationRequest!=null||Game1.fadeToBlack)return;
        if(Utility.doesAnyFarmerHaveOrWillReceiveMail(jojaFlag)) {
            if(Current!.phase!="joja_result") {
                if(jojaMoney-Game1.player.Money!=jojaCost)throw new InvalidOperationException("native_joja_payment_not_verified");
                Current.effects.Add(new{kind="native_joja_purchase",flag=jojaFlag,cost=jojaCost,note="原生申请已登记，施工/区域解锁仍等待次日实际状态"});Current.completed=1;Current.phase="joja_result";
            }
            if(Game1.activeClickableMenu==null){Finish("succeeded");return;}
            if(Game1.activeClickableMenu is JojaCDMenu)return; // Native one-second close timer.
        }
        if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(300);
        if(Game1.activeClickableMenu is JojaCDMenu development) {
            if(jojaMode!="project")throw new InvalidOperationException("joja_unexpected_development_menu");
            int index=Array.IndexOf(JojaProjects,jojaFlag);jojaCost=development.getPriceFromButtonNumber(index);
            if(development.checkboxes[index].name=="complete")throw new InvalidOperationException("joja_project_state_changed");
            if(jojaCost<0||jojaCost>jojaBudget||Game1.player.Money-jojaCost<jojaKeep)throw new InvalidOperationException("joja_project_budget_insufficient");
            jojaMoney=Game1.player.Money;
            var bounds=development.checkboxes[index].bounds;development.receiveLeftClick(bounds.Center.X,bounds.Center.Y);return;
        }
        if(Game1.activeClickableMenu is DialogueBox dialogue) {
            if(++jojaPages>30)throw new InvalidOperationException("joja_dialogue_limit");dialogue.finishTyping();
            if(!dialogue.isQuestion){dialogue.receiveLeftClick(dialogue.xPositionOnScreen+16,dialogue.yPositionOnScreen+16);return;}
            if(Game1.currentLocation.Name!="JojaMart"||Current!.phase=="joja_result")throw new InvalidOperationException("joja_dialogue_changed");
            if(jojaMode=="project"&&Game1.player.eventsSeen.Contains("502261"))throw new InvalidOperationException("joja_projects_finished_cinema_requires_separate_budget");
            int index=Game1.currentLocation.lastQuestionKey=="JojaSignUp"?Array.FindIndex(dialogue.responses,r=>r.responseKey=="Yes"):0;
            if(Game1.currentLocation.lastQuestionKey!="JojaSignUp"&&dialogue.characterDialogue?.speaker?.Name!="Morris")throw new InvalidOperationException("joja_question_speaker_changed");
            if(jojaMode=="membership"&&jojaCost!=5000||jojaMode=="cinema"&&jojaCost!=500000||jojaCost>jojaBudget||Game1.player.Money-jojaCost<jojaKeep)throw new InvalidOperationException("joja_confirmation_budget_changed");
            if(index<0||dialogue.responseCC==null||index>=dialogue.responseCC.Count)throw new InvalidOperationException("native_joja_choice_unavailable");
            jojaMoney=Game1.player.Money;
            var bounds=dialogue.responseCC[index].bounds;dialogue.performHoverAction(bounds.Center.X,bounds.Center.Y);dialogue.receiveLeftClick(bounds.Center.X,bounds.Center.Y);return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("joja_menu_requires_review");
        if(!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(jojaCounter==null) {
            var location=Game1.currentLocation;
            for(int y=0;y<location.Map.Layers[0].LayerHeight&&jojaCounter==null;y++)for(int x=0;x<location.Map.Layers[0].LayerWidth;x++)if(location.doesTileHaveProperty(x,y,"Action","Buildings")=="JoinJoja")try{var tile=new Point(x,y);Walk(Approach(tile,true));jojaCounter=tile;break;}catch(InvalidOperationException){}
            if(jojaCounter==null)throw new InvalidOperationException("joja_counter_unreachable");
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Adjacent(jojaCounter.Value);Face(jojaCounter.Value);
        if(++jojaVisits>3)throw new InvalidOperationException("joja_offer_not_available_today");
        NativeMenuInput.InteractWorld(jojaCounter.Value);Current!.phase="joja_offer";
    }
}
