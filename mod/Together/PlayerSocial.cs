using System.Text.Json;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Quests;

namespace Together;

public sealed partial class PlayerExecutor {
    private string socialName="",socialMode="",socialItem="",socialQuest="";
    private int socialSlot,socialStack,socialPoints,socialGifts,socialPages;
    private bool socialTalked;
    private NPC? socialNpc;
    private Quest? socialQuestObject;
    private object SocialEvidence()=>new {
        npc=socialName,mode=socialMode,
        friendship_before=socialPoints,friendship_after=Game1.player.friendshipData.GetValueOrDefault(socialName)?.Points??0,
        gifts_before=socialGifts,gifts_after=Game1.player.friendshipData.GetValueOrDefault(socialName)?.GiftsToday??0,
        talked_before=socialTalked,talked_after=Game1.player.friendshipData.GetValueOrDefault(socialName)?.TalkedToToday??false,
        item=socialItem,consumed=socialStack-Game1.player.Items.Where(i=>i?.QualifiedItemId==socialItem).Sum(i=>i.Stack),
        quest=socialQuest,quest_completed=socialQuestObject?.completed.Value==true
        ,relationship_status=Game1.player.friendshipData.GetValueOrDefault(socialName)?.Status.ToString(),spouse=Game1.player.spouse
    };
    private void StartSocial(JsonElement args) {
        socialName=AgentToolRegistry.Text(args,"npc");socialMode=AgentToolRegistry.Text(args,"mode","talk");socialQuest=AgentToolRegistry.Text(args,"quest_id","");
        if(socialMode is not ("talk" or "gift" or "deliver" or "relationship"))throw new InvalidOperationException("invalid_social_mode");
        socialNpc=Game1.getCharacterFromName(socialName)??throw new InvalidOperationException("unknown_npc");
        if(socialNpc.IsMonster||socialNpc.currentLocation==null)throw new InvalidOperationException("npc_not_available");
        socialQuestObject=socialMode=="deliver"?NativeQuestIdentity.Find(socialQuest):null;
        if(socialMode=="deliver"&&(socialQuestObject==null||socialQuestObject.completed.Value))throw new InvalidOperationException("active_quest_id_required");
        socialSlot=AgentToolRegistry.Number(args,"slot",-1);socialItem="";socialStack=0;socialPages=0;
        if(socialMode is "gift" or "relationship" || socialMode=="deliver"&&socialSlot>=0) {
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
        destination=socialNpc.currentLocation.NameOrUniqueName;Current!.phase="social_travel";
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
                "deliver"=>socialQuestObject?.completed.Value==true,
                "gift"=>(f?.GiftsToday??0)>socialGifts,
                _=>p.Items.Where(i=>i?.QualifiedItemId==socialItem).Sum(i=>i.Stack)<socialStack&&RelationshipResult(f)
            };
            Current.effects.Add(SocialEvidence());Finish(complete?"succeeded":"failed",complete?null:"native_social_goal_not_verified");return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("social_travel_interrupted_by_menu");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!p.CanMove)return;
        var npc=socialNpc??throw new InvalidOperationException("npc_missing");
        if(npc.currentLocation==null||npc.IsInvisible||npc.isSleeping.Value)throw new InvalidOperationException("npc_unavailable_or_sleeping");
        string next=npc.currentLocation.NameOrUniqueName;
        if(destination!=next){destination=next;edge=null;StopWalk();}
        if(Game1.currentLocation.NameOrUniqueName!=destination){Current.phase="social_travel";Travel();return;}
        if(Math.Abs(p.TilePoint.X-npc.TilePoint.X)+Math.Abs(p.TilePoint.Y-npc.TilePoint.Y)>1) {
            if(DateTime.UtcNow>=nextInteraction){nextInteraction=DateTime.UtcNow.AddMilliseconds(500);Walk(Approach(npc.TilePoint,true));}
            else if(ownedController!=null)MonitorWalk();Current.phase="social_approach";return;
        }
        StopWalk();p.CurrentToolIndex=socialSlot;p.netItemStowed.Value=false;Face(npc.TilePoint);
        if(socialItem.Length>0) {
            var item=p.ActiveObject;
            if(item?.QualifiedItemId!=socialItem)throw new InvalidOperationException("social_item_changed");
            var matching=p.questLog.Where(q=>!q.completed.Value&&q.OnItemOfferedToNpc(npc,item,true)).ToArray();
            if(socialMode=="deliver"&&!matching.Any(q=>NativeQuestIdentity.Id(q)==socialQuest))throw new InvalidOperationException("quest_does_not_accept_this_item_or_npc");
            if(socialMode!="deliver"&&(matching.Length>0||p.team.specialOrders.Any(o=>o.onItemDelivered?.GetInvocationList().Cast<Func<Farmer,NPC,Item,bool,int>>().Any(f=>f(p,npc,item,true)>0)==true)))
                throw new InvalidOperationException("gift_would_deliver_quest_item_use_delivery");
            int used=socialMode=="deliver"?item.Stack:1;
            ValidateConsumption?.Invoke(new Dictionary<Item,int>{{item,used}},"",socialMode=="deliver"?"quest:"+socialQuest:"");
        } else if(socialMode=="deliver") {
            var q=p.questLog.First(q=>NativeQuestIdentity.Id(q)==socialQuest);
            string? recipient=q switch {ResourceCollectionQuest r=>r.target.Value,FishingQuest f=>f.target.Value,SlayMonsterQuest s=>s.target.Value,_=>null};
            if(recipient!=socialName)throw new InvalidOperationException("quest_needs_item_or_different_recipient");
        }
        // One genuine nearby interaction routes through native quest, gift and dialogue logic.
        // The boolean can be false for a valid temporary dialogue: verify state after dismissal.
        npc.checkAction(p,Game1.currentLocation);Current.phase="social_dialogue";nextInteraction=DateTime.UtcNow.AddMilliseconds(400);
    }
    private bool RelationshipResult(Friendship? friendship)=>socialItem switch {
        "(O)458"=>friendship?.IsDating()==true,
        "(O)460"=>Game1.player.spouse==socialName&&(friendship?.IsEngaged()==true||friendship?.IsMarried()==true),
        "(O)277"=>friendship?.IsDating()==false,
        _=>true // Other explicit relationship items retain consumption evidence; no marriage claim.
    };
}
