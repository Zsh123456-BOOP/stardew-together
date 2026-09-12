using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class PlayerExecutor {
    public Action? ShopOpened {get;set;}
    private string service="",serviceShop="",serviceAction="";
    private Point? serviceTile;
    private bool serviceSelected;
    private int servicePages;
    private static string? ShopFromAction(GameLocation location,string[] action)=>action.FirstOrDefault() switch {
        "OpenShop"=>action.Length>1?action[1]:null,
        "Buy"=>action.Length>1&&action[1]=="Fish"?"FishShop":location.Name=="SeedShop"?"SeedShop":location.Name=="SandyHouse"?"Sandy":null,
        "Carpenter"=>"Carpenter","Blacksmith"=>"Blacksmith","AnimalShop"=>"AnimalShop","Saloon"=>"Saloon",
        "JojaShop"=>"Joja","HospitalShop"=>"Hospital","AdventureShop"=>"AdventureShop","ClubShop"=>"Casino","QiGemShop"=>"QiGemShop",_=>null
    };
    private void StartService(JsonElement args) {
        service=AgentToolRegistry.Text(args,"service","shop");serviceShop=AgentToolRegistry.Text(args,"shop","");destination=AgentToolRegistry.Text(args,"location",origin);
        if(Game1.getLocationFromName(destination)==null)throw new InvalidOperationException("unknown_service_location");
        if(service is not ("shop" or "build" or "upgrade_house" or "upgrade_tools" or "animals" or "geodes" or "claim_tool" or "museum_donate" or "museum_reward" or "daily_quests" or "special_orders" or "qi_orders"))throw new InvalidOperationException("unknown_native_service");
        if(service=="shop"&&serviceShop.Length==0)throw new InvalidOperationException("observed_shop_id_required");
        serviceTile=null;serviceSelected=false;servicePages=0;Current!.phase="service_travel";
    }
    private void TickService() {
        if(Current!.phase=="service_menu") {
            var menu=Game1.activeClickableMenu;
            bool ready=service switch {
                "shop"=>menu is ShopMenu shop&&shop.ShopId==serviceShop,
                "daily_quests"=>menu is Billboard,"special_orders"=>menu is SpecialOrdersBoard {boardType:""},"qi_orders"=>menu is SpecialOrdersBoard {boardType:"Qi"},
                "museum_donate"=>menu is MuseumMenu,"museum_reward"=>menu is ItemGrabMenu,
                "build"=>menu is CarpenterMenu,"upgrade_tools"=>menu is ShopMenu toolShop&&toolShop.ShopId=="ClintUpgrade",
                "animals"=>menu is PurchaseAnimalsMenu,"geodes"=>menu is GeodeMenu,
                "upgrade_house"=>serviceSelected&&menu is DialogueBox {isQuestion:true},
                "claim_tool"=>Game1.player.toolBeingUpgraded.Value==null&&Game1.player.Items.OfType<Tool>().Any(t=>t.QualifiedItemId==serviceAction),_=>false
            };
            if(ready&&menu is ShopMenu)ShopOpened?.Invoke();
            if(ready&&Current.skill=="player.upgrade_house"){Current.phase="house_confirm";return;}
            if(ready&&Current.skill=="player.acquire_animal"){StartLivestockPurchase(procurementArgs);return;}
            if(ready&&Current.skill=="player.procure"){StartPurchase(procurementArgs);return;}
            if(ready){Current.effects.Add(new{kind="native_service_opened",service,shop=(menu as ShopMenu)?.ShopId,menu=menu?.GetType().Name,note="服务已打开；购买/建造/升级决策仍需执行并核验"});Finish("succeeded");return;}
            if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(400);
            if(menu is DialogueBox dialogue) {
                if(++servicePages>20)throw new InvalidOperationException("service_dialogue_limit");dialogue.finishTyping();
                if(!dialogue.isQuestion){dialogue.receiveLeftClick(dialogue.xPositionOnScreen+16,dialogue.yPositionOnScreen+16);return;}
                if(service=="build"&&Game1.currentLocation.lastQuestionKey=="pagedResponse") {
                    int farm=Array.FindIndex(dialogue.responses,r=>r.responseKey=="Farm");
                    if(farm<0)farm=Array.FindIndex(dialogue.responses,r=>r.responseKey=="nextPage");
                    if(farm<0||dialogue.responseCC==null||farm>=dialogue.responseCC.Count)throw new InvalidOperationException("farm_build_location_choice_missing");
                    NativeMenuInput.ClickMenu(dialogue,dialogue.responseCC[farm].bounds);return;
                }
                string key=service switch{"shop"=>serviceShop=="AnimalShop"?"Supplies":"Shop","museum_donate"=>"Donate","museum_reward"=>"Collect","build"=>"Construct","upgrade_house" or "upgrade_tools"=>"Upgrade","animals"=>"Purchase","geodes"=>"Process",_=>""};
                int index=Array.FindIndex(dialogue.responses,r=>r.responseKey==key);
                if(serviceSelected||index<0||dialogue.responseCC==null||dialogue.responseCC.Count<=index)throw new InvalidOperationException("service_branch_unavailable_read_menu");
                var bounds=dialogue.responseCC[index].bounds;dialogue.performHoverAction(bounds.Center.X,bounds.Center.Y);dialogue.receiveLeftClick(bounds.Center.X,bounds.Center.Y);serviceSelected=true;return;
            }
            if(!Game1.player.CanMove)return;
            throw new InvalidOperationException("native_service_did_not_open_or_wrong_shop");
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("service_route_menu_requires_review");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        var l=Game1.currentLocation;
        if(serviceTile==null) {
            var candidates=new List<Point>();
            for(int y=0;y<l.Map.Layers[0].LayerHeight;y++)for(int x=0;x<l.Map.Layers[0].LayerWidth;x++) {
                var action=l.GetTilePropertySplitBySpaces("Action","Buildings",x,y);if(action.Length==0)continue;
                bool match=service switch {
                    "shop"=>ShopFromAction(l,action)==serviceShop,
                    "daily_quests"=>action[0]=="Billboard"&&action.Length>1&&action[1]=="3",
                    "special_orders"=>action[0]=="SpecialOrders","qi_orders"=>action[0]=="QiChallengeBoard",
                    "museum_donate" or "museum_reward"=>action[0]=="Gunther",
                    "build"=>action[0] is "Carpenter" or "WizardBook",
                    "upgrade_house"=>action[0]=="Carpenter",
                    "animals"=>action[0]=="AnimalShop",_=>action[0]=="Blacksmith"
                };
                if(match)candidates.Add(new(x,y));
            }
            foreach(var at in candidates.OrderBy(p=>Vector2.DistanceSquared(p.ToVector2(),Game1.player.Tile))) {
                // Legacy counters require approaching from below. Explicit OpenShop
                // direction is left to the native action's own condition validation.
                foreach(var stand in new[]{new Point(at.X,at.Y+1),new Point(at.X-1,at.Y),new Point(at.X+1,at.Y),new Point(at.X,at.Y-1)}) {
                    if(!Passable(l,stand))continue;
                    try{Walk(stand);serviceTile=at;Current.phase="service_walk";break;}catch(InvalidOperationException){ }
                }
                if(serviceTile.HasValue)break;
            }
            if(!serviceTile.HasValue)throw new InvalidOperationException("no_reachable_native_service_counter");
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();var counter=serviceTile.Value;Adjacent(counter);Face(counter);
        if(service=="claim_tool") {
            var tool=Game1.player.toolBeingUpgraded.Value;if(tool==null||Game1.player.daysLeftForToolUpgrade.Value>0)throw new InvalidOperationException("no_finished_tool_upgrade");
            serviceAction=tool.QualifiedItemId;
        }
        if(!Game1.tryToCheckAt(counter.ToVector2(),Game1.player))throw new InvalidOperationException("native_service_unavailable_check_hours_and_owner");
        Current.phase="service_menu";nextInteraction=DateTime.UtcNow.AddMilliseconds(400);
    }
}
