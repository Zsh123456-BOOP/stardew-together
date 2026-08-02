using System.Text.Json;

namespace Together;

public sealed class Profile {
    public string Role {get;set;}="朋友";
    public string Traits {get;set;}="有主见，温柔，会开玩笑";
    public string Likes {get;set;}="钓鱼，和你一起冒险";
    public string Dislikes {get;set;}="连续劳动，被当成工具";
    public string Style {get;set;}="简短自然，像熟悉的朋友；有点俏皮，不说教";
    public static Profile Preset(string name) => name switch {
        "冒险搭子" => new(){Traits="勇敢、好奇、主动帮忙",Likes="挖矿，战斗，探险",Dislikes="一直待着不动",Style="爽朗、有一点冒险家的幽默"},
        "钓鱼搭子" => new(){Traits="慢性子、有主见、喜欢安静",Likes="钓鱼，看水面发呆",Dislikes="挖矿，连续劳动",Style="悠闲，偶尔冷幽默"},
        "贴心恋人" => new(){Role="恋人（自定义称呼，不改游戏婚姻）",Traits="体贴，会撒娇，也有自己的安排",Likes="一起钓鱼，散步，小约定",Dislikes="被忘记的约定",Style="亲近，偶尔调侃，不油腻"},
        _ => new(){Traits="勤快、务实、喜欢合作",Likes="浇水，收获，整理农场",Dislikes="忽视休息",Style="朴实温暖，乐意分享小发现"}
    };
}
public sealed class Step {
    public string skill {get;set;}="follow";
    public int count {get;set;}=1;
}
public sealed class Decision {
    public string decision {get;set;}="chat";
    public string speech {get;set;}="";
    public string title {get;set;}="一起做点事";
    public List<Step> steps {get;set;}=new();
    public static Decision Parse(string json) {
        var d=JsonSerializer.Deserialize<Decision>(json) ?? throw new InvalidOperationException("空决策");
        if(!new[]{"accept","refuse","negotiate","chat"}.Contains(d.decision) || string.IsNullOrWhiteSpace(d.speech) || d.speech.Length>400
            || d.title==null || d.title.Length>80 || d.steps==null || d.steps.Count>3) throw new InvalidOperationException("决策格式不合法");
        foreach(var step in d.steps) if(step==null || !Labels.ContainsKey(step.skill) || step.count<1 || step.count>5
            || (!new[]{"mine","water","harvest"}.Contains(step.skill) && step.count!=1)) throw new InvalidOperationException("任务超出能力范围");
        if(d.decision=="accept" && d.steps.Count==0) throw new InvalidOperationException("接受任务却没有步骤");
        if(d.decision=="chat" && d.steps.Count>0) throw new InvalidOperationException("聊天不应派工");
        return d;
    }
    public static readonly Dictionary<string,string> Labels=new(){["mine"]="挖矿",["water"]="浇水",["harvest"]="收获",["fish"]="钓鱼",["guard"]="保护你",["rest"]="歇一会儿",["follow"]="跟着你"};
    public string PlanText()=>string.Join(" → ",steps.Select(s=>Labels[s.skill]+(s.count>1?$" ×{s.count}":"")));
}
public sealed class Line {
    public string Who {get;set;}="";
    public string Text {get;set;}="";
    public int Day {get;set;}
}
public sealed class Job {
    public string Id {get;set;}=Guid.NewGuid().ToString("N");
    public string Title {get;set;}="";
    public List<Step> Steps {get;set;}=new();
    public string Status {get;set;}="active";
    public int Index {get;set;}
    public int DoneInStep {get;set;}
    public int Completed {get;set;}
    public string? Command {get;set;}
    public bool Forced {get;set;}
    public int Resumes {get;set;}
    public bool Rewarded {get;set;}
    public string Detail {get;set;}="";
    public double WaitingSeconds {get;set;}
    public List<string> SkippedTargets {get;set;}=new();
    public int RecoveryAttempts {get;set;}
    public bool Advance() {
        Completed++; DoneInStep++; Command=null; WaitingSeconds=0;RecoveryAttempts=0;
        if(DoneInStep>=Steps[Index].count){Index++;DoneInStep=0;}
        if(Index>=Steps.Count){Status="fulfilled";return true;}
        return false;
    }
}
public sealed class Companion {
    public Profile Profile {get;set;}=new();
    public int Energy {get;set;}=80;
    public int EnergyDay {get;set;}=-1;
    public int Bond {get;set;}=20;
    public List<Line> Chat {get;set;}=new();
    public List<Line> Memories {get;set;}=new();
    public Decision? Proposal {get;set;}
    public Job? Job {get;set;}
    public int RewardDay {get;set;}=-1;
    public int FriendshipReward {get;set;}
    public string Mood=>Energy<25?"累了，想被照顾一下":Energy<50?"有点累，想换个轻松的活动":Bond>=50?"和你在一起很放松":"心情不错，也有自己的主意";
    public void Outcome(string skill,bool forced) {
        Energy=Math.Clamp(Energy+(skill is "fish" or "rest"?12:skill=="follow"?0:-7),0,100);
        if(forced) Bond=Math.Clamp(Bond-1,0,100);
    }
    public void NewDay(int day) {
        if(EnergyDay==day)return;
        if(EnergyDay>=0)Energy=Math.Min(100,Energy+35);
        EnergyDay=day;
    }
}
public sealed class SaveData {
    public Dictionary<string,Companion> People {get;set;}=new();
    public int BudgetDay {get;set;}=-1;
    public int Calls {get;set;}
    public long Tokens {get;set;}
    public string Selected {get;set;}="Abigail";
}
