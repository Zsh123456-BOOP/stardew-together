using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    // Item IDs belong to the audited native-source adapter, never to recursive
    // recipe expansion. A new recipe using an existing material needs no branch.
    private bool PrepareResourceMaterial(SharedGoal goal,GoalNode node,Action<string,object,string> add) {
        string skill=ResourceRules.WorkKind(node.Item);if(skill.Length==0||node.Quality!=0)return false;
        string? location=FindGoalResourceLocation(node.Item,skill);if(location==null)return false;
        // Small debris is a lower-energy source with no falling-tree scatter.
        // Commit only its observed prefix; a later refresh may choose trees.
        int small=skill=="wood"?Game1.getLocationFromName(location)?.objects.Values.Count(o=>o.IsTwig())??0:0;
        int count=Math.Min(node.ToPrepare,999);if(small>0)count=Math.Min(count,small);
        bool trees=skill=="wood"&&small==0;
        add("work.run",new{goal=skill,item=node.Item,count,location,include_trees=trees,goal_id=goal.Id},"为"+goal.Title+"准备"+node.Name+"，剩余 "+node.ToPrepare);
        return true;
    }
}
