using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Objects;

namespace Together;
public sealed partial class PlayerExecutor {
    private Point? informationTile;
    private TV? informationTV;
    private string informationChannel="",informationLetter="";
    private int informationLimit,informationPages;
    private HashSet<string> informationRecipes=new();
    private static HashSet<string> KnownRecipeKeys()=>Game1.player.cookingRecipes.Keys.Select(k=>"cook:"+k).Concat(Game1.player.craftingRecipes.Keys.Select(k=>"craft:"+k)).ToHashSet();
    private void StartInformation(JsonElement args) {
        informationTile=null;informationTV=null;informationLetter="";informationPages=0;informationRecipes=KnownRecipeKeys();
        informationLimit=AgentToolRegistry.Number(args,"count",20);
        if(informationLimit is <1 or >40)throw new InvalidOperationException("invalid_mail_batch_limit");
        informationChannel=AgentToolRegistry.Text(args,"channel","cooking") switch{"cooking"=>"The","weather"=>"Weather","fortune"=>"Fortune","tips"=>"Livin'","fishing"=>"Fishing",_=>throw new InvalidOperationException("unknown_tv_channel")};
        destination=Current!.skill=="player.read_mail"?"Farm":Utility.getHomeOfFarmer(Game1.player).NameOrUniqueName;
        Current.phase="information_travel";
    }
    private void TickInformation() {
        if(DateTime.UtcNow<nextInteraction||Game1.locationRequest!=null||Game1.fadeToBlack)return;
        nextInteraction=DateTime.UtcNow.AddMilliseconds(250);
        if(++informationPages>600)throw new InvalidOperationException("information_interaction_limit");
        if(Game1.activeClickableMenu is LetterViewerMenu letter&&Current!.skill=="player.read_mail") {
            if(letter.isFromCollection)throw new InvalidOperationException("mail_collection_is_not_unread_mail");
            if(letter.scale<1f)return;
            if(informationLetter.Length==0)informationLetter=letter.mailTitle;
            if(letter.mailTitle!=informationLetter)throw new InvalidOperationException("mail_menu_changed");
            if(letter.page<letter.mailMessage.Count-1){var page=letter.forwardButton.bounds;letter.receiveLeftClick(page.Center.X,page.Center.Y);return;}
            var gift=letter.itemsToGrab.FirstOrDefault(c=>c.item!=null);
            if(gift?.item is {} item) {
                CapacityAdapter.RequireReceive(Game1.player,item);
                string id=item.QualifiedItemId;int amount=item.Stack,quality=item.Quality;
                int before=Game1.player.Items.Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack);
                letter.receiveLeftClick(gift.bounds.Center.X,gift.bounds.Center.Y);
                int received=Game1.player.Items.Where(i=>i?.QualifiedItemId==id&&i.Quality==quality).Sum(i=>i.Stack)-before;
                if(gift.item!=null||received!=amount)throw new InvalidOperationException("mail_gift_not_verified");
                Current.effects.Add(new{kind="native_mail_gift",mail=letter.mailTitle,item=id,quality,count=received});return;
            }
            if(letter.HasQuestOrSpecialOrder) {
                string? quest=letter.questID,order=letter.specialOrderId;var button=letter.acceptQuestButton.bounds;
                letter.receiveLeftClick(button.Center.X,button.Center.Y);
                bool verified=quest!=null?Game1.player.questLog.Any(q=>q.id.Value==quest):order!=null&&Game1.player.team.specialOrders.Any(o=>o.questKey.Value==order);
                if(letter.HasQuestOrSpecialOrder||!verified)throw new InvalidOperationException("mail_quest_acceptance_not_verified");
                Current.effects.Add(new{kind="native_mail_quest",quest,order});return;
            }
            if(!letter.readyToClose())return;
            var learned=KnownRecipeKeys().Except(informationRecipes).ToArray();informationRecipes=KnownRecipeKeys();
            Current.effects.Add(new{kind="native_letter_read",mail=letter.mailTitle,text=letter.mailMessage,recipes_learned=learned,letter.moneyIncluded});Current.completed++;
            letter.exitThisMenu();informationLetter="";Current.phase="information_travel";return;
        }
        if(Game1.activeClickableMenu is DialogueBox dialogue&&Current!.skill=="player.watch_tv") {
            dialogue.finishTyping();
            if(dialogue.isQuestion) {
                if(Current.phase!="television_select")throw new InvalidOperationException("unexpected_tv_question");
                int selected=Array.FindIndex(dialogue.responses,r=>r.responseKey==informationChannel);
                bool available=selected>=0;
                if(!available)selected=Array.FindIndex(dialogue.responses,r=>r.responseKey=="(Leave)");
                if(selected<0||dialogue.responseCC==null||selected>=dialogue.responseCC.Count)throw new InvalidOperationException("tv_choice_schema_changed");
                var bounds=dialogue.responseCC[selected].bounds;dialogue.performHoverAction(bounds.Center.X,bounds.Center.Y);dialogue.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
                Current.effects.Add(new{kind="native_tv_channel",channel=informationChannel,available});Current.phase="television_watching";return;
            }
            Current.effects.Add(new{kind="native_tv_program",channel=informationChannel,text=dialogue.getCurrentString()});
            dialogue.receiveLeftClick(dialogue.xPositionOnScreen+16,dialogue.yPositionOnScreen+16);return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("information_menu_requires_review");
        if(!Game1.player.CanMove)return;
        if(Current!.skill=="player.watch_tv"&&Current.phase=="television_watching") {
            Current.effects.Add(new{kind="native_tv_finished",recipes_learned=KnownRecipeKeys().Except(informationRecipes).ToArray()});Current.completed=1;Finish("succeeded");return;
        }
        if(Current.skill=="player.read_mail"&&(Game1.mailbox.Count==0||Current.completed>=informationLimit)){Finish("succeeded");return;}
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(informationTile==null) {
            if(Current.skill=="player.read_mail")informationTile=Game1.player.getMailboxPosition();
            else {
                informationTV=Game1.currentLocation.furniture.OfType<TV>().OrderBy(tv=>Vector2.Distance(tv.TileLocation,Game1.player.Tile)).FirstOrDefault()??throw new InvalidOperationException("home_television_missing");
                informationTile=informationTV.TileLocation.ToPoint();
            }
            Walk(Approach(informationTile.Value,true));Current.phase="information_walk";
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Adjacent(informationTile.Value);Face(informationTile.Value);
        if(Current.skill=="player.read_mail") {
            informationLetter=Game1.mailbox[0];int before=Game1.mailbox.Count;
            NativeMenuInput.InteractWorld(informationTile.Value);
            if(Game1.mailbox.Count>=before)throw new InvalidOperationException("native_mailbox_not_opened");
            if(Game1.activeClickableMenu is not LetterViewerMenu){Current.effects.Add(new{kind="native_empty_letter_removed",mail=informationLetter});informationLetter="";Current.completed++;}
        }else {
            if(informationTV==null||!Game1.currentLocation.furniture.Contains(informationTV)||!informationTV.checkForAction(Game1.player))throw new InvalidOperationException("native_tv_interaction_rejected");
            Current.phase="television_select";
        }
    }
}
