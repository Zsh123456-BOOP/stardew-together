using StardewValley;

namespace Together;

// Keys are local to one simulation. A key is shared only after pairwise native
// agreement; unknown/modded variants never gain speculative stacking capacity.
internal sealed class CapacityAdapter {
    private readonly Dictionary<Item,StackKey> keys=new(ReferenceEqualityComparer.Instance);
    private readonly Dictionary<Item,int> limits=new(ReferenceEqualityComparer.Instance);
    public CapacitySnapshot Snapshot {get;}
    public int NativeComparisons {get;private set;}
    public int ConservativeSplits {get;private set;}
    public CapacityAdapter(Farmer who,IEnumerable<Item>? prospective=null,Func<Item,bool>? protect=null):this(who.MaxItems,who.Items,prospective,protect){}
    public CapacityAdapter(int slotCount,IEnumerable<Item?> inventory,IEnumerable<Item>? prospective=null,Func<Item,bool>? protect=null) {
        var items=inventory.Where(i=>i!=null).Cast<Item>().ToArray();
        var groups=new List<List<Item>>();
        foreach(var item in items.Concat(prospective??Array.Empty<Item>())) {
            if(keys.ContainsKey(item))continue;
            int limit=Math.Max(1,item.maximumStackSize());
            string basis=item.GetType().FullName+"|"+item.QualifiedItemId+"|"+item.Quality;
            var match=groups.FirstOrDefault(g=>g[0].GetType()==item.GetType()&&g[0].QualifiedItemId==item.QualifiedItemId&&g[0].Quality==item.Quality&&g.All(other=>Compatible(other,item)));
            if(match==null) {
                if(groups.Any(g=>g[0].QualifiedItemId==item.QualifiedItemId&&g[0].Quality==item.Quality))ConservativeSplits++;
                match=new List<Item>();groups.Add(match);
            }
            // Non-reflexive stack rules (tools etc.) must never merge a new unit
            // into this instance, even if a mod reports an optimistic stack limit.
            if(!Compatible(item,item))limit=Math.Max(1,item.Stack);
            keys[item]=new(item.QualifiedItemId,item.Quality,basis+"|group:"+groups.IndexOf(match));limits[item]=limit;match.Add(item);
        }
        Snapshot=new(slotCount,items.Select(i=>(keys[i],i.Stack,limits[i],protect?.Invoke(i)??false)));
    }
    private bool Compatible(Item a,Item b) {
        NativeComparisons++;
        try{return a.maximumStackSize()>1&&b.maximumStackSize()>1&&a.canStackWith(b)&&b.canStackWith(a);}
        catch{return false;}
    }
    public StackKey Key(Item item)=>keys[item];
    public int Limit(Item item)=>limits[item];
    public CapacityOp Take(Item item,int count)=>new TakeOp(keys[item],count);
    public CapacityOp Put(Item item,int? count=null)=>new PutOp(keys[item],count??item.Stack,limits[item]);
    public static CapacitySnapshot Of(Farmer who)=>new CapacityAdapter(who).Snapshot;
    public static CapacityVerdict Receive(Farmer who,Item item,int? count=null) {
        var a=new CapacityAdapter(who,new[]{item});return CapacityPlan.Simulate(a.Snapshot,new[]{a.Put(item,count)});
    }
    public static bool CanReceive(Farmer who,Item item,int? count=null)=>Receive(who,item,count).Feasible;
    public static void RequireReceive(Farmer who,Item item,int? count=null) {
        var verdict=Receive(who,item,count);if(!verdict.Feasible)throw new InvalidOperationException(verdict.Reason);
    }
    public static void RequireReceiveAll(Farmer who,IEnumerable<Item> outputs) {
        var items=outputs.ToArray();var a=new CapacityAdapter(who,items);
        var verdict=CapacityPlan.Simulate(a.Snapshot,items.Select(i=>a.Put(i)).ToArray());
        if(!verdict.Feasible)throw new InvalidOperationException(verdict.Reason);
    }
    public static bool HasSlots(Farmer who,int required)=>CapacityPlan.Simulate(Of(who),new CapacityOp[]{new RequireFreeOp(required)}).Feasible;
    public static void RequireSlots(Farmer who,int required) {
        if(!HasSlots(who,required))throw new InvalidOperationException("capacity_no_free_slot");
    }
    public static object StackAudit(IEnumerable<Item> specimens) {
        var items=specimens.Select(i=>{var copy=i.getOne();copy.Stack=1;return copy;}).ToArray();var rows=new List<object>();
        for(int i=0;i<items.Length;i++)for(int j=0;j<items.Length;j++) {
            var a=new CapacityAdapter(1,new[]{items[i]},new[]{items[j]});
            bool native=items[i].maximumStackSize()>1&&items[i].canStackWith(items[j])&&items[j].canStackWith(items[i]);
            bool predicted=CapacityPlan.Simulate(a.Snapshot,new[]{a.Put(items[j])}).Feasible;
            rows.Add(new{left=i,right=j,id=items[i].QualifiedItemId,quality=items[i].Quality,native,predicted,conservative=native&&!predicted,unsafe_match=predicted&&!native});
        }
        return rows;
    }
    public static CapacityVerdict After(Farmer who,IReadOnlyDictionary<Item,int> consumption,Item output) {
        var adapter=new CapacityAdapter(who,new[]{output});
        return CapacityPlan.Simulate(adapter.Snapshot,consumption.Select(p=>adapter.Take(p.Key,p.Value)).Append(adapter.Put(output)).ToArray());
    }
    public static Dictionary<Item,int> Ingredients(Farmer who,CraftingRecipe recipe) {
        var result=new Dictionary<Item,int>();
        foreach(var need in recipe.recipeList) {
            int left=need.Value;
            foreach(var item in who.Items.Reverse().Where(i=>i!=null&&CraftingRecipe.ItemMatchesForCrafting(i,need.Key))) {
                int take=Math.Min(left,item.Stack-result.GetValueOrDefault(item));
                if(take>0){result[item]=result.GetValueOrDefault(item)+take;left-=take;}if(left==0)break;
            }
            if(left>0)throw new InvalidOperationException("recipe_ingredients_overlap_or_missing");
        }
        return result;
    }
}
