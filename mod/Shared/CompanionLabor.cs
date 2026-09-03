namespace Together.Shared;
public static class CompanionLabor {
    public const int DailyLimit=180;
    public static int Cost(string skill)=>skill switch {"water" or "pet"=>1,"harvest" or "forage"=>2,"mine" or "clear"=>4,"fish"=>12,"plant" or "till" or "feed" or "tend" or "collect" or "refill"=>2,_=>0};
    public static int OptionalAllowance(int remaining,int dry,int ripe,int hour)=>System.Math.Max(0,remaining-dry-2*ripe-(hour<1500?30:0));
}
