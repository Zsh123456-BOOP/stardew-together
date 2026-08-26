using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Constants;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private int masterySkill;
    private Point? masteryPlaque;
    private HashSet<string> masteryRecipes=new();
    internal static object ReadMastery()=>new{experience=Game1.stats.Get("MasteryExp"),level=MasteryTrackerMenu.getCurrentMasteryLevel(),spent=Game1.stats.Get("masteryLevelsSpent"),skills=Enumerable.Range(0,5).Select(skill=>new{skill,claimed=Game1.player.stats.Get(StatKeys.Mastery(skill))>0}),note="原生技能编号：0农业、1钓鱼、2采集、3采矿、4战斗"};
    private void StartMastery(JsonElement args) {
        masterySkill=AgentToolRegistry.Number(args,"skill",-1);masteryPlaque=null;masteryRecipes=KnownRecipeKeys();
        if(masterySkill is <0 or >4)throw new InvalidOperationException("native_mastery_skill_required");
        if(Game1.player.stats.Get(StatKeys.Mastery(masterySkill))>0){Finish("succeeded");return;}
        if(MasteryTrackerMenu.getCurrentMasteryLevel()<=Game1.stats.Get("masteryLevelsSpent"))throw new InvalidOperationException("unspent_native_mastery_level_required");
        if(Game1.player.Items.Count(i=>i==null)<3)throw new InvalidOperationException("mastery_reward_inventory_space_required");
        destination="MasteryCave";Current!.phase="mastery_travel";
    }
    private void TickMastery() {
        if(Game1.locationRequest!=null||Game1.fadeToBlack)return;
        if(Current!.phase=="mastery_claimed") {
            if(Game1.activeClickableMenu==null&&Game1.player.CanMove){Finish("succeeded");return;}return;
        }
        if(Game1.activeClickableMenu is MasteryTrackerMenu menu) {
            var field=typeof(MasteryTrackerMenu).GetField("which",System.Reflection.BindingFlags.Instance|System.Reflection.BindingFlags.NonPublic);
            if((int?)field?.GetValue(menu)!=masterySkill||Game1.currentLocation.NameOrUniqueName!=destination)throw new InvalidOperationException("wrong_mastery_plaque");
            if(menu.mainButton?.visible!=true)throw new InvalidOperationException("native_mastery_claim_unavailable");
            if(Game1.player.Items.Count(i=>i==null)<3)throw new InvalidOperationException("mastery_reward_inventory_space_changed");
            uint spent=Game1.stats.Get("masteryLevelsSpent");var bounds=menu.mainButton.bounds;menu.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
            if(Game1.player.stats.Get(StatKeys.Mastery(masterySkill))==0||Game1.stats.Get("masteryLevelsSpent")!=spent+1)throw new InvalidOperationException("native_mastery_claim_not_verified");
            Current.effects.Add(new{kind="native_mastery_reward",skill=masterySkill,spent_before=spent,spent_after=Game1.stats.Get("masteryLevelsSpent"),recipes_learned=KnownRecipeKeys().Except(masteryRecipes).ToArray(),inventory=AgentToolRegistry.Inventory()});Current.completed=1;Current.phase="mastery_claimed";return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("mastery_menu_interrupted");
        if(!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(masteryPlaque==null) {
            string action="MasteryCave_"+new[]{"Farming","Fishing","Foraging","Mining","Combat"}[masterySkill];var l=Game1.currentLocation;
            for(int y=0;y<l.Map.Layers[0].LayerHeight&&masteryPlaque==null;y++)for(int x=0;x<l.Map.Layers[0].LayerWidth;x++)if(l.doesTileHaveProperty(x,y,"Action","Buildings")==action)try{var tile=new Point(x,y);Walk(Approach(tile,true));masteryPlaque=tile;break;}catch(InvalidOperationException){}
            if(masteryPlaque==null)throw new InvalidOperationException("native_mastery_plaque_unreachable");
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Adjacent(masteryPlaque.Value);Face(masteryPlaque.Value);NativeMenuInput.InteractWorld(masteryPlaque.Value);
        if(Game1.activeClickableMenu is not MasteryTrackerMenu)throw new InvalidOperationException("native_mastery_plaque_not_opened");
    }
}
