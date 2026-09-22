namespace Together;

// Native 1.6.15 breakStone drop routes, used only to SELECT physical targets.
// Random extra drops, yields, XP and progress remain entirely native.
public static class ResourceRules {
    public static string NormalizeGoal(string goal,string item)=>goal=="resource"&&WorkKind(item) is {Length:>0} kind?kind:goal;
    public static string WorkKind(string item)=>item switch{"(O)388"=>"wood","(O)390"=>"stone","(O)771"=>"fiber","(O)709"=>"hardwood",_=>Nodes.Values.Contains(item)?"resource":""};
    public static readonly IReadOnlyDictionary<string,string> Nodes=new Dictionary<string,string> {
        ["751"]="(O)378",["849"]="(O)378",["290"]="(O)380",["850"]="(O)380",
        ["764"]="(O)384",["VolcanoGoldNode"]="(O)384",["765"]="(O)386",
        ["BasicCoalNode0"]="(O)382",["BasicCoalNode1"]="(O)382",["VolcanoCoalNode0"]="(O)382",["VolcanoCoalNode1"]="(O)382",
        ["816"]="(O)881",["817"]="(O)881",["818"]="(O)330",["819"]="(O)749",
        ["8"]="(O)66",["10"]="(O)68",["12"]="(O)60",["14"]="(O)62",["6"]="(O)70",["4"]="(O)64",["2"]="(O)72",
        ["75"]="(O)535",["76"]="(O)536",["77"]="(O)537"
    };
    public static (string Tool,int Level,string Output)? Clump(int id)=>id switch {
        600=>("axe",1,"(O)709"),602=>("axe",2,"(O)709"),672=>("pickaxe",2,"(O)390"),
        622=>("pickaxe",3,"(O)386"),148=>("pickaxe",3,"(O)390"),752 or 754 or 756 or 758=>("pickaxe",0,"(O)390"),_=>null
    };
}
