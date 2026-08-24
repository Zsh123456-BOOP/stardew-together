using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private bool PursueFriendship(ProgressPursuit pursuit,int threshold,string? specific=null) {
        var p=Game1.player;var policy=Data.Autoplay.Campaign;bool reachable=false;
        foreach(var pair in Game1.characterData.Where(c=>(specific==null||c.Key==specific)&&!GameStateQuery.IsImmutablyFalse(c.Value.CanSocialize))
            .OrderByDescending(c=>Game1.getCharacterFromName(c.Key)?.isBirthday()==true).ThenByDescending(c=>p.friendshipData.GetValueOrDefault(c.Key)?.Points??0)) {
            var npc=Game1.getCharacterFromName(pair.Key);var f=p.friendshipData.GetValueOrDefault(pair.Key);
            if(npc==null||npc.IsMonster||npc.currentLocation==null||(f?.Points??0)>=threshold||!GameStateQuery.CheckConditions(pair.Value.CanSocialize,npc.currentLocation,p,random:new Random(0)))continue;
            if(pair.Value.CanBeRomanced&&threshold>2000&&f?.IsDating()!=true&&f?.IsMarried()!=true)continue;
            if(npc.currentLocation!=Game1.currentLocation&&PlayerExecutor.NextExit(Game1.currentLocation,npc.currentLocation.NameOrUniqueName)==null)continue;
            reachable=true;var actions=new List<(string Tool,object Args)>();
            if(f?.TalkedToToday!=true)actions.Add(("player.social",new{npc=pair.Key,mode="talk"}));
            bool gift=(f?.GiftsToday??0)==0&&((f?.GiftsThisWeek??0)<2||npc.isBirthday()||f?.IsMarried()==true);
            int spent=0;
            if(gift&&policy.GiftValuePerDay>policy.ReservedGiftValue) {
                var choices=new List<(int Slot,Item Item,int Taste,int Value)>();
                for(int slot=0;slot<p.Items.Count;slot++) {
                    if(p.Items[slot] is not StardewValley.Object item||item.bigCraftable.Value||item.questItem.Value||item.Category==-74||item.QualifiedItemId is "(O)458" or "(O)460" or "(O)808" or "(O)809"||item.GetContextTags().Any(t=>t.StartsWith("propose_roommate_")))continue;
                    int value=Math.Max(0,item.sellToStorePrice());if(value>policy.GiftValueLimit||value>policy.GiftValuePerDay-policy.ReservedGiftValue)continue;
                    int taste=npc.getGiftTasteForThisItem(item);if(taste is not (0 or 2))continue;
                    if(Facts.Bundles.Where(b=>!b.Complete).SelectMany(b=>b.Missing).Any(n=>n.Item==item.QualifiedItemId)||Facts.Goals.Where(q=>!q.Complete&&q.Kind!="craft").SelectMany(q=>q.Needs).Any(n=>n.Item==item.QualifiedItemId))continue;
                    if(!ReservationAllocation.Preserves(Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category}),AllReservations(),new[]{new GoalStock{Item=item.QualifiedItemId,Quality=item.Quality,Category=item.Category,Count=1}}))continue;
                    choices.Add((slot,item,taste,value));
                }
                var selected=choices.OrderBy(c=>c.Taste).ThenBy(c=>c.Value).FirstOrDefault();
                if(selected.Item!=null){actions.Add(("player.social",new{npc=pair.Key,mode="gift",slot=selected.Slot,item=selected.Item.QualifiedItemId}));spent=selected.Value;}
            }
            if(actions.Count>0) {
                if(QueuePursuit(pursuit,actions))policy.ReservedGiftValue+=spent;
                return true;
            }
        }
        PursuitState(pursuit,"waiting",reachable?"今日可达人物已聊天；合格礼物/每周次数/礼物价值预算暂不可用，继续其它工作":"关系目标需可达人物、原生社交条件或先确定恋爱分支");return true;
    }
}
