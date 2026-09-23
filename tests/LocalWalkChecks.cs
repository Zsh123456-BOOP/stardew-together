using Together;
internal static class LocalWalkChecks {
    internal static void Run(Action<bool,string> check) {
        bool Clear(FarmCell a,FarmCell b)=>!(b.X<0&&b.Y<32);
        var route=LocalWalkRouting.Find(p=>p.X<=-16&&p.Y>=64,Clear)!;
        check(route.Count>0&&route.Zip(new[]{new FarmCell(0,0)}.Concat(route)).All(e=>Clear(e.Second,e.First)),"local body routing sidesteps before aligning past a blocker");
        check(LocalWalkRouting.Find(p=>p.X>16,(_,_)=>false)==null,"fully blocked local route terminates instead of crossing collision");
        int probes=0;LocalWalkRouting.Find(_=>false,(_,_)=>{probes++;return true;},limit:20);
        check(probes<90,"local collision search has a bounded expansion budget");
        // Synthetic narrow doorway with the observed failure's body size and
        // origin. The centered tile box clips a corner but an offset body fits.
        bool BodyClear(int x,int y)=>!(x+48>436&&x<460&&y+32>1715&&y<1800);
        var offsetRoute=LocalWalkRouting.Find(p=>386+p.X>=386&&386+p.X+48<=446&&1671+p.Y>=1728&&1671+p.Y+32<=1790,(a,b)=>Enumerable.Range(1,4).All(i=>BodyClear(386+a.X+(b.X-a.X)*i/4,1671+a.Y+(b.Y-a.Y)*i/4)));
        check(!BodyClear(392,1740)&&offsetRoute is {Count:>0},"blocked tile center is not proof that a swept-body doorway route is impossible");
        check(offsetRoute!.Zip(new[]{new FarmCell(0,0)}.Concat(offsetRoute!)).All(e=>Math.Abs(e.First.X-e.Second.X)+Math.Abs(e.First.Y-e.Second.Y)==8),"doorway recovery uses adjacent pixel steps instead of teleporting across collision");
    }
}
