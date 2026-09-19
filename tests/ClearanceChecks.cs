using Together;
public static class ClearanceChecks {
    public static void Run(Action<bool,string> check) {
        var wall=new FarmCell(1,0);bool Open(FarmCell p)=>p.X>=0&&p.X<=3&&p.Y==0&&p!=wall;
        ClearanceRouting.Obstacle? Obstacle(FarmCell p)=>p==wall?new(3,2,true):null;
        var p=ClearanceRouting.Find(new(0,0),new(3,0),Open,Obstacle,_=>true,10);
        check(p?.Clear.SequenceEqual(new[]{wall})==true&&p.Path.Count==4,"bounded route clears blocking stone and retains native walk sequence");
        check(ClearanceRouting.Find(new(0,0),new(3,0),Open,_=>null,_=>true,10)==null,"missing tool or protected obstacle has infinite cost");
        check(ClearanceRouting.Find(new(0,0),new(3,0),Open,Obstacle,_=>false,10)==null,"full drop capacity rejects clearing route");
        check(ClearanceRouting.Find(new(0,0),new(3,0),Open,Obstacle,_=>true,1)==null,"route cannot spend reserved labour energy");
        check(ClearanceRouting.Find(new(0,0),new(3,0),Open,Obstacle,_=>true,10,maxClear:0)==null,"clearance hard limit cannot be bypassed");
        bool Around(FarmCell c)=>c.X>=0&&c.X<=3&&c.Y>=0&&c.Y<=1&&c!=wall;
        p=ClearanceRouting.Find(new(0,0),new(3,0),Around,Obstacle,_=>true,10);
        check(p?.Clear.Count==0&&p.Path.Count==6,"cheap detour wins over unnecessary clearing");
        p=ClearanceRouting.Find(new(0,0),new(3,0),Around,c=>c==wall?new(.5,0,false):null,_=>true,10);
        check(p?.Clear.Count==1,"quick safe weed clearance wins over expensive detour");
        check(ClearanceRouting.Find(new(0,0),new(5,0),c=>c.Y==0&&(c.X==0||c.X==5),c=>c.Y==0&&c.X>0&&c.X<5?new(1,0,true):null,_=>true,10)==null,"four-obstacle corridor is not excavated by navigation");
    }
}
