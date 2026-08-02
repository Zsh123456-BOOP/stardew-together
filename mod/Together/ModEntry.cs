using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace Together;

public interface ICompanionControl {
    string GetState(); string GetMap(string actorId); string StartAction(string request);
    string PollAction(string id); string CancelAction(string id); string PrepareLab(); void Reset();
}
public sealed class Config {
    public SButton OpenKey {get;set;}=SButton.F8;
    public string ApiKeyFile {get;set;}=".env";
    public string Model {get;set;}="deepseek-flash";
    public bool Autonomy {get;set;}=true;
    public int AutoIntervalSeconds {get;set;}=90;
    public int MaxCallsPerDay {get;set;}=24;
    public bool AllowTrialRecruitment {get;set;}=true;
    public bool EnableLab {get;set;}
}

public sealed class ModEntry:Mod {
    public Config Settings {get;private set;}=new();
    public SaveData Data {get;private set;}=new();
    public string Notice {get;private set;}="按 F8，聊聊今天想做什么。";
    public bool Thinking=>pending!=null;
    public string Selected=>Data.Selected;
    public Companion Current=>Person(Selected);
    public bool Connected=>api!=null;
    private ICompanionControl? api;
    private Task<ModelReply>? pending;
    private string pendingName="";
    private int generation,pendingGeneration;
    private DateTime autoAt=DateTime.UtcNow.AddSeconds(30);
    private string lastAutoSignature="";
    private double slowTick;
    private string? capturePath;
    private bool canPersist=true;
    private SpriteFont? font;
    private readonly Dictionary<string,(string Text,DateTime Until)> bubbles=new();
    public SpriteFont Font {
        get {font??=Game1.content.Load<SpriteFont>("Fonts/SmallFont.zh-CN");font.LineSpacing=28;return font;}
    }
    private readonly JsonSerializerOptions jsonOptions=new(){WriteIndented=true};
    private string SavePath=>Path.Combine(Helper.DirectoryPath,"data",$"{Game1.uniqueIDForThisGame}-{Game1.player.UniqueMultiplayerID}.json");
    public Companion Person(string name) {
        if(!Data.People.TryGetValue(name,out var person)) {
            Data.People[name]=person=new Companion();
            if(Context.IsWorldReady)person.NewDay(Game1.Date.TotalDays);
        }
        return person;
    }
    public override void Entry(IModHelper helper) {
        Settings=helper.ReadConfig<Config>();
        helper.Events.GameLoop.GameLaunched+=(_,_)=>{
            api=helper.ModRegistry.GetApi<ICompanionControl>("ThaliaFawnheart.TheStardewSquad");
            Notice=api==null?"需要带同行接口的 Squad 版本。":"同行已准备好，按 F8 打开。";
        };
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>Load();
        helper.Events.GameLoop.Saving+=(_,_)=>Persist();
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{generation++;pending=null;api?.Reset();Data=new();};
        helper.Events.GameLoop.DayStarted+=(_,_)=>{
            foreach(var p in Data.People.Values) p.NewDay(Game1.Date.TotalDays);
            autoAt=DateTime.UtcNow.AddSeconds(30);
        };
        helper.Events.Input.ButtonPressed+=(_,e)=>{
            if(Context.IsWorldReady && e.Button==Settings.OpenKey && (Game1.activeClickableMenu==null || Game1.activeClickableMenu is CompanionMenu)) {
                helper.Input.Suppress(e.Button);
                if(Game1.activeClickableMenu is CompanionMenu) Game1.exitActiveMenu();else Open();
            }
        };
        helper.Events.GameLoop.UpdateTicked+=Update;
        helper.Events.Display.RenderedWorld+=(_,e)=>{
            if(!Context.IsWorldReady)return;
            foreach(var pair in bubbles.Where(p=>p.Value.Until>DateTime.UtcNow)) {
                var npc=Game1.getCharacterFromName(pair.Key);
                if(npc?.currentLocation!=Game1.currentLocation)continue;
                string text=Game1.parseText(pair.Value.Text,Font,500);
                Vector2 size=Font.MeasureString(text)*.65f;
                Vector2 position=Game1.GlobalToLocal(Game1.viewport,npc.Position+new Vector2(32,-64));
                if(position.X<-100 || position.X>Game1.viewport.Width+100)continue;
                position.X=Math.Clamp(position.X-size.X/2,8,Math.Max(8,Game1.viewport.Width-size.X-8));
                position.Y=Math.Max(8,position.Y-size.Y);
                e.SpriteBatch.Draw(Game1.staminaRect,new Rectangle((int)position.X-8,(int)position.Y-6,(int)size.X+16,(int)size.Y+12),new Color(248,239,216)*.95f);
                e.SpriteBatch.DrawString(Font,text,position,new Color(43,65,57),0,Vector2.Zero,.65f,SpriteEffects.None,1);
            }
        };
        helper.Events.Display.RenderedHud+=(_,e)=>{
            if(!Context.IsWorldReady || Game1.activeClickableMenu!=null) return;
            string text=$"{Settings.OpenKey} 同行 · {Selected}  "+(Thinking?"正在想怎么回答你…":Current.Job?.Status is "active" or "waiting"?JobText(Current.Job):"聊聊 / 小约定");
            e.SpriteBatch.Draw(Game1.staminaRect,new Rectangle(16,Game1.uiViewport.Height-53,Math.Min(760,Game1.uiViewport.Width-32),38),new Color(28,44,42)*.88f);
            e.SpriteBatch.DrawString(Font,text,new Vector2(28,Game1.uiViewport.Height-47),new Color(246,235,211),0,Vector2.Zero,.8f,SpriteEffects.None,1);
        };
        helper.Events.Display.Rendered+=(_,_)=>Capture();
        helper.ConsoleCommands.Add("together_open","Open Together panel.",(_,_)=>Open());
        helper.ConsoleCommands.Add("together_close","Close Together panel.",(_,_)=>{if(Game1.activeClickableMenu is CompanionMenu)Game1.exitActiveMenu();});
        helper.ConsoleCommands.Add("together_auto","Toggle autonomous proposals.",(_,_)=>ToggleAuto());
        helper.ConsoleCommands.Add("together_export","Write sanitized state for local integration checks.",(_,_)=>{
            if(Context.IsWorldReady){Directory.CreateDirectory(Path.Combine(Helper.DirectoryPath,"diagnostics"));File.WriteAllText(Path.Combine(Helper.DirectoryPath,"diagnostics","state.json"),StatusJson());}
        });
        helper.ConsoleCommands.Add("together_say","Send a message through the same game chat pipeline.",(_,args)=>Send(string.Join(" ",args)));
        helper.ConsoleCommands.Add("together_select","Select a nearby or recruited NPC.",(_,args)=>{if(args.Length==1)Select(args[0]);});
        helper.ConsoleCommands.Add("together_preset","Set one of the four persona presets.",(_,args)=>{if(args.Length==1)SetPreset(args[0]);});
        helper.ConsoleCommands.Add("together_accept","Accept the visible proposal.",(_,_)=>AcceptProposal(false));
        helper.ConsoleCommands.Add("together_force","Explicitly force the visible proposal.",(_,_)=>AcceptProposal(true));
        helper.ConsoleCommands.Add("together_cancel","Cancel thinking and the selected companion's task.",(_,_)=>Cancel());
        helper.ConsoleCommands.Add("together_resume","Resume a saved or waiting promise.",(_,_)=>Resume());
        helper.ConsoleCommands.Add("together_status","Print sanitized Together status.",(_,_)=>Monitor.Log(StatusJson(),LogLevel.Info));
        helper.ConsoleCommands.Add("together_capture","Save a development screenshot in the Mod's screenshots folder.",(_,_)=>{
            if(Settings.EnableLab && Context.IsWorldReady && Game1.player.Name=="AgentLab") capturePath=Path.Combine(Helper.DirectoryPath,"screenshots","panel.png");
        });
        helper.ConsoleCommands.Add("together_lab_ui","AgentLab-only Chinese panel rendering check: chat/persona/history.",(_,args)=>{
            if(!Settings.EnableLab || !Context.IsWorldReady || Game1.player.Name!="AgentLab")return;
            Game1.exitActiveMenu();
            LocalizedContentManager.CurrentLanguageCode=LocalizedContentManager.LanguageCode.zh;
            Game1.activeClickableMenu=new CompanionMenu(this,args.FirstOrDefault() switch {"persona"=>1,"history"=>2,_=>0});
        });
        helper.ConsoleCommands.Add("together_lab_plan","AgentLab-only deterministic integration plan: skill[:count] ...",(_,args)=>{
            if(!Settings.EnableLab || !Context.IsWorldReady || Game1.player.Name!="AgentLab")return;
            try {
                var d=new Decision {decision="negotiate",speech="我们来试试这个小约定。",title="测试小约定",steps=args.Select(s=>new Step {skill=s.Split(':')[0],count=s.Contains(':')?int.Parse(s.Split(':')[1]):1}).ToList()};
                Current.Proposal=Decision.Parse(JsonSerializer.Serialize(d));AcceptProposal(false);
            }catch(Exception){Notice="测试计划不合法。";}
        });
    }
    private void Load() {
        generation++;pending=null;canPersist=true;api?.Reset();
        try{Data=File.Exists(SavePath)?JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SavePath))??new():new();}
        catch{Data=new();canPersist=false;Notice="同行记录无法读取，本次暂停写入以保留原文件。";return;}
        foreach(var p in Data.People.Values) if(p.Job?.Status is "active" or "waiting") {p.Job.Status="paused";p.Job.Command=null;p.Job.Detail="上次的小约定还在；点继续后重新检查环境。";}
        foreach(var p in Data.People.Values)p.NewDay(Game1.Date.TotalDays);
        // Reclaim previously managed companions after Squad restores its saved team.
        // A paused promise must not fall through to unrelated autonomous Squad labor.
        try {
            foreach(var actor in World().GetProperty("actors").EnumerateArray())
                if(Data.People.ContainsKey(actor.GetProperty("name").GetString()!))Request(actor.GetProperty("id").GetString()!,"follow");
        }catch{Notice="队伍尚未就绪；靠近队友后可重新邀请。";}
        EnsureBudget();autoAt=DateTime.UtcNow.AddSeconds(30);
        if(Settings.EnableLab)Game1.options.pauseWhenOutOfFocus=false;
    }
    public void Persist() {
        if(!Context.IsWorldReady || !canPersist)return;
        Directory.CreateDirectory(Path.GetDirectoryName(SavePath)!);
        File.WriteAllText(SavePath+".tmp",JsonSerializer.Serialize(Data,jsonOptions));
        File.Move(SavePath+".tmp",SavePath,true);
    }
    public string[] Names() {
        if(!Context.IsWorldReady)return Array.Empty<string>();
        var names=new HashSet<string>();
        try{foreach(var actor in World().GetProperty("actors").EnumerateArray())names.Add(actor.GetProperty("name").GetString()!);}catch{}
        foreach(var npc in Game1.currentLocation.characters.Where(n=>!n.IsMonster && Vector2.Distance(n.Tile,Game1.player.Tile)<=8)) names.Add(npc.Name);
        if(names.Count==0)names.Add(Data.Selected);
        return names.OrderBy(n=>n).ToArray();
    }
    public void Select(string name) {
        if(!Context.IsWorldReady || !Names().Contains(name))return;
        generation++;Data.Selected=name;Persist();
    }
    public void Open(){if(Context.IsWorldReady && Game1.activeClickableMenu==null)Game1.activeClickableMenu=new CompanionMenu(this);}
    public void SetPreset(string name){if(!Context.IsWorldReady)return;Current.Profile=Profile.Preset(name);Notice="人设已更新，下一句话就会生效。";Persist();}
    public void ToggleAuto(){Settings.Autonomy=!Settings.Autonomy;Helper.WriteConfig(Settings);Notice=Settings.Autonomy?"允许队友主动提议。":"队友会等你的指令。";}
    private void EnsureBudget(){if(Data.BudgetDay!=Game1.Date.TotalDays){Data.BudgetDay=Game1.Date.TotalDays;Data.Calls=0;}}
    private JsonElement World(){using var doc=JsonDocument.Parse(api?.GetState()??"{}");return doc.RootElement.Clone();}
    private JsonElement? Actor(JsonElement world,string name) {
        if(!world.TryGetProperty("actors",out var actors))return null;
        foreach(var actor in actors.EnumerateArray())if(actor.GetProperty("name").GetString()==name)return actor;
        return null;
    }
    private JsonElement Request(string actor,string skill,string? target=null,int seconds=30) {
        string id=Guid.NewGuid().ToString("N");
        using var doc=JsonDocument.Parse(api!.StartAction(JsonSerializer.Serialize(new{command_id=id,actor_id=actor,skill,target_id=target,seconds,trial=Settings.AllowTrialRecruitment})));
        return doc.RootElement.Clone();
    }
    public void Recruit() {
        if(!Connected || !Context.IsWorldReady)return;
        try{Request(Selected,"recruit");Say(Selected,"那就一起走吧。今天想先做什么？");Notice="已邀请同行。";}
        catch(Exception){Notice="暂时无法邀请：请靠近角色，并检查队伍人数与角色日程。";}
    }
    public void Dismiss() {
        Cancel();try{var a=Actor(World(),Selected);if(a.HasValue)Request(a.Value.GetProperty("id").GetString()!,"dismiss");Notice="先各忙各的，下次见。";}catch{Notice="暂时不能结束同行。";}
    }
    public void Send(string message,bool autonomous=false) {
        if(!Context.IsWorldReady || !Connected || string.IsNullOrWhiteSpace(message))return;
        if(Thinking){Notice="等我把这句话想完，或点停止。";return;}
        if(message.Length>400){Notice="一句话最多 400 字。";return;}
        EnsureBudget();if(Data.Calls>=Math.Clamp(Settings.MaxCallsPerDay,1,100)){Notice="今天的聊天额度用完啦，仍可继续约定和使用快捷活动。";return;}
        JsonElement world;try{world=World();}catch{Notice="游戏状态暂不可用。";return;}
        var person=Current; var actor=Actor(world,Selected);
        if(!autonomous)AddLine(person.Chat,"你",message);
        var context=new {event_type=autonomous?"idle":"player_message",player_message=message,npc=Selected,profile=person.Profile,
            mood=person.Mood,energy=person.Energy,bond=person.Bond,actor,world=new{location=world.GetProperty("location").GetString(),time=Game1.timeOfDay,
                health=Game1.player.health,season=Game1.currentSeason,raining=Game1.isRaining,threats=world.GetProperty("threats")},
            memories=person.Memories.TakeLast(5).ToArray(),conversation=person.Chat.TakeLast(5).ToArray(),current_promise=person.Job,
            note="bond是本Mod亲近感，actor.relationship是真实好感。没有招募时可以聊天，行动需先邀请。"};
        Data.Calls++;pendingName=Selected;pendingGeneration=generation;
        string file=Path.IsPathRooted(Settings.ApiKeyFile)?Settings.ApiKeyFile:Path.Combine(Helper.DirectoryPath,Settings.ApiKeyFile);
        pending=ModelClient.Ask(file,Settings.Model,context);Notice="正在想怎么回答你…";Persist();
    }
    private void CompleteReply() {
        if(pending==null || !pending.IsCompleted)return;
        var task=pending;pending=null;
        if(pendingGeneration!=generation)return;
        try {
            var result=task.GetAwaiter().GetResult();Data.Tokens+=result.Tokens;
            var decision=Decision.Parse(result.Json);var person=Person(pendingName);
            Say(pendingName,decision.speech);
            person.Proposal=decision.steps.Count>0?decision:null;
            if(decision.decision=="accept" && decision.steps.Count>0 && pendingName==Selected)Start(decision,false);
            else if(person.Proposal!=null)Notice="有个小约定，按 F8 看看要不要答应。";
            else Notice="聊完啦，继续一起玩。";
            Persist();
        }catch(Exception e){Notice=e is InvalidOperationException?e.Message:"这句话没想清楚，先不行动。";AddLine(Person(pendingName).Chat,"提示",Notice);Persist();}
    }
    public void Quick(string skill) {
        var d=new Decision{decision="negotiate",title=Decision.Labels[skill],speech="一起"+Decision.Labels[skill]+"，好吗？",steps=new(){new Step{skill=skill,count=1}}};
        Current.Proposal=d;Notice="快捷活动已放到约定卡，点同意开始。";Persist();
    }
    public void AcceptProposal(bool forced) {
        if(Current.Proposal==null){Notice="还没有待决定的小约定。";return;}
        if(Current.Proposal.decision=="refuse" && !forced){Notice="对方已经拒绝。可以换个提议，或明确选择强制。";return;}
        Start(Current.Proposal,forced);
    }
    private void Start(Decision d,bool forced) {
        if(Current.Job?.Status is "active" or "waiting" or "paused"){Notice="先完成或停止当前约定，再开始新的。";return;}
        if(!Actor(World(),Selected).HasValue){Notice="先点击邀请同行，再开始约定。";return;}
        Current.Job=new Job{Title=d.title,Steps=d.steps.Select(s=>new Step{skill=s.skill,count=s.count}).ToList(),Forced=forced};
        Current.Proposal=null;
        AddLine(Current.Memories,"约定",d.title+"："+d.PlanText());
        Say(Selected,forced?"好，这次听你的。之后也记得问问我想做什么。":"说好了，一起做完这个小约定。");
        Notice="约定开始；关闭面板后队友会行动。";Persist();
    }
    public void Decline(){Current.Proposal=null;Say(Selected,"好，那就换个时候。陪着也挺好的。");Persist();}
    public void Cancel() {
        generation++;pending=null;
        var job=Current.Job;
        if(job!=null && job.Status is "active" or "waiting" or "paused") {
            if(job.Command!=null)try{
                using var result=JsonDocument.Parse(api!.CancelAction(job.Command));
                if(result.RootElement.GetProperty("status").GetString()=="succeeded")ApplyResult(Selected,Current,result.RootElement);
            }catch{}
            if(job.Status!="fulfilled") {
                job.Status="cancelled";job.Command=null;job.Detail="由玩家结束";
                AddLine(Current.Memories,"取消",$"{job.Title}，结束前完成 {job.Completed} 个步骤动作。");
            }
        }
        try{var actor=Actor(World(),Selected);if(actor.HasValue)Request(actor.Value.GetProperty("id").GetString()!,"follow");}catch{}
        Notice="已停止当前约定，恢复跟随。";Persist();
    }
    public void Resume(){if(Current.Job?.Status is "paused" or "waiting"){Current.Job.Status="active";Current.Job.Command=null;Current.Job.WaitingSeconds=0;Notice="继续之前的约定。";Persist();}}
    public static string JobText(Job? job)=>job==null?"没有约定":job.Status switch {
        "fulfilled"=>"约定完成 · "+job.Title,"cancelled"=>"约定已结束","failed"=>"遇到困难 · "+job.Detail,
        "paused"=>"等待继续 · "+job.Title,"waiting"=>"等你带路 · "+job.Detail,
        _=>job.Index<job.Steps.Count?$"{job.Title} · {Decision.Labels[job.Steps[job.Index].skill]} {job.DoneInStep}/{job.Steps[job.Index].count}":job.Title};
    private void Update(object? sender,UpdateTickedEventArgs e) {
        if(!Context.IsWorldReady || api==null)return;
        CompleteReply();
        foreach(var pair in Data.People.Where(p=>p.Value.Job?.Command!=null).ToArray())Poll(pair.Key,pair.Value);
        slowTick+=Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
        if(slowTick<1)return;slowTick=0;
        if(!Context.IsPlayerFree)return;
        foreach(var pair in Data.People.Where(p=>p.Value.Job?.Status is "active" or "waiting").ToArray())if(pair.Value.Job!.Command==null)Dispatch(pair.Key,pair.Value);
        if(!Settings.Autonomy || Thinking || Current.Proposal!=null || Current.Job?.Status is "active" or "waiting" or "paused" || DateTime.UtcNow<autoAt)return;
        autoAt=DateTime.UtcNow.AddSeconds(Math.Max(30,Settings.AutoIntervalSeconds));
        var world=World();var actor=Actor(world,Selected);if(!actor.HasValue || actor.Value.GetProperty("in_combat").GetBoolean())return;
        string signature=Selected+"|"+world.GetProperty("location").GetString()+"|"+Game1.Date.TotalDays+"|"+Current.Energy;
        if(signature==lastAutoSignature)return;lastAutoSignature=signature;
        Send("现在没有玩家任务。看看身边，结合我们的经历，说一句有趣的话或提议一个小约定。",true);
    }
    private void Dispatch(string name,Companion person) {
        var job=person.Job!;if(job.Index>=job.Steps.Count)return;
        var step=job.Steps[job.Index];
        try {
            var world=World();var actor=Actor(world,name);
            if(!actor.HasValue){Fail(name,person,"队友已离队");return;}
            var a=actor.Value;string? target=null;
            if(step.skill is "mine" or "water" or "harvest") {
                var targets=a.GetProperty("candidates").EnumerateArray().Where(c=>c.GetProperty("skill").GetString()==step.skill && !job.SkippedTargets.Contains(c.GetProperty("target_id").GetString()!)).ToArray();
                if(targets.Length==0){Wait(name,person,"附近没有可做的目标，带我换个地方吧。");return;}
                target=targets[0].GetProperty("target_id").GetString();
            }
            if(step.skill=="fish" && !a.GetProperty("fishing_available").GetBoolean()){Wait(name,person,"带我到附近能站稳的水边吧。");return;}
            if(a.GetProperty("in_combat").GetBoolean()){Wait(name,person,"先应对眼前的怪物，再继续约定。");return;}
            var result=Request(a.GetProperty("id").GetString()!,step.skill,target,step.skill=="rest"?20:30);
            job.Command=result.GetProperty("command_id").GetString();job.Status="active";job.Detail="";job.WaitingSeconds=0;Persist();
            ApplyResult(name,person,result);
        }catch(Exception){Wait(name,person,"暂时无法行动，先靠近我，或换个位置。");}
    }
    private void Wait(string name,Companion person,string detail) {
        var job=person.Job!;job.WaitingSeconds++;
        if(job.Status!="waiting" || job.Detail!=detail){job.Status="waiting";job.Detail=detail;Say(name,detail);Persist();}
        if(job.WaitingSeconds>180)Fail(name,person,"等了很久还没有合适条件；可以换个约定。");
    }
    private void Poll(string name,Companion person) {
        try{using var doc=JsonDocument.Parse(api!.PollAction(person.Job!.Command!));ApplyResult(name,person,doc.RootElement);}
        catch{Fail(name,person,"任务连接中断，未确认完成。");}
    }
    private void ApplyResult(string name,Companion person,JsonElement result) {
        var job=person.Job!;string status=result.GetProperty("status").GetString()!;
        if(status=="running")return;
        if(status!="succeeded") {
            string error=result.TryGetProperty("error",out var reason)?reason.GetString()??status:status;
            job.Command=null;
            if(error=="location_changed"){Wait(name,person,"换地方了，继续看看附近能做什么。");return;}
            if(error is "path_stalled" or "task_timeout" or "recovery_warp" or "target_changed_without_actor_evidence"
                && job.RecoveryAttempts<2 && result.TryGetProperty("evidence",out var evidence)
                && evidence.TryGetProperty("target_id",out var target) && !string.IsNullOrEmpty(target.GetString())) {
                job.SkippedTargets.Add(target.GetString()!);job.RecoveryAttempts++;
                Wait(name,person,"这条路走不通，我换个附近的目标再试。");return;
            }
            Fail(name,person,error);return;
        }
        var step=job.Steps[job.Index];person.Outcome(step.skill,job.Forced);
        string detail=Decision.Labels[step.skill];
        if(result.TryGetProperty("evidence",out var ev)) {
            if(ev.TryGetProperty("resumes",out var resumed))job.Resumes+=resumed.GetInt32();
            if(step.skill=="fish" && ev.TryGetProperty("caught_items",out var items)) detail+=$"，收获 {items.GetArrayLength()} 件（可能含杂物）";
        }
        AddLine(person.Memories,"一起做过",detail);
        bool complete=job.Advance();
        if(complete) {
            if(!job.Rewarded) {
                job.Rewarded=true;person.Bond=Math.Clamp(person.Bond+(job.Forced?0:4),0,100);
                if(person.RewardDay!=Game1.Date.TotalDays){person.RewardDay=Game1.Date.TotalDays;person.FriendshipReward=0;}
                var npc=Game1.getCharacterFromName(name);
                if(npc!=null && !job.Forced && person.FriendshipReward<6){
                    if(!Game1.player.friendshipData.ContainsKey(name))Game1.player.friendshipData[name]=new Friendship(0);
                    Game1.player.changeFriendship(2,npc);person.FriendshipReward+=2;
                }
            }
            AddLine(person.Memories,"兑现约定",job.Title);
            Say(name,job.Forced?"做完啦。下一个活动，让我来选一次吧？":"约定完成！这下我们又多了一件一起做过的事。");
            Notice="约定完成 · "+job.Title;
        } else Say(name,$"{detail}好了。接下来是{Decision.Labels[job.Steps[job.Index].skill]}，我记着呢。");
        Persist();
    }
    private void Fail(string name,Companion person,string error) {
        string detail=error switch {
            "path_stalled" or "task_timeout" or "recovery_warp"=>"这条路暂时走不通，靠近目标后再试吧。",
            "target_changed_without_actor_evidence"=>"目标已经变化，这次不能算我完成的。",
            "actor_dismissed"=>"队友已经离队。",
            "task_interrupted"=>"活动被其他操作打断了。",
            "unreachable_after_combat"=>"打完怪后找不到回去的路了。",
            _=>error
        };
        var job=person.Job!;job.Status="failed";job.Detail=detail;job.Command=null;
        AddLine(person.Memories,"未完成",job.Title+"："+detail);Say(name,"这次没能完成，我们换个办法吧。");Persist();
    }
    private void Say(string name,string text) {
        AddLine(Person(name).Chat,name,text);
        bubbles[name]=(text,DateTime.UtcNow.AddSeconds(10));
        Persist();
    }
    private void AddLine(List<Line> list,string who,string text) {
        list.Add(new Line{Who=who,Text=text,Day=Game1.Date.TotalDays});
        if(list.Count>60)list.RemoveRange(0,list.Count-60);
    }
    public string StatusJson()=>JsonSerializer.Serialize(new{selected=Selected,thinking=Thinking,notice=Notice,person=Current,calls=Data.Calls,tokens=Data.Tokens});
    private void Capture() {
        if(capturePath==null)return;string path=capturePath;capturePath=null;
        try {
            // Stardew draws UI into a separate render target before compositing it.
            if(Game1.game1.uiScreen is { } ui) {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var output=File.Create(path);ui.SaveAsPng(output,ui.Width,ui.Height);
                Monitor.Log("Saved Together UI render target.",LogLevel.Info);return;
            }
            var device=Game1.game1.GraphicsDevice;int w=device.PresentationParameters.BackBufferWidth,h=device.PresentationParameters.BackBufferHeight;
            var colors=new Color[w*h];device.GetBackBufferData(colors);
            using var image=new Texture2D(device,w,h);image.SetData(colors);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);using var stream=File.Create(path);image.SaveAsPng(stream,w,h);
            Monitor.Log("Saved Together panel screenshot.",LogLevel.Info);
        }catch{Monitor.Log("Screenshot unavailable in this graphics mode.",LogLevel.Warn);}
    }
}
