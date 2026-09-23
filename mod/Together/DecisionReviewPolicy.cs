using System.Text.Json;
namespace Together;

// Feasibility belongs to observed facts; relative value remains the model's choice.
public sealed record ReviewOption(string id,float? energy,int minutes,string scope,int owned_seeds=0);
public static class DecisionReviewPolicy {
    public static ReviewOption[] Select(IEnumerable<ReviewOption> options)=>options
        .OrderBy(o=>o.id is "water" or "care:dry_crops" or "harvest"?0:o.owned_seeds>0?1:o.id.Contains("seed")||o.id=="inspect:current-shop"?2:o.energy==0?3:4)
        .GroupBy(o=>o.owned_seeds==0&&(o.id.Contains("seed")||o.id=="inspect:current-shop")?"seed-quote-selection":o.id).Select(g=>g.First()).Take(3).ToArray();
    private static string Text(JsonElement a,string k)=>a.ValueKind==JsonValueKind.Object&&a.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    public static string? Sleep(JsonElement args,float stamina,int workMinutes,IReadOnlyList<ReviewOption> options) {
        if(Text(args,"reason").Trim().Length<3)return "decision_review:sleep_reason_required";
        // The model acknowledges which observed work it elects to postpone;
        // numeric feasibility remains a program fact, not a prose examination.
        if(args.TryGetProperty("defer",out var deferred)) {
            if(deferred.ValueKind!=JsonValueKind.Array||deferred.GetArrayLength()>3||deferred.EnumerateArray().Any(v=>v.ValueKind!=JsonValueKind.String))return "decision_review:defer_expected_ids_max_3";
            var ids=deferred.EnumerateArray().Select(v=>v.GetString()!).ToArray();
            if(ids.Distinct().Count()!=ids.Length||ids.Any(id=>!options.Any(o=>o.id==id)))return "decision_review:deferred_options_changed";
            return options.Any(o=>!ids.Contains(o.id))?"decision_review:acknowledge_remaining_work":null;
        }
        if(!args.TryGetProperty("alternatives",out var rows))return options.Count==0?null:"decision_review:alternatives_required";
        if(rows.ValueKind!=JsonValueKind.Array||rows.GetArrayLength()>3)return "decision_review:alternatives_expected_max_3";
        var seen=new HashSet<string>();
        foreach(var row in rows.EnumerateArray()) {
            string id=Text(row,"id"),reason=Text(row,"because"),note=Text(row,"detail");
            if(!seen.Add(id)||options.FirstOrDefault(o=>o.id==id) is not {} option)return "decision_review:alternative_changed_or_duplicate:"+id;
            if(note.Trim().Length<3)return "decision_review:alternative_detail_required:"+id;
            switch(reason) {
                case "energy":if(!option.energy.HasValue)return "decision_review:energy_unknown_not_infeasible:"+id;if(option.energy<=stamina)return "decision_review:energy_contradiction:"+id;break;
                case "time":if(option.minutes<=workMinutes)return "decision_review:time_contradiction:"+id;break;
                case "low_value":case "defer":
                    if(!row.TryGetProperty("value",out var value)||value.ValueKind!=JsonValueKind.Object||Text(value,"today").Trim().Length<3||Text(value,"defer").Trim().Length<3)
                        return "decision_review:compare_today_and_deferred_future_value:"+id;
                    if(option.owned_seeds>0) {
                        if(!value.TryGetProperty("owned_seeds",out var owned)||owned.ValueKind!=JsonValueKind.Number||!owned.TryGetInt32(out int n)||n!=option.owned_seeds)return "decision_review:owned_seed_count_changed:"+id;
                        if(!value.TryGetProperty("additional_seed_cost",out var cost)||cost.ValueKind!=JsonValueKind.Number||!cost.TryGetInt32(out int c)||c!=0)return "decision_review:owned_seeds_need_no_new_purchase:"+id;
                        if(Text(value,"growth_tradeoff").Trim().Length<3)return "decision_review:deferred_planting_growth_tradeoff_required:"+id;
                    }
                    break;
                default:return "decision_review:invalid_comparison_reason:"+id;
            }
        }
        return options.Any(o=>!seen.Contains(o.id))?"decision_review:compare_current_alternatives":null;
    }
    public static string? Resource(JsonElement args,int missing,int reserve,int pendingEnergy) {
        if(missing<=0)return null;
        if(!args.TryGetProperty("labor_review",out var review)||review.ValueKind!=JsonValueKind.Object)return "decision_review:resource_purpose_and_reserve_required";
        if(Text(review,"purpose").Trim().Length<3||Text(review,"followup").Trim().Length<3)return "decision_review:resource_purpose_and_followup_required";
        if(!args.TryGetProperty("reserve_stamina",out var r)||r.ValueKind!=JsonValueKind.Number||!r.TryGetInt32(out _)||reserve<0)return "decision_review:explicit_stamina_reserve_required";
        string care=Text(review,"care");
        if(care is not ("preserve" or "defer"))return "decision_review:care_must_be_preserve_or_defer";
        if(care=="preserve"&&reserve<pendingEnergy)return "decision_review:reserve_below_pending_care";
        if(care=="defer"&&Text(review,"tradeoff").Trim().Length<3)return "decision_review:deferred_care_tradeoff_required";
        return null;
    }
}

// Counters are per action and root error; unrelated mistakes never combine into
// a global halt. Runtime includes native progress/day in the key.
public sealed class DecisionReviewAttempts {
    private readonly Dictionary<string,int> counts=new();
    public int Count {get;private set;}
    public bool Reject(string current) {if(counts.Count>256)counts.Clear();Count=counts[current]=counts.GetValueOrDefault(current)+1;return Count>=3;}
    public void Clear(){counts.Clear();Count=0;}
}
