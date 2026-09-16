using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    // Item IDs belong to the audited native-source adapter, never to recursive
    // recipe expansion. A new recipe using an existing material needs no branch.
    private bool PrepareResourceMaterial(SharedGoal goal,GoalNode node,Action<string,object,string> add) {
        string skill=ResourceRules.WorkKind(node.Item);if(skill.Length==0||node.Quality!=0)return false;
        string? location=FindGoalResourceLocation(node.Item,skill);if(location==null)return false;
        int count=Math.Min(node.ToPrepare,999);
        bool trees=skill=="wood";
        add("work.run",new{goal=skill,item=node.Item,count,location,include_trees=trees,goal_id=goal.Id},"为"+goal.Title+"准备"+node.Name+"，剩余 "+node.ToPrepare);
        return true;
    }
}
