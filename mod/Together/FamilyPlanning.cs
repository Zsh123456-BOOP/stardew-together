using StardewValley;
using StardewValley.Locations;

namespace Together;
public sealed partial class ModEntry {
    private bool PursueFamily(ProgressPursuit pursuit,bool children) {
        var p=Game1.player;var policy=Data.Autoplay.Family;var budget=Data.Autoplay.Campaign;var actions=new List<(string Tool,object Args)>();
        string partner=p.spouse??policy.Partner;
        if(partner.Length==0){PursuitState(pursuit,"waiting","先用 strategy.family 选择婚恋对象；模型作一次关系取舍，算法负责后续原生步骤");return true;}
        var friendship=p.friendshipData.GetValueOrDefault(partner);
        if(p.isMarriedOrRoommates()) {
            if(!children){PursuitState(pursuit,"waiting","原生已婚状态待目录核验");return true;}
            if(p.getChildrenCount()>=2){PursuitState(pursuit,"waiting","原生家庭条件已达到；平台解锁单独核验");return true;}
            if(policy.AcceptChildren!=true||policy.TargetChildren<2||policy.ChildNames.Count<2){PursuitState(pursuit,"waiting","全家庭目标需要既定的两个孩子生育及命名策略");return true;}
            if(p.HouseUpgradeLevel<2)return PursueFamilyHouse(pursuit);
            if(Utility.getHomeOfFarmer(p).cribStyle.Value<=0){PursuitState(pursuit,"waiting","住宅育婴床已拆除，需要原生房屋装修恢复");return true;}
            PursueFriendship(pursuit,3000,partner);if(pursuit.State=="running")return true;
            PursuitState(pursuit,"waiting",friendship?.NextBirthingDate is {} date?"等待原生预产日 "+date.TotalDays:"维持婚后关系与正常夜晚，等待原生生育事件/孩子成长，不跳过随机条件");return true;
        }
        if(p.isEngaged()){PursuitState(pursuit,"waiting","已经订婚，按原生婚期正常生活并观看婚礼");return true;}
        if(!Game1.characterData.TryGetValue(partner,out var character)||!character.CanBeRomanced||friendship?.IsDivorced()==true){PursuitState(pursuit,"waiting","原生婚恋条件不满足，需要模型重新选择关系方向");return true;}
        int required=friendship?.IsDating()==true?2500:2000;
        if((friendship?.Points??0)<required){PursueFriendship(pursuit,required,partner);if(pursuit.State=="running")return true;if(p.HouseUpgradeLevel<(children?2:1))return PursueFamilyHouse(pursuit);return true;}
        if(friendship?.IsDating()!=true)return PursueRelationshipItem(pursuit,partner,"(O)458");
        if(p.HouseUpgradeLevel<1)return PursueFamilyHouse(pursuit);
        if(p.Items.Any(i=>i?.QualifiedItemId=="(O)460")||SharedStorage().Any(s=>s.Chest.GetItemsForPlayer().Any(i=>i?.QualifiedItemId=="(O)460")))return PursueRelationshipItem(pursuit,partner,"(O)460");
        if(Game1.getLocationFromName("Beach") is not Beach beach){PursuitState(pursuit,"waiting","海滩未加载");return true;}
        if(!beach.bridgeFixed.Value) {
            if(!PursuitMaterials(pursuit,new[]{("(O)388",300,0)},actions))return true;
            actions.Add(("player.beach",new{mode="bridge"}));QueuePursuit(pursuit,actions);return true;
        }
        if(!beach.IsRainingHere()||Game1.timeOfDay>=1700){PursuitState(pursuit,"waiting","等待雨天白昼拜访海滩老水手；此时继续其它工作");return true;}
        if(p.Items.All(i=>i!=null)){QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="store"})});return true;}
        QueuePursuit(pursuit,new[]{("player.beach",(object)new{mode="pendant",budget=5000,keep_gold=budget.KeepGold})},5000);return true;
    }
    private bool PursueFamilyHouse(ProgressPursuit pursuit)=>PursueHouse(pursuit,Game1.player.HouseUpgradeLevel+1);
    private bool PursueRelationshipItem(ProgressPursuit pursuit,string partner,string item) {
        var p=Game1.player;var policy=Data.Autoplay.Campaign;
        int slot=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]?.QualifiedItemId==item,-1);
        if(slot>=0){QueuePursuit(pursuit,new[]{("player.social",(object)new{npc=partner,mode="relationship",item,slot})});return true;}
        if(SharedStorage().Any(s=>s.Chest.GetItemsForPlayer().Any(i=>i?.QualifiedItemId==item))){QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="withdraw",item,count=1})});return true;}
        if(item!="(O)458")return false;
        if(Game1.timeOfDay<900||Game1.timeOfDay>=1600||Game1.dayOfMonth%7==3&&!p.mailReceived.Contains("ccIsComplete")){PursuitState(pursuit,"waiting","花束需在皮埃尔营业时间购买，先推进其它工作");return true;}
        if(p.Items.All(i=>i!=null)){QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="store"})});return true;}
        QueuePursuit(pursuit,new[]{("player.service",(object)new{service="shop",shop="SeedShop",location="SeedShop"}),("player.buy",(object)new{shop="SeedShop",item,count=1,max_unit_price=200,budget=200,keep_gold=policy.KeepGold})},200);return true;
    }
}
