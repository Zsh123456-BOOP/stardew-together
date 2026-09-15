using System.Reflection;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private string beachMode="";
    private Point? beachTarget;
    private int beachBudget,beachKeep,beachMoney,beachWood,beachPendant;
    private bool beachSubmitted;
    private static NPC? Mariner(Beach beach)=>typeof(Beach).GetField("oldMariner",BindingFlags.Instance|BindingFlags.NonPublic)?.GetValue(beach) as NPC;
    internal static object ReadBeach() {
        var beach=LoadedLocation("Beach") as Beach;
        return new{bridge_fixed=beach?.bridgeFixed.Value,mariner_present=beach!=null&&Mariner(beach)!=null,eligible_relationship=Game1.player.hasAFriendWithHeartLevel(10,datablesOnly:true),house_level=Game1.player.HouseUpgradeLevel,pendant_price=5000,bridge_wood=300,
            note="老水手按原生下雨/营业条件出现；修桥与购买使用实际菜单、材料和金钱。"};
    }
    private void StartBeach(JsonElement args) {
        beachMode=AgentToolRegistry.Text(args,"mode");if(beachMode is not ("bridge" or "pendant"))throw new InvalidOperationException("invalid_beach_service");
        beachBudget=AgentToolRegistry.Number(args,"budget",0);beachKeep=AgentToolRegistry.Number(args,"keep_gold",0);
        if(beachKeep<0||beachBudget<0)throw new InvalidOperationException("invalid_beach_budget");
        beachTarget=null;beachSubmitted=false;destination="Beach";Current!.phase="beach_travel";
    }
    private void CheckBeachCost() {
        var p=Game1.player;
        if(beachMode=="pendant") {
            if(beachBudget<5000||p.Money-5000<beachKeep)throw new InvalidOperationException("pendant_budget_insufficient");
            if(p.isMarriedOrRoommates()||p.isEngaged()||!p.hasAFriendWithHeartLevel(10,datablesOnly:true)||p.HouseUpgradeLevel<1)throw new InvalidOperationException("pendant_native_relationship_or_house_requirement");
            CapacityAdapter.RequireReceive(p,ItemRegistry.Create("(O)460"));
            return;
        }
        int remaining=300;var used=new Dictionary<Item,int>();foreach(var item in p.Items.Where(i=>i?.QualifiedItemId=="(O)388")){int n=Math.Min(remaining,item.Stack);if(n>0)used[item]=n;remaining-=n;}
        if(remaining>0)throw new InvalidOperationException("beach_bridge_needs_300_wood");ValidateConsumption?.Invoke(used,"","bridge:Beach");
    }
    private void TickBeach() {
        var p=Game1.player;
        if(Current!.phase=="beach_confirm") {
            if(Game1.currentLocation is not Beach beach)throw new InvalidOperationException("beach_location_changed");
            if(beachSubmitted) {
                if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} notice) {
                    if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(400);notice.finishTyping();NativeMenuInput.ClickMenu(notice,new Rectangle(notice.xPositionOnScreen+16,notice.yPositionOnScreen+16,32,32));return;
                }
                if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("beach_result_menu_requires_review");
                if(!p.CanMove||Game1.fadeToBlack)return;
                int used=beachWood-p.Items.Where(i=>i?.QualifiedItemId=="(O)388").Sum(i=>i.Stack),paid=beachMoney-p.Money,received=p.Items.Where(i=>i?.QualifiedItemId=="(O)460").Sum(i=>i.Stack)-beachPendant;
                bool done=beachMode=="bridge"?beach.bridgeFixed.Value&&used==300:received==1&&paid==5000;
                Current.effects.Add(new{kind="native_beach_service",mode=beachMode,bridge_fixed=beach.bridgeFixed.Value,wood_used=used,gold_paid=paid,pendants_received=received});if(done)Current.completed=1;
                Finish(done?"succeeded":"failed",done?null:"native_beach_result_not_verified");return;
            }
            if(Game1.activeClickableMenu is not DialogueBox {isQuestion:true} question||beach.lastQuestionKey!=(beachMode=="bridge"?"BeachBridge":"mariner"))throw new InvalidOperationException("beach_native_offer_unavailable");
            CheckBeachCost();question.finishTyping();int index=Array.FindIndex(question.responses,r=>r.responseKey==(beachMode=="bridge"?"Yes":"Buy"));
            if(index<0||question.responseCC==null||index>=question.responseCC.Count)throw new InvalidOperationException("beach_offer_choice_missing");
            beachMoney=p.Money;beachWood=p.Items.Where(i=>i?.QualifiedItemId=="(O)388").Sum(i=>i.Stack);beachPendant=p.Items.Where(i=>i?.QualifiedItemId=="(O)460").Sum(i=>i.Stack);
            NativeMenuInput.ClickMenu(question,question.responseCC[index].bounds);beachSubmitted=true;return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("beach_travel_menu_requires_review");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!p.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        var l=(Beach)Game1.currentLocation;
        if(beachMode=="bridge"&&l.bridgeFixed.Value){Current.effects.Add(new{kind="already_repaired_bridge"});Finish("succeeded");return;}
        if(beachMode=="pendant"&&p.Items.Any(i=>i?.QualifiedItemId=="(O)460")){Finish("succeeded");return;}
        CheckBeachCost();
        if(beachTarget==null) {
            var sites=new List<Point>();
            if(beachMode=="pendant") {var npc=Mariner(l)??throw new InvalidOperationException("mariner_not_present_need_native_rain_and_hours");sites.Add(npc.TilePoint);}
            else {var layer=l.Map.Layers[0];for(int y=0;y<layer.LayerHeight;y++)for(int x=0;x<layer.LayerWidth;x++)if(l.getTileIndexAt(x,y,"Buildings","untitled tile sheet")==284)sites.Add(new(x,y));}
            foreach(var at in sites.OrderBy(t=>Vector2.DistanceSquared(t.ToVector2(),p.Tile)))try{Walk(Approach(at,true));beachTarget=at;break;}catch(InvalidOperationException){}
            if(beachTarget==null)throw new InvalidOperationException("beach_service_unreachable");
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Face(beachTarget.Value);Adjacent(beachTarget.Value);
        NativeMenuInput.InteractWorld(beachTarget.Value);Current.phase="beach_confirm";
    }
}
