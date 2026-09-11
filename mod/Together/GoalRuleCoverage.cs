using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    internal object GoalRuleCoverage(JsonElement args) {
        RefreshFacts(true);ReadGoalRecipes();string item=AgentToolRegistry.Text(args,"item");
        int offset=Math.Max(0,AgentToolRegistry.Number(args,"offset",0));
        var parsed=goalRecipes.Values.Where(r=>item.Length==0||r.Item==item||r.Id==item).OrderBy(r=>r.Id,StringComparer.Ordinal).ToArray();
        var unparsed=new List<object>();
        foreach(var key in DataLoader.CraftingRecipes(Game1.content).Keys)if(!goalRecipes.ContainsKey("craft:"+key))unparsed.Add(new{id="craft:"+key,reason="unparsed_native_recipe_shape"});
        foreach(var key in DataLoader.CookingRecipes(Game1.content).Keys)if(!goalRecipes.ContainsKey("cook:"+key))unparsed.Add(new{id="cook:"+key,reason="unparsed_native_recipe_shape"});
        foreach(var machine in DataLoader.Machines(Game1.content))foreach(var rule in machine.Value.OutputRules??new()) {
            string id="process:"+machine.Key+":"+rule.Id;
            if(!goalRecipes.ContainsKey(id))unparsed.Add(new{id,reason="conditional_random_dynamic_or_custom_machine_rule_requires_adapter"});
        }
        return new{rules=parsed.Skip(offset).Take(24),total=parsed.Length,next_offset=offset+24<parsed.Length?(int?)(offset+24):null,
            coverage=new{parsed=goalRecipes.Count,unparsed=unparsed.Count,unparsed_rules=unparsed.Skip(offset).Take(24),unparsed_next_offset=offset+24<unparsed.Count?(int?)(offset+24):null},
            note="Known表示当前解锁/设施条件；解析并不证明运行成功。未解析规则是能力缺口，不是不存在该用途。特殊机制需要适配；不能通过材料齐全推断解锁。"};
    }
}
