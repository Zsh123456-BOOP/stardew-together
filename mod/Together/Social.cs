namespace Together;

public sealed class Temperament {
    public int Initiative {get;set;}=60;
    public int Sociability {get;set;}=50;
    public int RiskTolerance {get;set;}=45;
    public int Patience {get;set;}=60;
    public int Planning {get;set;}=55;
}
public sealed class RelationshipState {
    public int Trust {get;set;}=35;
    public int Comfort {get;set;}=40;
    public int Cooperation {get;set;}=35;
    public int ChangeDay {get;set;}=-1;
    public int DailyPositive {get;set;}
    public List<string> AppliedEvents {get;set;}=new();
    public void Apply(string id,string kind,int day) {
        if(AppliedEvents.Contains(id))return;
        AppliedEvents.Add(id);if(AppliedEvents.Count>400)AppliedEvents.RemoveAt(0);
        if(ChangeDay!=day){ChangeDay=day;DailyPositive=0;}
        if(kind=="forced") {Comfort=Math.Max(0,Comfort-2);Cooperation=Math.Max(0,Cooperation-1);return;}
        if(kind=="external_failure" || kind=="declined" || kind=="quiet" || DailyPositive>=6)return;
        if(kind=="promise_kept") {Trust=Math.Min(100,Trust+1);Cooperation=Math.Min(100,Cooperation+1);}
        else if(kind=="shared_time")Comfort=Math.Min(100,Comfort+1);
        else return;
        DailyPositive++;
    }
}
public sealed class ConversationTopic {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Text {get;set;}="";
    public string EventId {get;set;}="";
    public int Day {get;set;}
    public int ExpiresDay {get;set;}
    public bool Answered {get;set;}
    public string Reply {get;set;}="";
}
public sealed class SharedHabit {
    public string Skill {get;set;}="";
    public string Location {get;set;}="";
    public string Title {get;set;}="";
    public bool Enabled {get;set;}=true;
    public bool Confirmed {get;set;}
    public List<int> Days {get;set;}=new();
    public int LastInvitedDay {get;set;}=-1;
}
public sealed class DiaryEntry {
    public int Day {get;set;}
    public string Text {get;set;}="";
    public List<string> Sources {get;set;}=new();
    public string PlayerNote {get;set;}="";
}
public sealed class SharedChallenge {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public int Day {get;set;}
    public int Deadline {get;set;}
    public int StartingFishSpecies {get;set;}
    public bool PlayerDone {get;set;}
    public string CompanionCatch {get;set;}="";
    public bool Celebrated {get;set;}
    public string Status {get;set;}="active";
}
public sealed class PersonalWish {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Title {get;set;}="";
    public string Skill {get;set;}="fish";
    public string Status {get;set;}="active";
    public int Target {get;set;}=3;
    public int CreatedDay {get;set;}
    public List<string> Evidence {get;set;}=new();
}
public sealed class SocialState {
    [System.Text.Json.Serialization.JsonIgnore,Newtonsoft.Json.JsonIgnore]
    public Action<IEnumerable<DiaryEntry>>? ArchiveRemoved {get;set;}
    public SharedChallenge? Challenge {get;set;}
    public bool TheirTurn {get;set;}
    public List<string> CelebratedProjects {get;set;}=new();
    public Dictionary<int,string> PlayerNotes {get;set;}=new();
    public int GiftDay {get;set;}=-1;
    public int HolidayDay {get;set;}=-1;
    public string Mode {get;set;}="normal"; // quiet, holiday, normal
    public double PlaySeconds {get;set;}
    public double LastOpening {get;set;}=-180;
    public int OpeningDay {get;set;}=-1;
    public int Openings {get;set;}
    public RelationshipState Relationship {get;set;}=new();
    public List<ConversationTopic> Topics {get;set;}=new();
    public List<SharedHabit> Habits {get;set;}=new();
    public List<DiaryEntry> Diary {get;set;}=new();
    public List<PersonalWish> Wishes {get;set;}=new();
    public List<string> Preferences {get;set;}=new();
    public List<string> SharedResults {get;set;}=new();
    public bool CanOpen(int day) {
        if(OpeningDay!=day){OpeningDay=day;Openings=0;}
        return Mode!="quiet" && Openings<3 && PlaySeconds-LastOpening>=180;
    }
    public void Open(int day,string text,string eventId) {
        if(OpeningDay!=day){OpeningDay=day;Openings=0;}
        LastOpening=PlaySeconds;Openings++;
        Topics.Add(new(){Text=text,EventId=eventId,Day=day,ExpiresDay=day+3});
        Topics=Topics.Where(t=>t.ExpiresDay>=day).TakeLast(12).ToList();
    }
    public void Reply(string text,int day) {
        var topic=Topics.LastOrDefault(t=>!t.Answered && t.ExpiresDay>=day);
        if(topic!=null){topic.Answered=true;topic.Reply=text;}
    }
    public void ObserveShared(string eventId,string skill,string location,int day,bool voluntary) {
        if(!voluntary || SharedResults.Contains(eventId))return;
        SharedResults.Add(eventId);SharedResults=SharedResults.TakeLast(120).ToList();
        Relationship.Apply(eventId+":shared","shared_time",day);
        if(skill is not ("fish" or "mine" or "rest" or "harvest"))return;
        var habit=Habits.FirstOrDefault(h=>h.Skill==skill && h.Location==location);
        if(habit==null){habit=new(){Skill=skill,Location=location,Title="在"+PlaceName(location)+"一起"+Decision.Labels[skill]};Habits.Add(habit);}
        if(!habit.Days.Contains(day))habit.Days.Add(day);
        // Repetition suggests a habit; explicit acceptance is required to call it "our habit".
        Habits=Habits.TakeLast(12).ToList();
    }
    public void CloseDay(int day,IEnumerable<Experience> experiences,IEnumerable<SharedProject> projects) {
        var facts=experiences.Where(e=>e.Day==day).ToArray();
        if(Diary.Any(d=>d.Day==day))return;
        string text=facts.Length==0?"今天没有特别记下什么，留一页给你说说今天吧。":string.Join("；",facts.TakeLast(3).Select(DiarySentence))+"。";
        var promises=projects.Where(p=>p.Status=="active" && p.Remaining>0).Take(2).Select(p=>p.Title).ToArray();
        if(promises.Length>0)text+="还记着我们说好的："+string.Join("、",promises)+"，慢慢来。";
        Diary.Add(new(){Day=day,Text=text,Sources=facts.Select(e=>e.Id).ToList(),PlayerNote=PlayerNotes.GetValueOrDefault(day,"")});
        if(Diary.Count>28)ArchiveRemoved?.Invoke(Diary.Take(Diary.Count-28).ToArray());Diary=Diary.TakeLast(28).ToList();
    }
    private static string DiarySentence(Experience e) {
        if(e.Id.EndsWith(":partial"))return e.Summary;
        string action=e.Skill switch {
            "water"=>"浇了水","harvest"=>"收下了成熟的作物","mine"=>"敲了一会儿石头","fish"=>"钓了一会儿鱼",
            "rest"=>"歇了一会儿","refill"=>"把机器需要的原料补好了","collect"=>"收好了机器做出的东西",
            "plant"=>"把种子种下了","till"=>"翻好了地","clear"=>"收拾了菜地的杂物","feed"=>"给动物添了干草",
            "pet"=>"摸了摸小动物","tend"=>"收好了动物产物","forage"=>"捡到了野生的收获","deposit"=>"把随身东西放进了箱子",
            "gift"=>"留下了一份小礼物","ship"=>"把要卖的东西送去了出货箱","buy"=>"买好了清单上的东西","guard"=>"守在你附近",
            _=>""
        };
        if(action=="")return e.Summary;
        string place=e.Location==""?"":"在"+PlaceName(e.Location);
        return (e.PlayerParticipated && e.Skill is "rest" or "fish"?"你也在附近的时候，我":"我")+place+action;
    }
    public static string PlaceName(string place)=>place switch {
        "Farm"=>"农场","FarmHouse"=>"家里","Beach"=>"海边","Town"=>"镇上","Forest"=>"森林","Mountain"=>"山边","Mine"=>"矿洞入口",
        "SeedShop"=>"皮埃尔商店","AnimalShop"=>"玛妮牧场","Blacksmith"=>"铁匠铺","Greenhouse"=>"温室",
        _=>place.StartsWith("UndergroundMine")?"矿洞第"+place[15..]+"层":place.StartsWith("Coop")?"鸡舍":place.StartsWith("Barn")?"畜棚":place};
    public void Forget() {Topics.Clear();Habits.Clear();Diary.Clear();Wishes.Clear();Preferences.Clear();SharedResults.Clear();PlayerNotes.Clear();Challenge=null;CelebratedProjects.Clear();}
}
