using System.Text.Json;
using StardewValley;
using StardewValley.Quests;

namespace Together;
public sealed partial class ModEntry {
    private bool PursueStoryQuest(ProgressPursuit pursuit,Quest quest) {
        string handle=NativeQuestIdentity.Id(quest);var p=Game1.player;
        if(quest is CraftingQuest craft) {
            var recipe=goalRecipes.Values.FirstOrDefault(r=>r.Item==craft.ItemId.Value&&r.Known&&r.Kind is "craft" or "cook");
            if(recipe==null){PursuitState(pursuit,"waiting","先解锁任务物品的原生配方");return true;}
            var goal=(SharedGoal)AgentGoalCreate(JsonSerializer.SerializeToElement(new{request_id="quest-craft-"+handle+"-"+(++pursuit.Revision),entity=recipe.Id,count=1,completion=recipe.Kind=="cook"?"cooked":"crafted"}));
            pursuit.ChildGoal=goal.Id;AgentGoalRun(JsonSerializer.SerializeToElement(new{id=goal.Id}));PursuitState(pursuit,"running","按原生制作事件推进任务，不以已有成品代替");return true;
        }
        if(quest is GoSomewhereQuest visit) {
            var actions=new List<(string Tool,object Args)>();string target=visit.whereToGo.Value;
            if(Game1.currentLocation.NameOrUniqueName==target) {
                var neighbour=Game1.locations.FirstOrDefault(l=>l!=Game1.currentLocation&&PlayerExecutor.NextExit(Game1.currentLocation,l.NameOrUniqueName)!=null);
                if(neighbour==null){PursuitState(pursuit,"waiting","当前地图没有已知可走出口，不能伪造到达事件");return true;}
                actions.Add(("player.travel",new{location=neighbour.NameOrUniqueName}));
            }
            actions.Add(("player.travel",new{location=target}));QueuePursuit(pursuit,actions);return true;
        }
        if(quest is HaveBuildingQuest building) {
            var definition=ReadNativeGoalRows().FirstOrDefault(n=>n.id=="build:"+building.buildingType.Value);
            if(definition==null){PursuitState(pursuit,"waiting","建筑类型尚无原生图纸");return true;}
            if(definition.completed==true){PursuitState(pursuit,"waiting","建筑已存在，等待原生任务检查");return true;}
            // Reuse the building planner with the same budget and child receipts;
            // the parent target is still verified by its native quest completion.
            string parent=pursuit.Target;try{pursuit.Target=definition.id;return TryAdvanceNativePursuit(pursuit,definition);}finally{pursuit.Target=parent;}
        }
        if(quest is SocializeQuest greet) {
            var npc=greet.whoToGreet.Select(name=>Game1.getCharacterFromName(name)).Where(n=>n!=null&&!n.IsInvisible&&!n.isSleeping.Value&&n.currentLocation!=null)
                .OrderBy(n=>n.currentLocation==Game1.currentLocation?0:1).FirstOrDefault(n=>n.currentLocation==Game1.currentLocation||PlayerExecutor.NextExit(Game1.currentLocation,n.currentLocation.NameOrUniqueName)!=null);
            if(npc==null){PursuitState(pursuit,"waiting","尚未介绍的人物当前不可达，保留名单稍后继续");return true;}
            QueuePursuit(pursuit,new[]{("player.social",(object)new{npc=npc.Name,mode="greet",quest_id=handle})});return true;
        }
        if(quest is LostItemQuest lost) {
            var carried=p.Items.FirstOrDefault(i=>i?.QualifiedItemId==lost.ItemId.Value);
            if(lost.itemFound.Value&&carried!=null){QueuePursuit(pursuit,new[]{("player.social",(object)new{npc=lost.npcName.Value,mode="deliver",quest_id=handle})});return true;}
            if(lost.itemFound.Value) {
                var actions=new List<(string Tool,object Args)>();if(!PursuitMaterials(pursuit,new[]{(lost.ItemId.Value,1,0)},actions))return true;QueuePursuit(pursuit,actions);return true;
            }
            QueuePursuit(pursuit,new[]{("player.find_lost_item",(object)new{quest_id=handle})});return true;
        }
        if(quest is ItemHarvestQuest harvest) {
            var actions=new List<(string Tool,object Args)>();
            var node=new GoalNode{Item=harvest.ItemId.Value,Required=Math.Clamp(harvest.Number.Value,1,999)};
            if(PrepareLivingMaterial(new SharedGoal{Item=node.Item},node,(t,a,_)=>actions.Add((t,a)),out string wait)) {
                if(actions.Count>0)QueuePursuit(pursuit,actions);else PursuitState(pursuit,"waiting",wait);return true;
            }
            PursuitState(pursuit,"waiting","尚无目标作物/野采/种子；保留原生收获任务，先取得真实种子或来源");return true;
        }
        return false;
    }
}
