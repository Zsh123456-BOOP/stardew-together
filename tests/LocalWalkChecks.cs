using Together;
internal static class LocalWalkChecks {
    internal static void Run(Action<bool,string> check) {
        bool Clear(FarmCell a,FarmCell b)=>!(b.X<0&&b.Y<32);
        var route=LocalWalkRouting.Find(p=>p.X<=-16&&p.Y>=64,Clear)!;
        check(route.Count>0&&route.Zip(new[]{new FarmCell(0,0)}.Concat(route)).All(e=>Clear(e.Second,e.First)),"local body routing sidesteps before aligning past a blocker");
        check(LocalWalkRouting.Find(p=>p.X>16,(_,_)=>false)==null,"fully blocked local route terminates instead of crossing collision");
        int probes=0;LocalWalkRouting.Find(_=>false,(_,_)=>{probes++;return true;},limit:20);
        check(probes<90,"local collision search has a bounded expansion budget");
    }
}
