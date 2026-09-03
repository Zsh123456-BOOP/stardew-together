namespace Together;
// Basic material sales require an explicit surplus decision. Quantities come
// from current projects and production, not a fixed reserve per item.
public static class BusinessRetention {
    public static readonly HashSet<string> Materials=new(){"(O)388","(O)390","(O)771","(O)92","(O)382","(O)330","(O)309","(O)310","(O)311","(O)770"};
    public static int Sellable(int owned,int unreserved,int floor,int processing)=>Math.Max(0,Math.Min(unreserved,owned-Math.Max(floor,processing)));
}
