namespace Together;

// A bounded dependency frontier, not a search over game inputs. Nodes are
// predicates from a detached snapshot; operators name reusable domain skills.
public sealed class ProgressPlanNode {
    public string Id {get;set;}="";
    public bool Met {get;set;}
    public string Operator {get;set;}="";
    public string Wait {get;set;}="";
    public double Cost {get;set;}=1;
    public int Deadline {get;set;}=int.MaxValue;
    public List<ProgressPlanRoute> Routes {get;set;}=new();
}
public sealed record ProgressPlanRoute(string[] Children,int Required=-1);
public sealed record ProgressFrontier(string Node,string Operator,double Cost,int Deadline,string[] Roots);
public sealed record ProgressGraphResult(IReadOnlyList<ProgressFrontier> Frontier,IReadOnlyDictionary<string,string[]> Gaps,int Visited,bool Truncated);
public static class ProgressGraph {
    private sealed record Evaluation(bool Met,List<ProgressFrontier> Ready,List<string> Gaps);
    public static ProgressGraphResult Plan(IReadOnlyDictionary<string,ProgressPlanNode> nodes,IEnumerable<string> requested,int maxNodes=4096,int maxDepth=32) {
        var cache=new Dictionary<string,Evaluation>();var stack=new HashSet<string>();int visited=0;bool truncated=false;
        Evaluation Gap(string reason)=>new(false,new(),new(){reason});
        Evaluation Visit(string id,int depth) {
            if(stack.Contains(id))return Gap("dependency_cycle:"+id);
            if(cache.TryGetValue(id,out var cached))return cached;
            if(depth>maxDepth||++visited>maxNodes){truncated=true;return Gap("planning_limit:"+id);}
            if(!nodes.TryGetValue(id,out var node))return Gap("missing_dependency:"+id);
            if(node.Met)return cache[id]=new(true,new(),new());
            stack.Add(id);
            var routes=new List<Evaluation>();
            foreach(var route in node.Routes) {
                var children=route.Children.Distinct(StringComparer.Ordinal).ToArray();int required=route.Required<0?children.Length:route.Required;
                if(required<0||required>children.Length){routes.Add(Gap("invalid_dependency_threshold:"+id));continue;}
                var results=children.Select(child=>Visit(child,depth+1)).ToArray();int remaining=Math.Max(0,required-results.Count(r=>r.Met));
                if(remaining==0){routes.Add(Own(node));continue;}
                // For collect-K-of-N goals, prefer currently executable branches;
                // don't demand every optional recipe/fish before making progress.
                var pending=results.Where(r=>!r.Met).OrderBy(r=>r.Ready.Count==0).ThenBy(r=>r.Ready.Count==0?double.MaxValue:r.Ready.Min(a=>a.Cost)).Take(remaining).ToArray();
                routes.Add(new(false,pending.SelectMany(r=>r.Ready).ToList(),pending.SelectMany(r=>r.Gaps).ToList()));
            }
            stack.Remove(id);
            if(routes.Count==0)return cache[id]=Own(node);
            // Alternatives are plans to choose from, not cumulative obligations.
            return cache[id]=routes.OrderBy(r=>r.Ready.Count==0).ThenBy(r=>r.Gaps.Count).ThenBy(r=>r.Ready.Sum(a=>a.Cost)).First();
        }
        Evaluation Own(ProgressPlanNode node)=>node.Wait.Length>0?Gap(node.Wait):node.Operator.Length==0?Gap("await_native_predicate:"+node.Id):new(false,new(){new(node.Id,node.Operator,Math.Max(0,node.Cost),node.Deadline,Array.Empty<string>())},new());
        var combined=new Dictionary<string,ProgressFrontier>();var gaps=new Dictionary<string,string[]>();
        foreach(string root in requested.Distinct(StringComparer.Ordinal)) {
            var result=Visit(root,0);gaps[root]=result.Gaps.Distinct().ToArray();
            foreach(var candidate in result.Ready) {
                if(combined.TryGetValue(candidate.Node,out var old))combined[candidate.Node]=old with{Roots=old.Roots.Append(root).Distinct().ToArray()};
                else combined[candidate.Node]=candidate with{Roots=new[]{root}};
            }
        }
        return new(combined.Values.OrderBy(a=>a.Deadline).ThenByDescending(a=>a.Roots.Length).ThenBy(a=>a.Cost).ThenBy(a=>a.Node,StringComparer.Ordinal).ToArray(),gaps,visited,truncated);
    }
}
