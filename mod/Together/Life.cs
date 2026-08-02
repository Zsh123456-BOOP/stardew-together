namespace Together;

public sealed class LifeState {
    public double Interest {get;set;}=45;
    public double Company {get;set;}=35;
    public double Variety {get;set;}=30;
    public int LastMinute {get;set;}=-1;
    public int LastDay {get;set;}=-1;
    public int LastDecisionMinute {get;set;}=-1000;
    public int LastSpeechMinute {get;set;}=-1000;
    public string LastDecisionError {get;set;}="";
    public string DecisionSource {get;set;}="none";
    public string LastSkill {get;set;}="";
    public string Intent {get;set;}="想先看看今天有什么安排";
    public string Reason {get;set;}="";
    public string Wish {get;set;}="";
    public int WishProgress {get;set;}
    public int WishTarget {get;set;}=3;
    public int WishDay {get;set;}=-1;
    public string SharedHabit {get;set;}="傍晚碰面时聊聊各自的一天";
    public int HabitDay {get;set;}=-1;
    public int LastInvitationDay {get;set;}=-1;
    public Dictionary<string,int> RetryAfter {get;set;}=new();
    public List<Job> Suspended {get;set;}=new();
    public List<Experience> Experiences {get;set;}=new();
    public void Tick(int day,int minute,bool near,Profile profile) {
        if(LastDay!=day) {LastDay=day;LastMinute=minute;LastDecisionMinute=-1000;LastSpeechMinute=-1000;RetryAfter.Clear();}
        int elapsed=Math.Clamp(minute-LastMinute,0,30);LastMinute=minute;
        Interest=Math.Clamp(Interest+elapsed*.16,0,100);
        Company=Math.Clamp(Company+elapsed*(near?-.04:.14),0,100);
        Variety=Math.Clamp(Variety+elapsed*.1,0,100);
        if(WishDay<0 || (WishProgress>=WishTarget && day>WishDay)) {
            Wish=profile.Likes.Contains("钓鱼")?"这几天找三个空档钓鱼，留下一段水边的回忆":profile.Likes.Contains("冒险") || profile.Likes.Contains("挖矿")?"和你凑齐三次小冒险": "把三次答应分担的农活做好";
            WishProgress=0;WishTarget=3;WishDay=day;
        }
    }
    public void Complete(string skill,int day,int minute,string summary,bool personal) {
        if(skill!=LastSkill)Variety=Math.Max(0,Variety-25);
        LastSkill=skill;
        if(personal){Interest=Math.Max(0,Interest-35);WishProgress=Math.Min(WishTarget,WishProgress+1);}
        Experiences.Add(new(){Id=Guid.NewGuid().ToString("N"),Day=day,Minute=minute,Summary=summary,Personal=personal});
        if(Experiences.Count>120)Experiences.RemoveRange(0,Experiences.Count-120);
    }
}
public sealed class Experience {
    public string Id {get;set;}="";
    public int Day {get;set;}
    public int Minute {get;set;}
    public string Summary {get;set;}="";
    public bool Personal {get;set;}
    public bool Shared {get;set;}
}
public sealed class SharedProject {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Kind {get;set;}="farm";
    public string Title {get;set;}="今天的农场一起照料";
    public string Status {get;set;}="active";
    public string Owner {get;set;}="together";
    public string Detail {get;set;}="";
    public int CreatedDay {get;set;}
    public int ObservedDay {get;set;}=-1;
    public int Remaining {get;set;}
    public List<Requirement> Needs {get;set;}=new();
}
public sealed class Requirement {
    public string Name {get;set;}="";
    public string Item {get;set;}="";
    public int Count {get;set;}
    public int Quality {get;set;}
    public int Owned {get;set;}
    public string Source {get;set;}="";
    public int Missing=>Math.Max(0,Count-Owned);
}
public sealed class ActivityOption {
    public string Id {get;set;}="";
    public string Title {get;set;}="";
    public string Reason {get;set;}="";
    public string Category {get;set;}="personal";
    public List<Step> Steps {get;set;}=new();
    public double Score {get;set;}
}
public sealed class Situation {
    public int Day {get;set;}
    public int Minute {get;set;}
    public bool NearPlayer {get;set;}
    public bool Threat {get;set;}
    public bool Fishing {get;set;}
    public string Location {get;set;}="";
    public string PlayerLocation {get;set;}="";
    public int FarmWater {get;set;}
    public int FarmHarvest {get;set;}
    public bool CanReachBeach {get;set;}
    public bool CanReachFarm {get;set;}
    public int Water {get;set;}
    public int Harvest {get;set;}
    public int Pet {get;set;}
    public int Collect {get;set;}
    public int Mine {get;set;}
    public bool FarmProject {get;set;}
    public string Pace {get;set;}="balanced";
}
public static class LifePlanner {
    public static List<ActivityOption> Options(Companion p,Situation s) {
        var list=new List<ActivityOption>();
        void Add(string id,string title,string reason,double score,string category,string skill,int count=1) {
            if(p.Life.RetryAfter.TryGetValue(id,out var after) && after>s.Minute)return;
            list.Add(new(){Id=id,Title=title,Reason=reason,Category=category,Score=score,
                Steps=new(){new(){skill=skill,count=count,location=s.Location}}});
        }
        if(s.Threat && s.NearPlayer)Add("defend","先照应你","附近有真实的危险",150,"care","guard");
        Add("rest","歇一会儿","留点力气，今天还有很长",p.Energy<25?135:Math.Max(5,65-p.Energy),"personal","rest");
        if(s.Minute>=23*60) {Add("company","一起收工","已经很晚了，别再添新活",120,"care","follow");return list.OrderByDescending(x=>x.Score).ToList();}
        double work=s.Pace=="relaxed"?12:s.Pace=="focused"?40:25;
        if(p.Energy>=25) {
            if(s.Pet>0)Add("pet","去看看农场的小家伙","有动物今天还没有被抚摸",work+(s.FarmProject?42:12),"shared","pet",Math.Min(s.Pet,8));
            if(s.Collect>0)Add("collect","收下机器做好的东西","机器的成品已经准备好了",work+(s.FarmProject?30:10),"shared","collect",Math.Min(s.Collect,8));
            if(s.Water>0)Add("water","我来照料干渴的作物","这里的作物还没有浇水",work+(s.FarmProject?40:15),"shared","water",Math.Min(s.Water,12));
            if(s.Harvest>0)Add("harvest","收下今天成熟的作物","成熟作物就在这里",work+(s.FarmProject?45:18),"shared","harvest",Math.Min(s.Harvest,12));
            if(s.Mine>0)Add("mine","探索附近的矿石","附近有我能处理的石头",Preference(p,"挖矿")+p.Life.Interest*.35+work*.4,"personal","mine",Math.Min(s.Mine,3));
        }
        if(s.Fishing)Add("fish","找个安稳的位置钓会儿鱼","这里确实有可达的钓位",Preference(p,"钓鱼")+p.Life.Interest*.55+(p.Life.LastSkill=="fish"?-20:15),"personal","fish");
        if((!s.Fishing || p.Life.Variety>=60 || p.Profile.Likes.Contains("海")) && s.CanReachBeach && s.Location!="Beach" && p.Energy>=25 && p.Profile.Likes.Contains("钓鱼") && p.Life.Interest>=50
            && (!p.Life.RetryAfter.TryGetValue("beach_trip",out var beachAfter) || beachAfter<=s.Minute))list.Add(new(){
                Id="beach_trip",Title="去海边钓会儿鱼",Reason="有点想念海边，也有走得到的路线",Category="personal",Score=45+p.Life.Interest*.55+p.Life.Variety*.2,
                Steps=new(){new(){skill="fish",location="Beach"}}});
        Add("company","陪你走一段","想知道你现在忙什么",15+p.Life.Company*.55+(s.NearPlayer?-10:20),"care","follow");
        if(s.FarmProject && s.CanReachFarm && s.Location!="Farm" && p.Energy>=25) {
            string skill=s.FarmHarvest>0?"harvest":"water";int count=skill=="harvest"?s.FarmHarvest:s.FarmWater;
            if(count>0 && (!p.Life.RetryAfter.TryGetValue("farm_trip",out var after) || after<=s.Minute))list.Add(new(){
                Id="farm_trip",Title="回农场完成分担的活",Reason="我们约好了分工，地图上有可走的出口",Category="shared",Score=work+45,
                Steps=new(){new(){skill=skill,count=Math.Min(count,12),location="Farm"}}});
        }
        return list.OrderByDescending(x=>x.Score).ToList();
    }
    private static int Preference(Companion p,string activity)=>p.Profile.Likes.Contains(activity)?40:p.Profile.Dislikes.Contains(activity)?-25:5;
    public static ActivityOption? Select(Companion p,Situation s)=>Options(p,s).FirstOrDefault();
}
