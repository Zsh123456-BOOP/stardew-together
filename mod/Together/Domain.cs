using System.Text.Json;

namespace Together;

public sealed class Profile {
    public Temperament Temperament {get;set;}=new();
    public string Role {get;set;}="朋友";
    public string Traits {get;set;}="有主见，温柔，会开玩笑";
    public string Likes {get;set;}="钓鱼，和你一起冒险";
    public string Dislikes {get;set;}="连续劳动，被当成工具";
    public string Style {get;set;}="简短自然，像熟悉的朋友；有点俏皮，不说教";
    public static Profile Preset(string name) => name switch {
        "冒险搭子" => new(){Temperament=new(){RiskTolerance=85,Initiative=80,Planning=40},Traits="勇敢、好奇、主动帮忙",Likes="挖矿，战斗，探险",Dislikes="一直待着不动",Style="爽朗、有一点冒险家的幽默"},
        "钓鱼搭子" => new(){Temperament=new(){RiskTolerance=25,Initiative=55,Sociability=35},Traits="慢性子、有主见、喜欢安静",Likes="钓鱼，看水面发呆",Dislikes="挖矿，连续劳动",Style="悠闲，偶尔冷幽默"},
        "贴心恋人" => new(){Temperament=new(){Sociability=85,Patience=80},Role="恋人（自定义称呼，不改游戏婚姻）",Traits="体贴，会撒娇，也有自己的安排",Likes="一起钓鱼，散步，小约定",Dislikes="被忘记的约定",Style="亲近，偶尔调侃，不油腻"},
        "慢热朋友" => new(){Temperament=new(){Sociability=20,Initiative=30,Patience=75},Traits="慢热、认真，有一点笨拙的幽默",Likes="散步，钓鱼，安静地陪着",Dislikes="突然被催促，危险的地方",Style="开始话少，熟悉后会记住小事"},
        _ => new(){Temperament=new(){Planning=85,Initiative=70},Traits="勤快、务实、喜欢合作",Likes="浇水，收获，整理农场",Dislikes="忽视休息",Style="朴实温暖，乐意分享小发现"}
    };
}
public sealed class Step {
    public string? target_item {get;set;}
    public string skill {get;set;}="follow";
    public int count {get;set;}=1;
    public string? location {get;set;}
}
public sealed class Decision {
    public string decision {get;set;}="chat";
    public string? option_id {get;set;}
    public string project {get;set;}="none";
    public string speech {get;set;}="";
    public string title {get;set;}="一起做点事";
    public List<Step> steps {get;set;}=new();
    public static Decision Parse(string json) {
        var d=JsonSerializer.Deserialize<Decision>(json) ?? throw new InvalidOperationException("空决策");
        if(!new[]{"accept","refuse","negotiate","chat"}.Contains(d.decision) || string.IsNullOrWhiteSpace(d.speech) || d.speech.Length>400
            || d.title==null || d.title.Length>80 || d.steps==null || d.steps.Count>3) throw new InvalidOperationException("决策格式不合法");
        if(!new[]{"none","farm","bundle"}.Contains(d.project))throw new InvalidOperationException("共同项目类型不支持");
        foreach(var step in d.steps) if(step==null || !Labels.ContainsKey(step.skill) || step.count<1 || step.count>5
            || (!new[]{"clear","till","plant","feed","tend","gift","forage","buy","ship","refill","deposit","mine","water","harvest","pet","collect"}.Contains(step.skill) && step.count!=1)) throw new InvalidOperationException("任务超出能力范围");
        if(d.decision=="accept" && d.steps.Count==0) throw new InvalidOperationException("接受任务却没有步骤");
        if(d.decision=="chat" && d.steps.Count>0) throw new InvalidOperationException("聊天不应派工");
        return d;
    }
    public static readonly Dictionary<string,string> Labels=new(){["clear"]="清理指定地块杂物",["tend"]="挤奶剪毛",["gift"]="留一件小礼物",["till"]="翻土",["plant"]="播种",["feed"]="给食槽添草",["forage"]="采集",["buy"]="采购清单物品",["ship"]="运送出售物品",["refill"]="给机器补料",["deposit"]="存放随身物资",["pet"]="照料动物",["collect"]="收取机器",["mine"]="挖矿",["water"]="浇水",["harvest"]="收获",["fish"]="钓鱼",["guard"]="保护你",["rest"]="歇一会儿",["follow"]="跟着你"};
    public string PlanText()=>string.Join(" → ",steps.Select(s=>Labels[s.skill]+(s.count>1?$" ×{s.count}":"")));
}
public sealed class Line {
    public string Who {get;set;}="";
    public string Text {get;set;}="";
    public int Day {get;set;}
}
public sealed class Job {
    public int? RemainingSeconds {get;set;}
    public List<string> PartialReceipts {get;set;}=new();
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Title {get;set;}="";
    public string Origin {get;set;}="player";
    public string OptionId {get;set;}="";
    public string Reason {get;set;}="";
    public string? TravelCommand {get;set;}
    public List<Step> Steps {get;set;}=new();
    public string Status {get;set;}="active";
    public int Index {get;set;}
    public int DoneInStep {get;set;}
    public int Completed {get;set;}
    public string? Command {get;set;}
    public bool FollowMode {get;set;}
    public double SharedSeconds {get;set;}
    public double ObservedSeconds {get;set;}
    public bool Forced {get;set;}
    public int Resumes {get;set;}
    public bool Rewarded {get;set;}
    public string Detail {get;set;}="";
    public double WaitingSeconds {get;set;}
    public List<string> SkippedTargets {get;set;}=new();
    public int RecoveryAttempts {get;set;}
    public bool Advance() {
        Completed++; DoneInStep++; Command=null; WaitingSeconds=0;RecoveryAttempts=0;RemainingSeconds=null;
        if(DoneInStep>=Steps[Index].count){Index++;DoneInStep=0;}
        if(Index>=Steps.Count){Status="fulfilled";return true;}
        return false;
    }
}
public sealed class Companion {
    public bool? DailyCompanion {get;set;}
    public SocialState Social {get;set;}=new();
    public Profile Profile {get;set;}=new();
    public LifeState Life {get;set;}=new();
    public int Energy {get;set;}=80;
    public int EnergyDay {get;set;}=-1;
    public int Bond {get;set;}=20;
    public List<Line> Chat {get;set;}=new();
    public List<Line> Memories {get;set;}=new();
    public Decision? LastDecision {get;set;}
    public Decision? Proposal {get;set;}
    public bool ProposalAutonomous {get;set;}
    public int ProposalExpires {get;set;}
    public Job? Job {get;set;}
    public int RewardDay {get;set;}=-1;
    public int FriendshipReward {get;set;}
    public string Mood=>Energy<25?"累了，想被照顾一下":Energy<50?"有点累，想换个轻松的活动":Bond>=50?"和你在一起很放松":"心情不错，也有自己的主意";
    public void Outcome(string skill,bool forced) {
        Energy=Math.Clamp(Energy+(skill=="rest"?18:skill=="follow"?-1:skill=="mine"?-9:skill=="fish"?-5:skill is "pet" or "collect"?-2:-4),0,100);

    }
    public void NewDay(int day) {
        if(EnergyDay==day)return;
        if(EnergyDay>=0)Energy=Math.Min(100,Energy+35);
        EnergyDay=day;
    }
}
public sealed class StoragePolicy {
    public bool AutoExpand {get;set;}=true;
    public int MaxSharedChests {get;set;}=4;
    public int WoodBudgetPerDay {get;set;}=100;
    public int BudgetDay {get;set;}=-1;
    public int WoodReserved {get;set;}
}
public sealed class SaveData {
    public StoragePolicy Storage {get;set;}=new();
    public FarmInvestmentPolicy FarmInvestment {get;set;}=new();
    public List<SharedGoal> SharedGoals {get;set;}=new();
    public KnowledgeNotebook Knowledge {get;set;}=new();
    public FarmPolicy FarmPolicy {get;set;}=new();
    public List<PlanNode> Today {get;set;}=new();
    public AutoplayState Autoplay {get;set;}=new();
    public int SchemaVersion {get;set;}=6;
    public string Pace {get;set;}="balanced";
    public bool FarmHelp {get;set;}=true;
    public List<SharedProject> Projects {get;set;}=new();
    public Dictionary<string,int> ObservedProgress {get;set;}=new();
    public Dictionary<string,int> Reservations {get;set;}=new();
    public Dictionary<string,Companion> People {get;set;}=new();
    public int BudgetDay {get;set;}=-1;
    public int Calls {get;set;}
    public long Tokens {get;set;}
    public string Selected {get;set;}="Abigail";
}
