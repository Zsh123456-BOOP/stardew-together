namespace Together;

// Compare recipes on isolated ledgers. Costs rank alternatives; they never
// authorize an action or turn forecast production into observed inventory.
public static class GoalRoutes {
    public static GoalRecipe? Select(IEnumerable<GoalRecipe> alternatives,IReadOnlyDictionary<string,GoalRecipe> rules,GoalLedger available,int amount,bool facilities) {
        int visits=0;
        double Cost(GoalRecipe recipe,int count,GoalLedger stock,HashSet<string> ancestors,int depth) {
            if(++visits>512||depth>6||!ancestors.Add(recipe.Item))return double.PositiveInfinity;
            double cost=recipe.Known?0:100000;
            int batches=(int)Math.Ceiling(count/(double)Math.Max(1,recipe.Output));
            cost+=batches;
            if(recipe.Kind=="process"&&!recipe.Known&&!facilities)return double.PositiveInfinity;
            foreach(var input in recipe.Inputs.OrderBy(i=>IsCategory(i.Item)).ThenByDescending(i=>i.Quality)) {
                long total=(long)batches*input.Count;if(total is <1 or >100000)return double.PositiveInfinity;
                int missing=(int)total-stock.Take(input.Item,(int)total,input.Quality);if(missing==0)continue;
                var routes=rules.Values.Where(r=>r.Item==input.Item&&r.Supported&&r.Known).OrderBy(r=>r.Id,StringComparer.Ordinal).ToArray();
                double best=double.PositiveInfinity;GoalLedger? chosen=null;
                foreach(var r in routes){var copy=stock.Clone();double score=Cost(r,missing,copy,new(ancestors),depth+1);if(score<best){best=score;chosen=copy;}}
                if(chosen!=null){stock.Replace(chosen);cost+=best;}
                else if(routes.Length==0)cost+=missing*100.0; // acquisition is still checked by the native capability adapter
                else return double.PositiveInfinity;
            }
            int surplus=batches*Math.Max(1,recipe.Output)-count;if(surplus>0)stock.Add(recipe.Item,surplus);
            return cost;
        }
        return alternatives.Where(r=>r.Supported).OrderBy(r=>r.Id,StringComparer.Ordinal)
            .Select(r=>(Rule:r,Score:Cost(r,amount,available.Clone(),new(),0)))
            .OrderBy(r=>r.Score).ThenBy(r=>r.Rule.Id,StringComparer.Ordinal).Select(r=>r.Rule).FirstOrDefault();
    }
    public static bool IsCategory(string item)=>int.TryParse(item.Replace("(O)",""),out int category)&&category<0;
}
