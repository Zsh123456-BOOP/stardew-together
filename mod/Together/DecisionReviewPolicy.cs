using System.Text.Json;
namespace Together;

// Feasibility belongs to observed facts; relative value remains the model's choice.
public sealed record ReviewOption(string id,float? energy,int minutes,string scope);
public static class DecisionReviewPolicy {
    private static string Text(JsonElement a,string k)=>a.ValueKind==JsonValueKind.Object&&a.TryGetProperty(k,out var v)&&v.ValueKind==JsonValueKind.String?v.GetString()??"":"";
    public static string? Sleep(JsonElement args,float stamina,int workMinutes,IReadOnlyList<ReviewOption> options) {
        if(Text(args,"reason").Trim().Length<3)return "decision_review:sleep_reason_required";
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
                case "low_value":case "defer":break;
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

// Reads and clock ticks cannot reset a repeated rejected decision. Verified native
// progress or a new day can; changing the wording of the mistake cannot.
public sealed class DecisionReviewAttempts {
    private string basis="";
    public int Count {get;private set;}
    public bool Reject(string current) {if(current!=basis){basis=current;Count=0;}return ++Count>=3;}
    public void Clear(){basis="";Count=0;}
}
