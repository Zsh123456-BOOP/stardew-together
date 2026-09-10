namespace Together;

public sealed record CapacityCandidate(string Id,int Priority,double Cost,bool Authorized,string Excluded,IReadOnlyList<CapacityOp> Operations);
public sealed record CapacityCandidateResult(string Id,bool Eligible,string Reason,int FreeSlotsAfter,double Cost);
public sealed record CapacityReliefVerdict(string? Selected,string RootCause,IReadOnlyList<CapacityCandidateResult> Candidates);
public static class CapacityRelief {
    public static CapacityReliefVerdict Choose(CapacitySnapshot start,IReadOnlyList<CapacityCandidate> candidates,int depth) {
        var rows=new List<CapacityCandidateResult>();
        foreach(var c in candidates) {
            string reason=depth>=1?"relief_depth_limit":!c.Authorized?"explicit_authorization_required":c.Excluded;
            var verdict=reason.Length==0?CapacityPlan.Simulate(start,c.Operations):null;
            if(verdict is {Feasible:false})reason=verdict.Reason+":step="+verdict.FailedStep;
            if(verdict is {Feasible:true}&&verdict.FreeSlotsAtEnd<=start.FreeSlots)reason="does_not_release_a_slot";
            rows.Add(new(c.Id,reason.Length==0,reason,verdict?.FreeSlotsAtEnd??start.FreeSlots,c.Cost));
        }
        var selected=candidates.Where(c=>rows.Any(r=>r.Id==c.Id&&r.Eligible)).OrderBy(c=>c.Priority).ThenBy(c=>c.Cost).FirstOrDefault();
        return new(selected?.Id,selected==null?"capacity_all_candidates_infeasible":"",rows);
    }
}
