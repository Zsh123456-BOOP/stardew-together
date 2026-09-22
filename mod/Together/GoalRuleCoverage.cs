using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    internal object GoalRuleCoverage(JsonElement args) {
        RefreshFacts(true);ReadGoalRecipes();string item=AgentToolRegistry.Text(args,"item");
        int offset=Math.Max(0,AgentToolRegistry.Number(args,"offset",0));
        var parsed=goalRecipes.Values.Where(r=>item.Length==0||r.Item==item||r.Id==item||r.Facility==item).OrderBy(r=>r.Id,StringComparer.Ordinal).ToArray();
        var unparsed=new List<object>();
        foreach(var key in DataLoader.CraftingRecipes(Game1.content).Keys)if(!goalRecipes.ContainsKey("craft:"+key))unparsed.Add(new{id="craft:"+key,reason="unparsed_native_recipe_shape"});
        foreach(var key in DataLoader.CookingRecipes(Game1.content).Keys)if(!goalRecipes.ContainsKey("cook:"+key))unparsed.Add(new{id="cook:"+key,reason="unparsed_native_recipe_shape"});
        foreach(var machine in DataLoader.Machines(Game1.content))foreach(var rule in machine.Value.OutputRules??new()) {
            string id="process:"+machine.Key+":"+rule.Id;
            if(!goalRecipes.ContainsKey(id)&&(item.Length==0||machine.Key==item||rule.OutputItem?.Any(o=>o.ItemId==item)==true))unparsed.Add(new{id,status="valuation_unknown",execution="native_player.machine_observation_required",reason="conditional_random_dynamic_or_custom_machine_rule_requires_adapter",inputs=rule.Triggers?.Select(t=>new{t.RequiredItemId,t.RequiredCount,t.RequiredTags,t.Condition}),outputs=rule.OutputItem?.Select(o=>new{o.ItemId,o.OutputMethod,o.RandomItemId}),estimated_margin=(double?)null});
        }
        return new{rules=parsed.Skip(offset).Take(24).Select(r=>new{recipe=r,status=!r.Known||r.Inputs.Any(i=>AccessibleStock(i.Item)<i.Count)?"prerequisites_missing":"supported",proof="parsed_native_data_not_completed_production"}),total=parsed.Length,next_offset=offset+24<parsed.Length?(int?)(offset+24):null,
            coverage=new{parsed=goalRecipes.Count,unparsed=unparsed.Count,unparsed_rules=unparsed.Skip(offset).Take(24),unparsed_next_offset=offset+24<unparsed.Count?(int?)(offset+24):null},
            note="Known表示当前解锁/设施条件；解析并不证明运行成功。未解析规则是能力缺口，不是不存在该用途。特殊机制需要适配；不能通过材料齐全推断解锁。"};
    }
}
