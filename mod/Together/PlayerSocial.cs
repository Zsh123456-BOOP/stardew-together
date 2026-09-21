using System.Text.Json;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Quests;

namespace Together;

public sealed partial class PlayerExecutor {
    public Func<string,string>? RecruitCompanion {get;set;}
    private string socialName="",socialMode="",socialItem="",socialQuest="";
    private int socialSlot,socialStack,socialPoints,socialGifts,socialPages;
    private bool socialTalked;
    private NPC? socialNpc;
    private Microsoft.Xna.Framework.Point? socialApproachTile;
    private Quest? socialQuestObject;
    private object SocialEvidence()=>new {
        npc=socialName,mode=socialMode,
        friendship_before=socialPoints,friendship_after=Game1.player.friendshipData.GetValueOrDefault(socialName)?.Points??0,
        gifts_before=socialGifts,gifts_after=Game1.player.friendshipData.GetValueOrDefault(socialName)?.GiftsToday??0,
        talked_before=socialTalked,talked_after=Game1.player.friendshipData.GetValueOrDefault(socialName)?.TalkedToToday??false,
        item=socialItem,consumed=socialStack-Game1.player.Items.Where(i=>i?.QualifiedItemId==socialItem).Sum(i=>i.Stack),
        quest=socialQuest,quest_completed=socialQuestObject?.completed.Value==true,order=socialOrder?.questKey.Value,order_before=socialOrderBefore,order_after=socialOrderObjective?.GetCount()
        ,relationship_status=Game1.player.friendshipData.GetValueOrDefault(socialName)?.Status.ToString(),spouse=Game1.player.spouse
    };
    private void StartSocial(JsonElement args) {
        socialApproachTile=null;socialName=AgentToolRegistry.Text(args,"npc");socialMode=AgentToolRegistry.Text(args,"mode","talk");socialQuest=AgentToolRegistry.Text(args,"quest_id","");
        if(socialMode is not ("recruit" or "talk" or "greet" or "gift" or "deliver" or "order_deliver" or "relationship"))throw new InvalidOperationException("invalid_social_mode");
        BindSocialOrder(args);
        socialNpc=Game1.getCharacterFromName(socialName)??throw new InvalidOperationException("unknown_npc");
        if(socialNpc.IsMonster||socialNpc.currentLocation==null)throw new InvalidOperationException("npc_not_available");
        if(SocialObservation.Satisfied(Game1.player,socialName,socialMode,socialQuest) is {} satisfied) {
            Current!.stop_reason=satisfied;Current.effects.Add(new{kind="social_already_satisfied",reason=satisfied,npc=socialName,quest_id=socialQuest,observation=SocialObservation.Read(Game1.player),action_executed=false});Finish("succeeded");return;
        }
        socialQuestObject=socialMode is "deliver" or "greet"?NativeQuestIdentity.Find(socialQuest):null;
        if(socialMode is "deliver" or "greet"&&(socialQuestObject==null||socialQuestObject.completed.Value))throw new InvalidOperationException("active_quest_id_required");
        if(socialMode=="greet"&&socialQuestObject is not SocializeQuest)throw new InvalidOperationException("greet_requires_introductions_quest");
        socialSlot=AgentToolRegistry.Number(args,"slot",-1);socialItem="";socialStack=0;socialPages=0;
        if(socialMode is "gift" or "relationship" or "order_deliver" || socialMode=="deliver"&&socialSlot>=0) {
            SelectSlot(args,true);
            var item=Game1.player.ActiveObject??throw new InvalidOperationException("social_item_must_be_object");socialItem=item.QualifiedItemId;
            string expected=AgentToolRegistry.Text(args,"item");if(expected.Length>0&&socialItem!=expected)throw new InvalidOperationException("planned_social_item_slot_changed");
            socialStack=Game1.player.Items.Where(i=>i?.QualifiedItemId==socialItem).Sum(i=>i.Stack);
            if(socialMode=="gift"&&(item.questItem.Value||item.QualifiedItemId is "(O)458" or "(O)460" or "(O)808" or "(O)809"||item.GetContextTags().Any(t=>t.StartsWith("propose_roommate_"))))
                throw new InvalidOperationException("special_relationship_item_requires_relationship_mode");
        } else {
            socialSlot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is null or Tool,-1);
            if(socialSlot<0)throw new InvalidOperationException("need_empty_or_tool_slot_for_talk");
        }
        var f=Game1.player.friendshipData.GetValueOrDefault(socialName);socialPoints=f?.Points??0;socialGifts=f?.GiftsToday??0;socialTalked=f?.TalkedToToday??false;
        if(socialMode=="talk"&&socialTalked){Current!.effects.Add(SocialEvidence());Finish("succeeded");return;}
        var access=SocialReach(socialNpc);
        Current!.effects.Add(new{kind="social_preflight",access});
        if(!access.reachable)throw new InvalidOperationException("social_access_unavailable");
        destination=socialNpc.currentLocation.NameOrUniqueName;Current!.phase="social_travel";
    }
    private Microsoft.Xna.Framework.Point SocialStand(NPC npc) {
        var p=Game1.player;var access=SocialReach(npc);
        if(access.reachable&&access.stand is {} selected) {
            var stand=new Microsoft.Xna.Framework.Point(selected[0],selected[1]);var path=MeasuredPath(stand);
            if(stand==p.TilePoint||path?.Count>0) {
                approachPath=path;approachStart=p.TilePoint;approachEnd=stand;approachLocation=Game1.currentLocation;
                Current!.effects.Add(new{kind="social_approach_selected",npc=npc.Name,access});return stand;
            }
        }
        throw new InvalidOperationException("exit_unreachable");
    }
    private void TickSocial() {
        var p=Game1.player;
        if(Current!.phase=="social_dialogue") {
            if(Game1.activeClickableMenu is DialogueBox {isQuestion:false} dialogue) {
                if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(400);
                if(++socialPages>40)throw new InvalidOperationException("dialogue_page_limit");
                Current.effects.Add(new{kind="native_npc_dialogue",npc=socialName,text=dialogue.getCurrentString()});
                dialogue.finishTyping();dialogue.receiveLeftClick(dialogue.xPositionOnScreen+16,dialogue.yPositionOnScreen+16);return;
            }
            if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("social_branching_menu_requires_choice");
            if(!p.CanMove||p.freezePause>0)return;
            var f=p.friendshipData.GetValueOrDefault(socialName);bool complete=socialMode switch {
                "talk"=>f?.TalkedToToday==true,
                "greet"=>socialQuestObject is SocializeQuest introduction&&!introduction.whoToGreet.Contains(socialName),
                "deliver"=>socialQuestObject?.completed.Value==true,
                "order_deliver"=>socialOrderObjective!=null&&socialOrderObjective.GetCount()>socialOrderBefore&&socialStack-p.Items.Where(i=>i?.QualifiedItemId==socialItem).Sum(i=>i.Stack)==socialOrderObjective.GetCount()-socialOrderBefore,
                "gift"=>(f?.GiftsToday??0)>socialGifts,
                _=>p.Items.Where(i=>i?.QualifiedItemId==socialItem).Sum(i=>i.Stack)<socialStack&&RelationshipResult(f)
            };
            Current.effects.Add(SocialEvidence());if(complete)Current.completed=1;Finish(complete?"succeeded":"failed",complete?null:"native_social_goal_not_verified");return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("social_travel_interrupted_by_menu");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!p.CanMove)return;
        var npc=socialNpc??throw new InvalidOperationException("npc_missing");
        if(npc.currentLocation==null||npc.IsInvisible||npc.isSleeping.Value)throw new InvalidOperationException("npc_unavailable_or_sleeping");
        string next=npc.currentLocation.NameOrUniqueName;
        if(destination!=next){destination=next;edge=null;StopWalk();}
        if(Game1.currentLocation.NameOrUniqueName!=destination){Current.phase="social_travel";Travel();return;}
        if(!Utility.withinRadiusOfPlayer(npc.StandingPixel.X,npc.StandingPixel.Y,1,p)) {
            if((ownedController==null||socialApproachTile!=npc.TilePoint)&&DateTime.UtcNow>=nextInteraction) {
                nextInteraction=DateTime.UtcNow.AddMilliseconds(500);
                try{Walk(SocialStand(npc));socialApproachTile=npc.TilePoint;}
                catch(InvalidOperationException e)when(e.Message=="exit_unreachable") {
                    Current.effects.Add(new{kind="npc_stand_blocked",npc=npc.Name,location=Game1.currentLocation.NameOrUniqueName,npc_tile=new[]{npc.TilePoint.X,npc.TilePoint.Y},player=new[]{Game1.player.TilePoint.X,Game1.player.TilePoint.Y},npc_bounds=npc.GetBoundingBox().ToString(),player_bounds=Game1.player.GetBoundingBox().ToString()});
                    throw new InvalidOperationException("social_access_unavailable");
                }
            }
            if(ownedController!=null)MonitorWalk();Current.phase="social_approach";return;
        }
        StopWalk();
        if(socialMode=="recruit") {
            string actor=RecruitCompanion?.Invoke(socialName)??throw new InvalidOperationException("companion_recruitment_unavailable");
            Current.effects.Add(new{kind="companion_recruited",npc=socialName,actor});Current.completed=1;Finish("succeeded");return;
        }
        PlayerSelection.Set(p,socialSlot);p.netItemStowed.Value=false;Face(npc.TilePoint);
        if(socialItem.Length>0) {
            var item=p.ActiveObject;
            if(item?.QualifiedItemId!=socialItem)throw new InvalidOperationException("social_item_changed");
            if(socialMode=="order_deliver")ValidateOrderDelivery(npc,item);
            else {
            var matching=p.questLog.Where(q=>!q.completed.Value&&q.OnItemOfferedToNpc(npc,item,true)).ToArray();
            if(socialMode=="deliver"&&!matching.Any(q=>NativeQuestIdentity.Id(q)==socialQuest))throw new InvalidOperationException("quest_does_not_accept_this_item_or_npc");
            if(socialMode!="deliver"&&(matching.Length>0||p.team.specialOrders.Any(o=>o.onItemDelivered?.GetInvocationList().Cast<Func<Farmer,NPC,Item,bool,int>>().Any(f=>f(p,npc,item,true)>0)==true)))
                throw new InvalidOperationException("gift_would_deliver_quest_item_use_delivery");
            int used=socialMode=="deliver"?item.Stack:1;
            ValidateConsumption?.Invoke(new Dictionary<Item,int>{{item,used}},"",socialMode=="deliver"?"quest:"+socialQuest:"");
            }
        } else if(socialMode=="deliver") {
            var q=p.questLog.First(q=>NativeQuestIdentity.Id(q)==socialQuest);
            string? recipient=q switch {ResourceCollectionQuest r=>r.target.Value,FishingQuest f=>f.target.Value,SlayMonsterQuest s=>s.target.Value,LostItemQuest l=>l.npcName.Value,_=>null};
            if(recipient!=socialName)throw new InvalidOperationException("quest_needs_item_or_different_recipient");
            if(q is LostItemQuest lost) {
                var returning=p.Items.FirstOrDefault(i=>i?.QualifiedItemId==lost.ItemId.Value)??throw new InvalidOperationException("found_quest_item_must_be_carried");
                ValidateConsumption?.Invoke(new Dictionary<Item,int>{{returning,1}},"","quest:"+socialQuest);
            }
        }
        // One genuine nearby interaction routes through native quest, gift and dialogue logic.
        // The boolean can be false for a valid temporary dialogue: verify state after dismissal.
        Current.effects.Add(new{kind="native_social_interaction",npc=npc.Name,player_tile=new[]{p.TilePoint.X,p.TilePoint.Y},npc_standing=new[]{npc.StandingPixel.X,npc.StandingPixel.Y},native_radius_verified=Utility.withinRadiusOfPlayer(npc.StandingPixel.X,npc.StandingPixel.Y,1,p)});
        npc.checkAction(p,Game1.currentLocation);Current.phase="social_dialogue";nextInteraction=DateTime.UtcNow.AddMilliseconds(400);
    }
    private bool RelationshipResult(Friendship? friendship)=>socialItem switch {
        "(O)458"=>friendship?.IsDating()==true,
        "(O)460"=>Game1.player.spouse==socialName&&(friendship?.IsEngaged()==true||friendship?.IsMarried()==true),
        "(O)277"=>friendship?.IsDating()==false,
        _=>true // Other explicit relationship items retain consumption evidence; no marriage claim.
    };
}
