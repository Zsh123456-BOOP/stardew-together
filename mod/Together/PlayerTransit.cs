using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    private string transitRoute="",transitArrival="",transitQuestion="",transitPart="";
    private int transitBudget,transitKeep;
    private Point? transitTile;
    private static (string Item,int Count,string Mail,string Question,Point? Tile) BoatPart(string part)=>part switch {
        "hull"=>("(O)709",200,"willyBoatHull","WillyBoatDonateHardwood",new Point(6,8)),
        "anchor"=>("(O)337",5,"willyBoatAnchor","WillyBoatDonateIridium",new Point(8,10)),
        "ticket_machine"=>("(O)787",5,"willyBoatTicketMachine","WillyBoatDonateBatteries",null),
        _=>throw new InvalidOperationException("unknown_boat_repair_part")
    };
    internal static object ReadTransportation()=>new {
        bus=new{unlocked=Game1.MasterPlayer.mailReceived.Contains("ccVault"),price=(Game1.getLocationFromName("BusStop") as BusStop)?.TicketPrice,driver_present=Game1.netWorldState.Value.canDriveYourselfToday.Value||Game1.getLocationFromName("BusStop")?.characters.Any(n=>n.Name=="Pam"&&n.TilePoint==new Point(21,10))==true},
        boat=new{unlocked=Game1.MasterPlayer.mailReceived.Contains("willyBoatFixed"),price=(Game1.getLocationFromName("BoatTunnel") as BoatTunnel)?.TicketPrice,parts=new[]{"hull","anchor","ticket_machine"}.Select(id=>{var part=BoatPart(id);return new{id,part.Item,part.Count,completed_or_pending=Game1.MasterPlayer.hasOrWillReceiveMail(part.Mail)};})},
        routes=new[]{"desert","from_desert","island","from_island"}
    };
    private void StartTransit(JsonElement args) {
        transitTile=null;transitPart="";transitBudget=AgentToolRegistry.Number(args,"budget",0);transitKeep=AgentToolRegistry.Number(args,"keep_gold",500);
        if(transitBudget<0||transitKeep<0)throw new InvalidOperationException("invalid_transport_budget");
        if(Current!.skill=="player.repair_boat") {
            transitPart=AgentToolRegistry.Text(args,"part");var part=BoatPart(transitPart);
            if(Game1.MasterPlayer.hasOrWillReceiveMail(part.Mail)){Current.effects.Add(new{kind="boat_part_already_repaired_or_pending",part=transitPart});Finish("succeeded");return;}
            destination="BoatTunnel";transitArrival="";transitQuestion=part.Question;transitRoute="repair";
        } else {
            transitRoute=AgentToolRegistry.Text(args,"route");
            (destination,transitArrival,transitQuestion)=transitRoute switch {
                "desert"=>("BusStop","Desert","Bus"),"from_desert"=>("Desert","BusStop","DesertBus"),
                "island"=>("BoatTunnel","IslandSouth","Boat"),"from_island"=>("IslandSouth","BoatTunnel","LeaveIsland"),
                _=>throw new InvalidOperationException("unknown_transport_route")
            };
            if(Game1.currentLocation.NameOrUniqueName==transitArrival){Finish("succeeded");return;}
            if(transitRoute=="desert"&&!Game1.MasterPlayer.mailReceived.Contains("ccVault")||transitRoute=="island"&&!Game1.MasterPlayer.mailReceived.Contains("willyBoatFixed"))throw new InvalidOperationException("native_transport_not_unlocked");
        }
        Current.phase="transit_travel";
    }
    private void TickTransit() {
        if(Current!.phase=="transit_departed") {
            if(Game1.currentLocation.NameOrUniqueName==transitArrival&&!Game1.fadeToBlack&&Game1.locationRequest==null&&Game1.currentMinigame==null) {
                Current.effects.Add(new{kind="native_transport_arrival",route=transitRoute,location=transitArrival,event_pending=Game1.eventUp});Current.completed=1;Finish("succeeded");return;
            }
            if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} travelNotice&&DateTime.UtcNow>=nextInteraction){nextInteraction=DateTime.UtcNow.AddMilliseconds(350);travelNotice.finishTyping();travelNotice.receiveLeftClick(travelNotice.xPositionOnScreen+16,travelNotice.yPositionOnScreen+16);}
            // Native bus/boat movement and the first-journey cinematic own the player.
            return;
        }
        if(Game1.eventUp)throw new InvalidOperationException("event_interrupted_read_menu");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||DateTime.UtcNow<nextInteraction)return;
        if(Game1.activeClickableMenu is DialogueBox question) {
            if(!question.isQuestion||Game1.currentLocation.NameOrUniqueName!=destination||Game1.currentLocation.lastQuestionKey!=transitQuestion)throw new InvalidOperationException("native_transport_offer_unavailable_read_notice");
            question.finishTyping();int index=Array.FindIndex(question.responses,r=>r.responseKey=="Yes");
            if(index<0||question.responseCC==null||index>=question.responseCC.Count)throw new InvalidOperationException("transport_confirmation_not_available");
            int beforeMoney=Game1.player.Money,cost=Game1.currentLocation is BusStop bus?bus.TicketPrice:transitRoute=="island"&&Game1.currentLocation is BoatTunnel boat?boat.TicketPrice:0;
            if(transitRoute=="repair")cost=0;
            if(cost>transitBudget||cost>0&&Game1.player.Money-cost<transitKeep)throw new InvalidOperationException("transport_budget_insufficient");
            string item="",mail="";int needed=0,beforeItems=0;
            if(transitRoute=="repair") {
                var part=BoatPart(transitPart);item=part.Item;needed=part.Count;mail=part.Mail;
                beforeItems=Game1.player.Items.Where(i=>i?.QualifiedItemId==item).Sum(i=>i.Stack);
                if(beforeItems<needed)throw new InvalidOperationException("boat_materials_missing");
                var consumed=new Dictionary<Item,int>();int remainder=needed;
                foreach(var stack in Game1.player.Items.Where(i=>i?.QualifiedItemId==item)){int take=Math.Min(remainder,stack.Stack);if(take>0)consumed[stack]=take;remainder-=take;if(remainder==0)break;}
                ValidateConsumption?.Invoke(consumed,"","boat:"+transitPart);
            }
            var bounds=question.responseCC[index].bounds;question.performHoverAction(bounds.Center.X,bounds.Center.Y);question.receiveLeftClick(bounds.Center.X,bounds.Center.Y);
            if(transitRoute=="repair") {
                if(!Game1.MasterPlayer.hasOrWillReceiveMail(mail)||beforeItems-Game1.player.Items.Where(i=>i?.QualifiedItemId==item).Sum(i=>i.Stack)!=needed)throw new InvalidOperationException("native_boat_repair_not_verified");
                Current.effects.Add(new{kind="native_boat_repair",part=transitPart,item,consumed=needed,pending_mail=mail});Current.completed=1;Finish("succeeded");return;
            }
            if(beforeMoney-Game1.player.Money!=cost)throw new InvalidOperationException("native_transport_payment_not_verified");
            if(Game1.activeClickableMenu is DialogueBox {isQuestion:false}&&transitRoute=="desert")throw new InvalidOperationException("bus_driver_or_departure_unavailable");
            Current.effects.Add(new{kind="native_transport_departure",route=transitRoute,cost});Current.phase="transit_departed";return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("transport_menu_requires_review");
        if(!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        bool touch=transitRoute is "from_desert" or "from_island";
        if(transitTile==null) {
            var location=Game1.currentLocation;
            if(transitRoute=="repair")transitTile=BoatPart(transitPart).Tile;
            if(transitTile==null)for(int y=0;y<location.Map.Layers[0].LayerHeight&&transitTile==null;y++)for(int x=0;x<location.Map.Layers[0].LayerWidth;x++) {
                string? action=location.doesTileHaveProperty(x,y,touch?"TouchAction":"Action",touch?"Back":"Buildings");
                bool found=transitRoute=="desert"?location.getTileIndexAt(x,y,"Buildings")==1057:action==(touch?transitQuestion:"BoatTicket");
                if(found){transitTile=new Point(x,y);break;}
            }
            if(transitTile==null)throw new InvalidOperationException("native_transport_interaction_not_found");
            Walk(touch?transitTile.Value:Approach(transitTile.Value,true));Current.phase="transit_walk";
        }
        if(Game1.player.TilePoint!=target){MonitorWalk();return;}StopWalk();
        if(touch) {if(Current.phase=="transit_touch_wait")throw new InvalidOperationException("native_transport_touch_not_triggered");Current.phase="transit_touch_wait";nextInteraction=DateTime.UtcNow.AddMilliseconds(750);return;}
        Adjacent(transitTile.Value);Face(transitTile.Value);
        NativeMenuInput.InteractWorld(transitTile.Value);
        if(Game1.activeClickableMenu is not DialogueBox)throw new InvalidOperationException("native_transport_interaction_rejected");nextInteraction=DateTime.UtcNow.AddMilliseconds(300);
    }
}
