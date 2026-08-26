using System.Text.Json;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Monsters;
using StardewValley.Quests;

namespace Together;
public sealed partial class ModEntry {
    private bool PursueQuestAchievement(ProgressPursuit pursuit,int achievement) {
        if(JsonSerializer.SerializeToElement(AchievementRules.Native(achievement)).GetProperty("condition_satisfied").GetBoolean()){PursuitState(pursuit,"waiting","任务完成数量已达标，等待原生成就登记");return true;}
        var quests=Game1.player.questLog.Where(q=>q.dailyQuest.Value||q.questType.Value==7).ToArray();
        var reward=quests.FirstOrDefault(q=>q.completed.Value&&q.HasMoneyReward());
        if(reward!=null){QueuePursuit(pursuit,new[]{("player.claim_reward",(object)new{quest_id=NativeQuestIdentity.Id(reward)})});return true;}
        foreach(var quest in quests.Where(q=>!q.completed.Value).OrderBy(q=>q.daysLeft.Value>0?q.daysLeft.Value:int.MaxValue)) {
            if(PursueQuest(pursuit,quest)&&pursuit.State=="running")return true;
        }
        var today=Game1.questOfTheDay;
        if(today!=null&&!today.accepted.Value) {
            _=today.questTitle;_=today.questDescription;
            if(today is ItemDeliveryQuest or ResourceCollectionQuest or FishingQuest or SlayMonsterQuest) {
                QueuePursuit(pursuit,new[]{("player.service",(object)new{location="Town",service="daily_quests"}),("player.accept_quest",(object)new{id=NativeQuestIdentity.Id(today)})});return true;
            }
        }
        PursuitState(pursuit,"waiting","当前委托需等待时间/供给，或今日没有新委托；推进其它经营事项");return true;
    }
    private bool PursueQuest(ProgressPursuit pursuit,Quest quest) {
        var p=Game1.player;string id=NativeQuestIdentity.Id(quest);
        string? recipient=quest switch {ItemDeliveryQuest q=>q.target.Value,ResourceCollectionQuest q=>q.target.Value,FishingQuest q=>q.target.Value,SlayMonsterQuest q=>q.target.Value,_=>null};
        if(quest is ItemDeliveryQuest) {
            var actions=new List<(string Tool,object Args)>();var needs=Facts.Goals.First(g=>g.Id=="quest:"+id).Needs;
            if(!PursuitMaterials(pursuit,needs.Select(n=>(n.Item,n.Count,n.Quality)),actions))return true;
            if(actions.Count>0){QueuePursuit(pursuit,actions);return true;}
            var npc=Game1.getCharacterFromName(recipient);if(npc==null){PursuitState(pursuit,"waiting","交付对象尚不可见");return true;}
            int slot=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]!=null&&quest.OnItemOfferedToNpc(npc,p.Items[i],true),-1);
            if(slot<0){PursuitState(pursuit,"waiting","原生任务当前不接受背包物品");return true;}
            QueuePursuit(pursuit,new[]{("player.social",(object)new{npc=recipient,mode="deliver",quest_id=id,slot})});return true;
        }
        if(quest is not (ResourceCollectionQuest or FishingQuest or SlayMonsterQuest))return false;
        var progress=NativeQuestIdentity.Count(quest);int left=Math.Max(0,progress.Required-progress.Current);
        if(left==0) {
            if(string.IsNullOrEmpty(recipient)||recipient=="null"){PursuitState(pursuit,"waiting","行为计数已足，等待原生任务完成事件");return true;}
            QueuePursuit(pursuit,new[]{("player.social",(object)new{npc=recipient,mode="deliver",quest_id=id})});return true;
        }
        if(Game1.timeOfDay>=1900||p.health<50||p.Stamina<35){PursuitState(pursuit,"waiting","委托劳动需要补给与充足时间，先处理低消耗事项");return true;}
        if(quest is FishingQuest fish) {
            if(!FishingLocations(fish.ItemId.Value).Any()){PursuitState(pursuit,"waiting","目标鱼当前无可达且符合时段/季节/天气的钓点，或缺少鱼竿");return true;}
            QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="fish",item=fish.ItemId.Value,count=Math.Min(100,left),quest_id=id,until=2100})});return true;
        }
        if(quest is ResourceCollectionQuest resource) {
            string item=resource.ItemId.Value,skill=item switch{"(O)388"=>"wood","(O)390"=>"stone","(O)771"=>"fiber","(O)709"=>"hardwood",_=>"resource"};
            string? location=FindGoalResourceLocation(item,skill);
            if(location!=null) {QueuePursuit(pursuit,new[]{("work.run",(object)new{goal=skill,item,location,count=Math.Min(999,left),quest_id=id,include_trees=true,until=2100})});return true;}
            if(!ResourceRules.Nodes.Values.Contains(item)&&item!="(O)390"){PursuitState(pursuit,"waiting","目前没有可采集目标；不会用共享箱重复取出伪造新采集");return true;}
        }
        if(quest is SlayMonsterQuest slay) {
            // Probe only existing monsters; never instantiate floors to inspect them.
            var known=new[]{Game1.currentLocation}.Concat(MineShaft.activeMines).Distinct().FirstOrDefault(l=>
                (l==Game1.currentLocation||PlayerExecutor.NextExit(Game1.currentLocation,l.NameOrUniqueName)!=null)&&
                l.characters.OfType<Monster>().Any(m=>m.Health>0&&slay.OnMonsterSlain(l,m,false,false,true)));
            if(known!=null){QueuePursuit(pursuit,new[]{("player.travel",(object)new{location=known.NameOrUniqueName}),("player.combat",(object)new{count=1,quest_id=id,min_health=40})});return true;}
        }
        // Explore real unlocked mine checkpoints in bounded passes. This isn't
        // a prediction that a randomly generated floor contains the target.
        int highest=Math.Min(115,MineShaft.lowestLevelReached/5*5),start=(pursuit.Revision++%(highest/5+1))*5;
        QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="mine_trip",start_level=start,target_level=Math.Min(120,start+10),quest_id=id,until=2100})});return true;
    }
}
