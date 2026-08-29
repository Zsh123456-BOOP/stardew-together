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
    string ConfigureFarm(string json); NPC? GetCharacter(string name); bool ResumeDay(string name); bool AttachCustomCompanion(string name);
}
public sealed class Config {
    public SButton OpenKey {get;set;}=SButton.F8;
    public string ApiKeyFile {get;set;}=".env";
    public string Model {get;set;}="deepseek-flash";
    public bool Autonomy {get;set;}=true;
    public int AutoIntervalSeconds {get;set;}=90;
    public int MaxCallsPerDay {get;set;}=24;
    public bool AllowTrialRecruitment {get;set;}=true;
    // Accept old config files, but never let a stored multiplier change game time.
    public double AutoplayClockRate {get=>1;set { }}
    public int AutoplayDecisionDelayMs {get;set;}=250;
    public int AutoplayMaxCallsPerDay {get;set;}=180;
    public long ModelTokenBudgetPerDay {get;set;}=1000000;
    public int ModelRequestByteLimit {get;set;}=240000;
    public bool EnableLab {get;set;}
}

public sealed partial class ModEntry:Mod {
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
    private double slowTick;
    private string? capturePath;
    private int recordingFrame=-1;
    private DateTime recordingAt;
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
            BindCompanionArchive(name,person);
            if(Context.IsWorldReady){person.NewDay(Game1.Date.TotalDays);person.Life.Tick(Game1.Date.TotalDays,Minute,false,person.Profile);}
        }
        return person;
    }
    public override void Entry(IModHelper helper) {
        Settings=helper.ReadConfig<Config>();
        SetupCustomPartner();
        SetupKnowledge();
        SetupAutoplay();
        NativeQuestIdentity.Install(ModManifest.UniqueID);
        helper.Events.GameLoop.GameLaunched+=(_,_)=>{
            api=helper.ModRegistry.GetApi<ICompanionControl>("ThaliaFawnheart.TheStardewSquad");
            Notice=api==null?"需要带同行接口的 Squad 版本。":"同行已准备好，按 F8 打开。";
        };
        helper.Events.GameLoop.SaveLoaded+=(_,_)=>Load();
        helper.Events.GameLoop.DayEnding+=(_,_)=>CheckpointJobs();
        helper.Events.GameLoop.Saving+=(_,_)=>{if(canPersist){SaveCustomPartner();ArchiveCompanionCheckpoint();Helper.Data.WriteSaveData("together-v2",Data);}};
        helper.Events.GameLoop.ReturnedToTitle+=(_,_)=>{ResetAgentRuntime();playerExecutor.ClearWorld();generation++;pending=null;api?.Reset();Data=new();ResetKnowledge();};
        helper.Events.GameLoop.DayStarted+=(_,_)=>{
            foreach(var p in Data.People.Values) {p.NewDay(Game1.Date.TotalDays);if(p.Job?.Status=="paused" && p.Job.Origin=="autonomous")p.Job.Status="active";}
            EnsureBudget();autoAt=DateTime.UtcNow.AddSeconds(30);
        };
        helper.Events.Input.ButtonPressed+=(_,e)=>{
            if(Context.IsWorldReady && e.Button==Settings.OpenKey && (Game1.activeClickableMenu==null || Game1.activeClickableMenu is CompanionMenu)) {
                helper.Input.Suppress(e.Button);
                if(AutoplayRunning)PauseAutoplay("打开同行面板，暂时交回控制");
                if(Game1.activeClickableMenu is CompanionMenu) Game1.exitActiveMenu();else Open();
            }
        };
        helper.Events.GameLoop.UpdateTicked+=Update;
        helper.Events.Display.RenderedWorld+=(_,e)=>{
            if(!Context.IsWorldReady)return;
            foreach(var pair in bubbles.Where(p=>p.Value.Until>DateTime.UtcNow)) {
                var npc=FindCharacter(pair.Key);
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
            if(agentToastUntil>DateTime.UtcNow) {
                string toast=Game1.parseText(agentToast,Font,Math.Min(600,Game1.uiViewport.Width-64));var size=Font.MeasureString(toast)*.85f;
                e.SpriteBatch.Draw(Game1.staminaRect,new Rectangle(24,Game1.uiViewport.Height-145,(int)size.X+24,(int)size.Y+20),new Color(248,239,216)*.96f);
                e.SpriteBatch.DrawString(Font,toast,new Vector2(36,Game1.uiViewport.Height-135),new Color(43,65,57),0,Vector2.Zero,.85f,SpriteEffects.None,1);
            }
            string text=$"{Settings.OpenKey} 同行 · 交谈对象 {Selected}  "+(AutoplayRunning || Data.Autoplay.Status=="paused" && Data.Autoplay.Goal.Length>0?AgentHud():Thinking?"正在想怎么回答你…":Current.Job?.Status is "active" or "waiting"?JobText(Current.Job):"聊聊 / 小约定");
            e.SpriteBatch.Draw(Game1.staminaRect,new Rectangle(16,Game1.uiViewport.Height-53,Math.Min(1180,Game1.uiViewport.Width-32),38),new Color(28,44,42)*.88f);
            e.SpriteBatch.DrawString(Font,text,new Vector2(28,Game1.uiViewport.Height-47),new Color(246,235,211),0,Vector2.Zero,.8f,SpriteEffects.None,1);
        };
        helper.Events.Display.Rendered+=(_,_)=>Capture();
        helper.ConsoleCommands.Add("together_open","Open Together panel.",(_,_)=>Open());
        helper.ConsoleCommands.Add("together_close","Close Together panel.",(_,_)=>{if(Game1.activeClickableMenu is CompanionMenu or EncyclopediaMenu)Game1.exitActiveMenu();});
        helper.ConsoleCommands.Add("together_auto","Toggle autonomous proposals.",(_,_)=>ToggleAuto());
        helper.ConsoleCommands.Add("together_export","Write sanitized state for local integration checks.",(_,_)=>{
            if(Context.IsWorldReady){RefreshFacts(true);Directory.CreateDirectory(Path.Combine(Helper.DirectoryPath,"diagnostics"));File.WriteAllText(Path.Combine(Helper.DirectoryPath,"diagnostics","state.json"),StatusJson());}
        });
        helper.ConsoleCommands.Add("together_say","Send a message through the same game chat pipeline.",(_,args)=>Send(string.Join(" ",args)));
        helper.ConsoleCommands.Add("together_select","Select a nearby or recruited NPC.",(_,args)=>{if(args.Length==1)Select(args[0]);});
        helper.ConsoleCommands.Add("together_preset","Set one of the four persona presets.",(_,args)=>{if(args.Length==1)SetPreset(args[0]);});
        helper.ConsoleCommands.Add("together_accept","Accept the visible proposal.",(_,_)=>AcceptProposal(false));
        helper.ConsoleCommands.Add("together_force","Explicitly force the visible proposal.",(_,_)=>AcceptProposal(true));
        helper.ConsoleCommands.Add("together_cancel","Cancel thinking and the selected companion's task.",(_,_)=>Cancel());
        helper.ConsoleCommands.Add("together_resume","Resume a saved or waiting promise.",(_,_)=>Resume());
        helper.ConsoleCommands.Add("together_farm","Share recurring farm care.",(_,_)=>{if(Context.IsWorldReady)AddFarmProject();});
        helper.ConsoleCommands.Add("together_bundle","Plan next incomplete bundle from real save data.",(_,_)=>{if(Context.IsWorldReady)AddBundleProject();});
        helper.ConsoleCommands.Add("together_lab_cleanup","Remove the dedicated homeless animal fixture before overnight checks.",(_,_)=>{
            if(Settings.EnableLab && Context.IsWorldReady && Game1.player.Name=="AgentLab")Game1.getFarm().animals.Remove(-449404282);
        });
        helper.ConsoleCommands.Add("together_lab_resource","AgentLab-only resource failure fixtures: reserve/unreserve/full/empty/unmark/restore.",(_,args)=>{
            if(!Settings.EnableLab || !Context.IsWorldReady || Game1.player.Name!="AgentLab")return;
            string mode=args.FirstOrDefault()??"";
            var objects=Game1.getFarm().objects;
            if(mode=="reserve")Data.Projects.Add(new(){Kind="lab-reserve",Needs=new(){new(){Item="(O)378",Count=10,Quality=0}}});
            if(mode=="unreserve")Data.Projects.RemoveAll(p=>p.Kind=="lab-reserve");
            if(objects.TryGetValue(new Vector2(50,26),out var o) && o is StardewValley.Objects.Chest output) {
                if(mode=="empty")output.Items.Clear();
                if(mode=="full"){output.Items.Clear();for(int i=0;i<36;i++)output.Items.Add(ItemRegistry.Create("(O)390",999));}
            }
            if(objects.TryGetValue(new Vector2(44,26),out var s) && s is StardewValley.Objects.Chest supply && mode is "unmark" or "restore")
                supply.modData["stardewagent.together/chest-role"]=mode=="unmark"?"none":"supplies";
            RefreshFacts(true);
        });
        helper.ConsoleCommands.Add("together_life_tick","AgentLab-only trigger a fresh autonomous decision.",(_,_)=>{
            if(Settings.EnableLab && Context.IsWorldReady && Game1.player.Name=="AgentLab") {autoAt=DateTime.MinValue;Current.Life.LastDecisionMinute=-1000;TickLife();}
        });
        helper.ConsoleCommands.Add("together_status","Print sanitized Together status.",(_,_)=>Monitor.Log(StatusJson(),LogLevel.Info));
        helper.ConsoleCommands.Add("together_record","AgentLab-only capture 120 world frames at 10 fps.",(_,_)=>{
            if(Settings.EnableLab && Context.IsWorldReady && Game1.player.Name=="AgentLab") {recordingFrame=0;recordingAt=DateTime.MinValue;}
        });
        helper.ConsoleCommands.Add("together_capture","Save a development screenshot in the Mod's screenshots folder.",(_,_)=>{
            if(Settings.EnableLab && Context.IsWorldReady && Game1.player.Name=="AgentLab") capturePath=Path.Combine(Helper.DirectoryPath,"screenshots","panel.png");
        });
        helper.ConsoleCommands.Add("together_lab_ui","AgentLab-only Chinese panel rendering check: chat/persona/history.",(_,args)=>{
            if(!Settings.EnableLab || !Context.IsWorldReady || Game1.player.Name!="AgentLab")return;
            Game1.exitActiveMenu();
            LocalizedContentManager.CurrentLanguageCode=LocalizedContentManager.LanguageCode.zh;
            Game1.activeClickableMenu=new CompanionMenu(this,args.FirstOrDefault() switch {"persona"=>1,"history"=>2,"life"=>3,"farm"=>4,_=>0});
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
        agentSaveEpoch=Guid.NewGuid().ToString("N");
        ResetAgentRuntime();playerExecutor.ClearWorld();generation++;pending=null;canPersist=true;api?.Reset();
        ResetKnowledge();
        try{Data=Helper.Data.ReadSaveData<SaveData>("together-v2") ?? (File.Exists(SavePath)?JsonSerializer.Deserialize<SaveData>(File.ReadAllText(SavePath))??new():new());}
        catch{Data=new();canPersist=false;Notice="同行记录无法读取，本次暂停写入以保留原文件。";return;}
        if(Data.SchemaVersion>6){canPersist=false;Notice="这是更新版本的同行记录，请先更新 Mod；本次不覆盖它。";return;}
        Data.SchemaVersion=6;Data.Autoplay.Schedule.Suspend();Data.Autoplay.ReconcileSleep(Game1.Date.TotalDays);
        AttachMemoryArchive();
        if(Data.Autoplay.Status=="running"){Data.Autoplay.Status="paused";Data.Autoplay.Detail="重新载入后先核对状态，使用 together_agent resume 继续。";}
        factsMinute=-1;RefreshFacts(true);
        foreach(var p in Data.People.Values) if(p.Job?.Status is "active" or "waiting") {p.Job.Status="paused";p.Job.Command=null;p.Job.TravelCommand=null;p.Job.Detail="上次的小约定还在；点继续后重新检查环境。";}
        foreach(var p in Data.People.Values){p.DailyCompanion??=p.Job!=null;p.NewDay(Game1.Date.TotalDays);}
        // Reclaim previously managed companions after Squad restores its saved team.
        // A paused promise must not fall through to unrelated autonomous Squad labor.
        try {
            foreach(var actor in World().GetProperty("actors").EnumerateArray())
                if(Data.People.ContainsKey(actor.GetProperty("name").GetString()!))Request(actor.GetProperty("id").GetString()!,"follow");
        }catch{Notice="队伍尚未就绪；靠近队友后可重新邀请。";}
        EnsureBudget();autoAt=DateTime.UtcNow.AddSeconds(30);
        Helper.GameContent.InvalidateCache("Data/Characters");EnsureCustomPartner(true);
        if(Settings.EnableLab)Game1.options.pauseWhenOutOfFocus=false;
    }
    // State is committed by SMAPI's Saving event together with the game save.
    // Dialogue/UI changes remain in memory until that checkpoint, so a day reload
    // cannot retain future actions and spend the same rewards twice.
    public void Persist() { }
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
        generation++;pendingGoalWork=null;Data.Selected=name;Persist();
    }
    public void Open(){if(Context.IsWorldReady && Game1.activeClickableMenu==null){RefreshFacts(true);Game1.activeClickableMenu=new CompanionMenu(this);}}
    public void SetPreset(string name){if(!Context.IsWorldReady)return;Current.Profile=Profile.Preset(name);Current.Life.WishDay=-1;Current.Life.Tick(Game1.Date.TotalDays,Minute,true,Current.Profile);Notice="人设已更新，下一句话就会生效。";Persist();}
    public void ToggleAuto(){Settings.Autonomy=!Settings.Autonomy;Helper.WriteConfig(Settings);Notice=Settings.Autonomy?"队友可以自己安排活动。":"暂停选择新的自主活动；已有安排可继续或停止。";}
    private void EnsureBudget(){ModelRequestBudget.Configure(UsagePath+".budget.json",Settings.ModelTokenBudgetPerDay,Settings.ModelRequestByteLimit);if(Data.BudgetDay!=Game1.Date.TotalDays){Data.BudgetDay=Game1.Date.TotalDays;Data.Calls=0;}LoadUsage();}
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
    public NPC? FindCharacter(string name)=>api?.GetCharacter(name) ?? Game1.getCharacterFromName(name);
    public void Recruit() {
        if(!Connected || !Context.IsWorldReady)return;
        try{Request(Selected,"recruit");Current.DailyCompanion=true;Say(Selected,"那就一起走吧。今天想先做什么？");Notice="已邀请同行。";}
        catch(Exception){Notice="暂时无法邀请：请靠近角色，并检查队伍人数与角色日程。";}
    }
    public void Dismiss() {
        Current.DailyCompanion=false;Cancel();try{var a=Actor(World(),Selected);if(a.HasValue)Request(a.Value.GetProperty("id").GetString()!,"dismiss");Notice="伙伴开始沿路返回自己的日程地点，到达后结束同行。";}catch{Notice="暂时不能结束同行。";}
    }
    public void Send(string message,bool autonomous=false) {
        if(!Context.IsWorldReady || !Connected || string.IsNullOrWhiteSpace(message))return;
        if(!autonomous && HandleGoalMessage(message))return;
        if(!autonomous && IsKnowledgeQuestion(message)){AskKnowledge(message);return;}
        if(HandleLocalConversation(message))return;
        if(Thinking){Notice="等我把这句话想完，或点停止。";return;}
        if(message.Length>400){Notice="一句话最多 400 字。";return;}
        EnsureBudget();if(Data.Calls>=Math.Clamp(Settings.MaxCallsPerDay,1,100)){Notice="今天的聊天额度用完啦，仍可继续约定和使用快捷活动。";return;}
        JsonElement world;try{world=World();}catch{Notice="游戏状态暂不可用。";return;}
        RefreshFacts();pendingGoalWork=null;pendingAutonomous=false;pendingOptions.Clear();
        var person=Current; var actor=Actor(world,Selected);
        if(!autonomous){AddLine(person.Chat,"你",message);person.Social.Reply(message,Game1.Date.TotalDays);}
        var context=new {event_type=autonomous?"idle":"player_message",player_message=message,npc=Selected,profile=person.Profile,
            mood=person.Mood,energy=person.Energy,bond=person.Bond,actor,world=new{location=world.GetProperty("location").GetString(),time=Game1.timeOfDay,
                health=Game1.player.health,season=Game1.currentSeason,raining=Game1.isRaining,threats=world.GetProperty("threats")},
            shared_goals=GoalContext(),farm=PromptFarm(),projects=Data.Projects.Where(p=>p.Status=="active").Take(8),today=Data.Today.Take(12),social=PromptSocial(person),needs=person.Life.ModelState(),pace=Data.Pace,
            recalled_experiences=MemoryRecall.Select(person.Life.Experiences,message,Game1.Date.TotalDays),
            archived_experiences=memoryArchive?.Read(message,Selected,2),
            memories=person.Memories.TakeLast(5).ToArray(),conversation=person.Chat.TakeLast(5).ToArray(),current_promise=person.Job,
            note="bond是本Mod亲近感，actor.relationship是真实好感。没有招募时可以聊天，行动需先邀请。"};
        Data.Calls++;RecordUsage();pendingName=Selected;pendingGeneration=generation;
        string file=Path.IsPathRooted(Settings.ApiKeyFile)?Settings.ApiKeyFile:Path.Combine(Helper.DirectoryPath,Settings.ApiKeyFile);
        pendingKnowledge=false;pending=ModelClient.Ask(file,Settings.Model,context);Notice="正在想怎么回答你…";Persist();
    }
    private void CompleteReply() {
        if(pending==null || !pending.IsCompleted || (!Context.IsPlayerFree && Game1.activeClickableMenu is not (EncyclopediaMenu or SharedGoalsMenu or CompanionMenu)))return;
        var task=pending;pending=null;
        if(pendingGeneration!=generation)return;
        try {
            var result=task.GetAwaiter().GetResult();Data.Tokens+=result.Tokens;RecordUsage();
            if(CompleteGoalWork(result))return;
            if(CompleteKnowledgeReply(result))return;
            if(HandleAutonomousReply(result))return;
            var decision=Decision.Parse(result.Json);var person=Person(pendingName);person.LastDecision=decision;
            if(pendingName==Selected){if(decision.project=="farm")AddFarmProject();else if(decision.project=="bundle")AddBundleProject();}
            Say(pendingName,decision.speech);
            person.Proposal=decision.steps.Count>0?decision:null;person.ProposalAutonomous=false;
            if(decision.decision=="accept" && decision.steps.Count>0 && pendingName==Selected)Start(decision,false);
            else if(person.Proposal!=null)Notice="有个小约定，按 F8 看看要不要答应。";
            else Notice="聊完啦，继续一起玩。";
            Persist();
        }catch(Exception e){
            pendingGoalWork=null;
            if(pendingKnowledge){KnowledgeFailure(e is InvalidOperationException?e.Message:"模型回复格式无法核对，显示本地资料。");return;}
            if(pendingAutonomous) {
                var world=World();var actor=Actor(world,pendingName);var p=Person(pendingName);
                if(actor.HasValue && p.Job?.Status is not ("active" or "waiting" or "paused")) {
                    var fallback=OptionsFor(pendingName,p,SituationFor(pendingName,actor.Value,world),actor.Value).FirstOrDefault();
                    if(fallback!=null){p.Life.DecisionSource="local-after-model-error";p.Life.LastDecisionError=e is InvalidOperationException?e.Message:"invalid_model_response";StartOption(pendingName,fallback);}
                }
                return;
            }
            Notice=e is InvalidOperationException?e.Message:"这句话没想清楚，先不行动。";AddLine(Person(pendingName).Chat,"提示",Notice);Persist();}
    }
    public void Quick(string skill) {
        var d=new Decision{decision="negotiate",title=Decision.Labels[skill],speech="一起"+Decision.Labels[skill]+"，好吗？",steps=new(){new Step{skill=skill,count=1}}};
        Current.Proposal=d;Notice="快捷活动已放到约定卡，点同意开始。";Persist();
    }
    public void AcceptProposal(bool forced) {
        if(Current.Proposal==null){Notice="还没有待决定的小约定。";return;}
        if(Current.Proposal.decision=="refuse" && !forced){Notice="对方已经拒绝。可以换个提议，或明确选择强制。";return;}
        if(Current.Proposal.option_id?.StartsWith("goal:")==true) {
            var option=FreshGoalOption(Current.Proposal.option_id,Selected);
            if(option==null){Current.Proposal=null;Notice="这一步的条件已经变化，请重新选择。";return;}
            var goal=Data.SharedGoals.First(g=>option.Id.StartsWith("goal:"+g.Id+":"));
            string node=option.Id[("goal:"+goal.Id+":").Length..];AssignGoalNode(goal.Id,node,Selected);
            StartFor(Selected,new(){title=option.Title,steps=option.Steps},forced,"player",option.Id,option.Reason);
        } else Start(Current.Proposal,forced);
    }
    private void Start(Decision d,bool forced) => StartFor(Selected,d,forced);
    public void Decline(){Current.Proposal=null;Say(Selected,"好，那就换个时候。陪着也挺好的。");Persist();}
    public void Cancel() {
        generation++;pending=null;pendingGoalWork=null;pendingKnowledge=false;Current.Proposal=null;Current.ProposalAutonomous=false;
        var job=Current.Job;
        Current.Life.Suspended.Clear();Current.Life.LastDecisionMinute=Minute;
        if(job?.TravelCommand!=null){api!.CancelAction(job.TravelCommand);job.TravelCommand=null;}
        if(job!=null && job.Status is "active" or "waiting" or "paused") {
            if(job.Command!=null)try{
                using var result=JsonDocument.Parse(api!.CancelAction(job.Command));
                if(result.RootElement.GetProperty("status").GetString()=="succeeded")ApplyResult(Selected,Current,result.RootElement);
                else RecordInterrupted(Current,job,result.RootElement);
            }catch{}
            if(job.Status!="fulfilled") {
                job.Status="cancelled";job.Command=null;job.Detail="由玩家结束";
                AddLine(Current.Memories,"取消",$"{job.Title}，结束前完成 {job.Completed} 个步骤动作。");
            }
        }
        try{var actor=Actor(World(),Selected);if(actor.HasValue)Request(actor.Value.GetProperty("id").GetString()!,"follow");}catch{}
        Notice="已停止当前约定，恢复跟随。";Persist();
    }
    public void Resume(){if(Current.Job?.Status is "paused" or "waiting"){if(!Actor(World(),Selected).HasValue){Notice="先邀请同行，我们再继续之前的约定。";return;}Current.Job.Status="active";Current.Job.Command=null;Current.Job.WaitingSeconds=0;Notice="继续之前的约定。";Persist();}}
    public static string JobText(Job? job)=>job==null?"没有约定":job.FollowMode && job.Status=="active"?"持续陪伴 · "+job.Title:job.Status switch {
        "fulfilled"=>"约定完成 · "+job.Title,"exhausted"=>"暂时做完可达目标 · "+job.Detail,"cancelled"=>"约定已结束","failed"=>"遇到困难 · "+job.Detail,
        "paused"=>"等待继续 · "+job.Title,"waiting"=>"暂时等待 · "+job.Detail,
        _=>job.Index<job.Steps.Count?$"{job.Title} · {Decision.Labels[job.Steps[job.Index].skill]} {job.DoneInStep}/{job.Steps[job.Index].count}":job.Title};
    private void Update(object? sender,UpdateTickedEventArgs e) {
        long started=System.Diagnostics.Stopwatch.GetTimestamp();frameStages.Clear();
        try {UpdateCore(sender,e);}finally {ProfileFrame((System.Diagnostics.Stopwatch.GetTimestamp()-started)*1000.0/System.Diagnostics.Stopwatch.Frequency);}
    }
    private void UpdateCore(object? sender,UpdateTickedEventArgs e) {
        if(!Context.IsWorldReady || api==null || !canPersist)return;
        long stage=System.Diagnostics.Stopwatch.GetTimestamp();Knowledge.Tick();FrameStage("knowledge",ref stage);
        TickAutoplay();
        if(AutoplayRunning)return;
        CompleteReply();
        foreach(var pair in Data.People.Where(p=>p.Value.Job?.Command!=null).ToArray())Poll(pair.Key,pair.Value);
        slowTick+=Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
        if(slowTick<1)return;slowTick=0;
        if(!Context.IsPlayerFree || (!Game1.game1.IsActive && Game1.options.pauseWhenOutOfFocus))return;
        foreach(var pair in Data.People.Where(p=>p.Value.Job?.Status is "active" or "waiting").ToArray())if(pair.Value.Job!.Command==null)Dispatch(pair.Key,pair.Value);
        TickLife();
    }
    private void Dispatch(string name,Companion person) {
        var job=person.Job!;if(job.Index>=job.Steps.Count)return;
        var step=job.Steps[job.Index];
        if(job.OptionId.StartsWith("goal:") && job.Command==null) {
            RefreshFacts(true);
            var goal=Data.SharedGoals.FirstOrDefault(g=>job.OptionId.StartsWith("goal:"+g.Id+":"));
            var node=goal?.Nodes.FirstOrDefault(n=>job.OptionId=="goal:"+goal.Id+":"+n.Id);
            if(goal?.Status!="active" || node==null || node.Missing==0 || (node.Owner!="together" && node.Owner!=name)) {
                if(job.TravelCommand!=null)api?.CancelAction(job.TravelCommand);
                job.TravelCommand=null;job.Status="cancelled";job.Detail="共同目标或分工已变化，停止这一步，保留实际成果。";return;
            }
        }
        if(job.FollowMode)return;
        if(job.Origin=="autonomous" && person.Energy<20 && job.TravelCommand==null && person.Life.Suspended.Count<3 && step.skill is "clear" or "till" or "plant" or "feed" or "tend" or "forage" or "buy" or "ship" or "gift" or "refill" or "deposit" or "mine" or "water" or "harvest" or "pet" or "collect") {
            if(person.Life.Suspended.Count<3)person.Life.Suspended.Add(job);
            job.Status="paused";person.Job=null;
            StartOption(name,new(){Id="rest",Title="先歇会儿，再接着做",Reason="我需要恢复些精力",Steps=new(){new(){skill="rest"}}});return;
        }
        try {
            var world=World();var actor=Actor(world,name);
            if(!actor.HasValue){Fail(name,person,"队友已离队");return;}
            var a=actor.Value;string? target=null;
            if(job.TravelCommand!=null) {
                using var travel=JsonDocument.Parse(api!.PollAction(job.TravelCommand));
                var status=travel.RootElement.GetProperty("status").GetString();
                if(status=="running")return;
                job.TravelCommand=null;
                if(status!="succeeded"){Fail(name,person,"不能沿现有地图出口到达工作地点");return;}
            }
            if(step.location!=null && a.GetProperty("location").GetString()!=step.location) {
                using var travel=JsonDocument.Parse(api!.StartAction(JsonSerializer.Serialize(new{command_id=Guid.NewGuid().ToString("N"),actor_id=a.GetProperty("id").GetString(),skill="travel",destination=step.location})));
                job.TravelCommand=travel.RootElement.GetProperty("command_id").GetString();return;
            }
            if(step.skill is "clear" or "till" or "plant" or "feed" or "tend" or "forage" or "buy" or "ship" or "gift" or "refill" or "deposit" or "mine" or "water" or "harvest" or "pet" or "collect") {
                var targets=a.GetProperty("candidates").EnumerateArray().Where(c=>c.GetProperty("skill").GetString()==step.skill && (step.target_item==null || CandidateMatches(c,step.target_item)) && !job.SkippedTargets.Contains(c.GetProperty("target_id").GetString()!)).ToArray();
                if(targets.Length==0){
                    if(job.Origin=="autonomous") {job.Status="exhausted";job.Detail="当前可达范围已没有待处理目标；实际完成 "+job.Completed+" 个";person.Life.RetryAfter[job.OptionId]=Minute+60;RefreshFacts(true);return;}
                    Wait(name,person,"当前可达范围没有目标，约定数量尚未做完。");return;
                }
                target=targets[0].GetProperty("target_id").GetString();
            }
            if(step.skill=="fish" && !a.GetProperty("fishing_available").GetBoolean()){Wait(name,person,"带我到附近能站稳的水边吧。");return;}
            if(a.GetProperty("in_combat").GetBoolean()){Wait(name,person,"先应对眼前的怪物，再继续约定。");return;}
            var result=Request(a.GetProperty("id").GetString()!,step.skill,target,job.RemainingSeconds??(step.skill=="rest"?20:30));
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
            if(error is "path_stalled" or "task_timeout" or "recovery_warp" or "target_changed_without_actor_evidence" or "target_moved"
                && job.RecoveryAttempts<2 && result.TryGetProperty("evidence",out var evidence)
                && evidence.TryGetProperty("target_id",out var target) && !string.IsNullOrEmpty(target.GetString())) {
                job.SkippedTargets.Add(target.GetString()!);job.RecoveryAttempts++;
                Wait(name,person,"这条路走不通，我换个附近的目标再试。");return;
            }
            Fail(name,person,error);return;
        }
        var step=job.Steps[job.Index];
        if(step.skill=="follow" && job.Origin=="player" && job.Steps.Count==1) {
            job.Command=null;job.FollowMode=true;job.Status="active";job.Detail="持续跟随，直到你改变安排";return;
        }
        person.Outcome(step.skill,job.Forced);
        string detail=Decision.Labels[step.skill];
        if(result.TryGetProperty("evidence",out var ev)) {
            if(ev.TryGetProperty("resumes",out var resumed))job.Resumes+=resumed.GetInt32();
            if(step.skill=="fish" && ev.TryGetProperty("caught_items",out var items)) detail+=$"，收获 {items.GetArrayLength()} 件（可能含杂物）";
        }
        AddLine(person.Memories,job.Origin=="autonomous"?"自己做过":"完成动作",detail);
        bool complete=job.Advance();
        if(complete) {
            if(!job.Rewarded) {
                job.Rewarded=true;person.Bond=Math.Clamp(person.Bond+(job.Forced || job.Origin=="autonomous"?0:4),0,100);
                if(person.RewardDay!=Game1.Date.TotalDays){person.RewardDay=Game1.Date.TotalDays;person.FriendshipReward=0;}
                var npc=FindCharacter(name);
                if(npc!=null && step.skill!="follow" && job.Origin=="player" && !job.Forced && person.FriendshipReward<6){
                    if(!Game1.player.friendshipData.ContainsKey(name))Game1.player.friendshipData[name]=new Friendship(0);
                    Game1.player.changeFriendship(2,npc);person.FriendshipReward+=2;
                }
            }
            AddLine(person.Memories,job.Origin=="autonomous"?"自己的收获":"兑现约定",job.Title);
            if(step.skill!="follow")person.Life.Complete(step.skill,Game1.Date.TotalDays,Minute,$"做了“{job.Title}”（{detail}；累计 {job.Completed} 个动作）",job.Origin=="autonomous" && (job.OptionId is "fish" or "mine" or "beach_trip" || person.Profile.Likes.Contains(Decision.Labels[step.skill])));
            else job.Detail="已进入跟随模式；是否来到身边仍取决于实际路径。";
            RememberResult(name,person,job,step.skill,result);
            person.Life.LastDecisionMinute=Minute;RefreshFacts(true);
            if(job.Origin=="player")Say(name,job.Forced?"做完啦。下一个活动，让我来选一次吧？":step.skill=="refill"?"原料补好了，接下来等机器慢慢加工。你那边怎么样？":"答应你的事做好了，你那边还顺利吗？");
            Notice="约定完成 · "+job.Title;
        } else if(job.Origin=="player" && job.DoneInStep==0)Say(name,$"{detail}好了。接下来是{Decision.Labels[job.Steps[job.Index].skill]}，我记着呢。");
        Persist();
    }
    private void Fail(string name,Companion person,string error) {
        string detail=error switch {
            "path_stalled" or "task_timeout" or "recovery_warp"=>"这条路暂时走不通，靠近目标后再试吧。",
            "target_changed_without_actor_evidence"=>"目标已经变化，这次不能算我完成的。",
            "actor_dismissed"=>"队友已经离队。",
            "task_interrupted"=>"活动被其他操作打断了。",
            "unreachable_after_combat"=>"打完怪后找不到回去的路了。",
            "output_chest_full"=>"收货箱放不下了，我先保管这些东西。",
            "pouch_full" or "inventory_full_or_machine_rejected"=>"身上放不下了，先找收货箱整理一下。",
            "budget_exceeded"=>"这次会超过你留的预算，我先不买。",
            "stock_or_order_changed" or "shop_or_permission_changed"=>"商店或购物安排变了，我先停下来核对。",
            "supply_changed_or_reserved" or "supply_now_reserved" or "ingredients_changed"=>"原料被挪动了，或已经留作别的用途，这次先不动它。",
            "seed_missing_or_reserved" or "seed_missing"=>"现在没有可以取用的种子，先保留这块地。",
            "hay_unavailable"=>"没有可用的干草了，补好后我再继续。",
            "production_permission_or_target_changed"=>"地块安排或目标状态变了，我停下重新看看。",
            "home_route_unavailable"=>"暂时没有走得通的回家路线，我先留在这里。",
            "sale_permission_or_reservation_changed"=>"出售安排变了，这些物品先留在我身上。",
            _=>"现在的条件还不合适，我先停下来。"
        };
        var job=person.Job!;person.Life.RetryAfter[job.OptionId]=Minute+90;job.Status="failed";job.Detail=detail;job.Command=null;
        AddLine(person.Memories,"未完成",job.Title+"："+detail);Say(name,"这次没能完成，我们换个办法吧。");Persist();
    }
    private void Say(string name,string text) {
        AddLine(Person(name).Chat,name,text);
        bubbles[name]=(text,DateTime.UtcNow.AddSeconds(10));
        Persist();
    }
    private void AddLine(List<Line> list,string who,string text) {
        string? owner=Data.People.FirstOrDefault(p=>ReferenceEquals(p.Value.Chat,list)||ReferenceEquals(p.Value.Memories,list)).Key;
        if(owner!=null)memoryArchive?.Append(Game1.Date.TotalDays,owner,"conversation",AgentJson.Encode(new{who,text,kind=ReferenceEquals(Data.People[owner].Chat,list)?"chat":"event_memory"}));
        list.Add(new Line{Who=who,Text=text,Day=Game1.Date.TotalDays});
        if(list.Count>60)list.RemoveRange(0,list.Count-60);
    }
    public string StatusJson()=>JsonSerializer.Serialize(new{autoplay=AutoplayDiagnostics(),event_up=Game1.eventUp,menu=Game1.activeClickableMenu?.GetType().Name,selected=Selected,thinking=Thinking,notice=Notice,person=Current,people=Data.People,projects=Data.Projects,shared_goals=Data.SharedGoals,facts=Facts,today=Data.Today,farm_policy=Data.FarmPolicy,performance=Performance(),reservations=Data.Reservations,pace=Data.Pace,calls=Data.Calls,tokens=Data.Tokens});
    private void Capture() {
        if(recordingFrame>=0 && DateTime.UtcNow>=recordingAt && Context.IsWorldReady) {
            try {
                var target=Game1.game1.screen;
                if(target!=null){string folder=Path.Combine(Helper.DirectoryPath,"screenshots","motion");Directory.CreateDirectory(folder);
                    using var output=File.Create(Path.Combine(folder,$"frame{recordingFrame:0000}.png"));target.SaveAsPng(output,target.Width,target.Height);
                    recordingFrame++;recordingAt=DateTime.UtcNow.AddMilliseconds(100);
                    if(recordingFrame>=120){recordingFrame=-1;Monitor.Log("Saved 120 world frames for motion review.",LogLevel.Info);}}
            }catch{recordingFrame=-1;Monitor.Log("Motion capture unavailable.",LogLevel.Warn);}
        }
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
