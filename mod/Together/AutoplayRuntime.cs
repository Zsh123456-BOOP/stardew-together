using System.Diagnostics;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private PlayerExecutor playerExecutor=null!;
    private AgentToolRegistry agentTools=null!;
    private Task<ModelReply>? agentPending;
    private CancellationTokenSource? agentCancellation;
    private DateTime agentNext;
    private readonly HashSet<string> agentWaiting=new();
    private int agentGeneration;
    private DateTime agentRequestedWait;
    private readonly Stopwatch agentWatch=new();
    private double agentLastLatency;
    private bool agentStarting;
    public bool AutoplayRunning=>Data.Autoplay.Status=="running";
    private void SetupAutoplay() {
        playerExecutor=new();agentTools=new(this,playerExecutor);
        Helper.Events.GameLoop.UpdateTicking+=(_,_)=>{
            if(!Context.IsWorldReady || Context.IsMultiplayer || !AutoplayRunning || Game1.eventUp || Game1.activeClickableMenu!=null || !Game1.shouldTimePass())return;
            // Only the native clock accumulator is accelerated. Every ten-minute event still runs in Game1.
            int extra=AutoplaySpeed.ExtraMilliseconds(Settings.AutoplayClockRate,Game1.currentGameTime.ElapsedGameTime.TotalMilliseconds,agentPending==null && !playerExecutor.Busy);
            Game1.gameTimeInterval+=extra;
        };
        Helper.Events.GameLoop.Saved+=(_,_)=>playerExecutor.Saved();
        Helper.Events.GameLoop.DayStarted+=(_,_)=>{
            if(playerExecutor.DayStarted())Data.Autoplay.SleepDays++;
            if(AutoplayRunning) {Data.Autoplay.Record("day_started",AgentJson.Encode(AgentSnapshot()));agentNext=DateTime.UtcNow.AddMilliseconds(250);}
        };
        Helper.Events.Input.ButtonPressed+=(_,e)=>{
            if(AutoplayRunning && e.Button==SButton.F10){Helper.Input.Suppress(e.Button);PauseAutoplay("玩家按 F10 暂停接管");}
            else if(AutoplayRunning && e.Button is SButton.W or SButton.A or SButton.S or SButton.D or SButton.Up or SButton.Down or SButton.Left or SButton.Right or SButton.Escape)
                PauseAutoplay("玩家接回控制");
        };
        Helper.ConsoleCommands.Add("together_agent","start <goal> / resume / pause / speed <1..8> / status",(_,args)=>{
            if(!Context.IsWorldReady || args.Length==0)return;
            switch(args[0]) {
                case "start":StartAutoplay(string.Join(" ",args.Skip(1)));break;
                case "resume":StartAutoplay(Data.Autoplay.Goal);break;
                case "pause":PauseAutoplay("玩家暂停");break;
                case "speed":if(args.Length==2 && double.TryParse(args[1],out double rate)){Settings.AutoplayClockRate=AutoplaySpeed.Clock(rate);Helper.WriteConfig(Settings);Notice=$"等待时游戏时钟 {Settings.AutoplayClockRate:0.#} 倍；走路与工具动画保持原速。";}break;
                case "status":Monitor.Log(JsonSerializer.Serialize(AutoplayDiagnostics()),LogLevel.Info);break;
            }
        });
    }
    public void StartAutoplay(string goal) {
        if(!Context.IsWorldReady || Context.IsMultiplayer || !canPersist){Notice="自主接管需要已载入的单人存档。";return;}
        if(string.IsNullOrWhiteSpace(goal) || goal.Length>2000){Notice="请给出不超过2000字的目标。";return;}
        if(AutoplayRunning)return;
        if(playerExecutor.Busy){Notice="先等待当前原生动作结束，或取消后再接管。";return;}
        generation++;pending=null;pendingGoalWork=null;pendingKnowledge=false;
        foreach(var name in Data.People.Keys.ToArray()) {
            var p=Person(name);if(p.Job is {Status:"active" or "waiting"} j){if(j.Command!=null)api?.CancelAction(j.Command);if(j.TravelCommand!=null)api?.CancelAction(j.TravelCommand);j.Command=null;j.TravelCommand=null;j.Status="paused";}
        }
        ResetAgentRuntime();playerExecutor.ClearStopped();
        if(Data.Autoplay.Goal!=goal)Data.Autoplay=new();
        Data.Autoplay.RunId=Guid.NewGuid().ToString("N");Data.Autoplay.StartDay=Game1.Date.TotalDays;
        Data.Autoplay.Record("new_run","开始新的接管片段。只有本片段的 tool_result 和 action_result 才是你实际调用工具的证据，目标文字不是完成记录。");
        Data.Autoplay.Goal=goal;Data.Autoplay.Status="running";Data.Autoplay.Detail="DeepSeek 接管；F10 或方向键随时暂停。";
        agentStarting=true;agentNext=DateTime.UtcNow;Notice=Data.Autoplay.Detail;
    }
    public void PauseAutoplay(string reason) {
        ResetAgentRuntime();Data.Autoplay.Status="paused";Data.Autoplay.Detail=reason;Notice=reason;
    }
    private void ResetAgentRuntime() {
        agentGeneration++;agentRequestedWait=DateTime.MinValue;agentCancellation?.Cancel();agentCancellation?.Dispose();agentCancellation=null;agentPending=null;
        foreach(string id in agentWaiting.Where(x=>!x.StartsWith("player:")))try{api?.CancelAction(id);}catch{}
        agentWaiting.Clear();playerExecutor?.Cancel();agentTools?.Reset();
    }
    private object AgentSnapshot()=>new{day=Game1.Date.TotalDays,date=Game1.Date.ToString(),time=Game1.timeOfDay,location=Game1.currentLocation.NameOrUniqueName,
        tile=new[]{Game1.player.TilePoint.X,Game1.player.TilePoint.Y},health=Game1.player.health,stamina=Game1.player.Stamina,money=Game1.player.Money,
        menu=Game1.activeClickableMenu?.GetType().Name,event_up=Game1.eventUp,minigame=Game1.currentMinigame?.GetType().Name,player_action=playerExecutor.Current};
    private object AutoplayDiagnostics()=>new{state=Data.Autoplay,snapshot=AgentSnapshot(),pending=agentPending!=null,last_model_ms=agentLastLatency,waiting=agentWaiting,
        clock_rate=Settings.AutoplayClockRate,decision_delay_ms=Settings.AutoplayDecisionDelayMs,source="DeepSeek; no local planning fallback"};
    private void TickAutoplay() {
        playerExecutor.Tick();
        if(!AutoplayRunning)return;
        if(Context.IsMultiplayer){PauseAutoplay("multiplayer_not_supported");return;}
        foreach(var id in agentWaiting.ToArray()) {
            JsonElement result;
            try {result=id.StartsWith("player:")?JsonSerializer.SerializeToElement(playerExecutor.Poll(id)):JsonDocument.Parse(api!.PollAction(id)).RootElement.Clone();}
            catch {result=JsonSerializer.SerializeToElement(new{status="failed",error="receipt_unavailable"});}
            if(result.TryGetProperty("status",out var s) && s.GetString()=="running")continue;
            agentWaiting.Remove(id);if(s.GetString()=="succeeded")Data.Autoplay.VerifiedActions++;Data.Autoplay.Record("action_result",result.GetRawText());
        }
        if(agentPending!=null) {
            if(!agentPending.IsCompleted)return;
            var task=agentPending;agentPending=null;agentLastLatency=agentWatch.Elapsed.TotalMilliseconds;
            try {
                var reply=task.GetAwaiter().GetResult();Data.Tokens+=reply.Tokens;RecordUsage();
                var turn=AgentTurn.Parse(reply.Json);Data.Autoplay.Decisions++;Data.Autoplay.Plan=turn.plan;
                Data.Autoplay.Record("decision",reply.Json);
                if(turn.speech.Length>0)Say(Selected,turn.speech);
                bool playerUsed=false;
                foreach(var call in turn.calls) {
                    if(!AutoplayRunning)break;
                    object result;
                    try {
                        bool mutatesPlayer=AgentToolRegistry.IsPlayerMutation(call.tool);
                        if(mutatesPlayer && playerUsed)throw new InvalidOperationException("one_player_action_per_turn");
                        if(mutatesPlayer)playerUsed=true;
                        result=agentTools.Execute(call.tool,call.args);
                    }catch(Exception e){result=new{status="failed",error=e is InvalidOperationException?e.Message:"tool_exception_"+e.GetType().Name};}
                    var observed=JsonSerializer.SerializeToElement(result,AgentJson.Options);
                    Data.Autoplay.Record("tool_result",AgentJson.Encode(new{tool=call.tool,result=observed}));
                    if(observed.ValueKind==JsonValueKind.Object && observed.TryGetProperty("command_id",out var id) && observed.TryGetProperty("status",out var status) && status.GetString()=="running")agentWaiting.Add(id.GetString()!);
                }
                agentNext=DateTime.UtcNow.AddMilliseconds(AutoplaySpeed.DecisionDelay(Settings.AutoplayDecisionDelayMs));
                if(agentRequestedWait>agentNext)agentNext=agentRequestedWait;
            }catch(Exception e){PauseAutoplay("模型本轮未执行："+(e is InvalidOperationException?e.Message:e.GetType().Name));}
        }
        if(!AutoplayRunning || agentPending!=null || DateTime.UtcNow<agentNext || Thinking || Game1.fadeToBlack || Game1.eventUp || Game1.currentMinigame!=null)return;
        // Ordinary actions finish without a second paid polling call. Interactive night menus are exceptions.
        if(agentWaiting.Count>0 && !(playerExecutor.NeedsMenuChoice && Game1.activeClickableMenu!=null))return;
        if(playerExecutor.Busy && !playerExecutor.NeedsMenuChoice)return;
        EnsureBudget();
        if(Data.Calls>=Math.Clamp(Settings.AutoplayMaxCallsPerDay,1,2000)){PauseAutoplay("今日自主模型调用达到预算上限，进度已保留");return;}
        if(agentStarting){Data.Autoplay.Record("resume_observation",AgentJson.Encode(AgentSnapshot()));agentStarting=false;}
        string file=Path.IsPathRooted(Settings.ApiKeyFile)?Settings.ApiKeyFile:Path.Combine(Helper.DirectoryPath,Settings.ApiKeyFile);
        var context=new{run_id=Data.Autoplay.RunId,start_day=Data.Autoplay.StartDay,verified_actions=Data.Autoplay.VerifiedActions,goal=Data.Autoplay.Goal,plan=Data.Autoplay.Plan,now=AgentSnapshot(),tools=AgentToolRegistry.Catalog,
            recent=Data.Autoplay.Journal.TakeLast(12),persona=Current.Profile,memories=Current.Memories.TakeLast(4)};
        string serialized=AgentJson.Encode(context); // No game objects are accessed by the HTTP task.
        Data.Calls++;RecordUsage();agentCancellation?.Dispose();agentCancellation=new();agentWatch.Restart();
        agentPending=AutoplayModel.Ask(file,Settings.Model,serialized,agentCancellation.Token);
    }
    internal object AgentWorld(){RefreshFacts(true);var world=World();return new{snapshot=AgentSnapshot(),farm=Facts,companions=world.TryGetProperty("actors",out var actors)?actors.EnumerateArray().Select(a=>new{id=a.GetProperty("id"),name=a.GetProperty("name"),location=a.GetProperty("location"),candidates=a.GetProperty("candidates").EnumerateArray().Take(25).ToArray()}).ToArray():null,goals=GoalContext()};}
    internal object AgentCompanion(JsonElement args) {
        if(api==null)throw new InvalidOperationException("companion_api_unavailable");
        var values=JsonSerializer.Deserialize<Dictionary<string,JsonElement>>(args.GetRawText())!;
        values["command_id"]=JsonSerializer.SerializeToElement(Guid.NewGuid().ToString("N"));
        // Existing Squad validation owns real targets, inventory and location checks.
        return JsonDocument.Parse(api.StartAction(JsonSerializer.Serialize(values))).RootElement.Clone();
    }
    internal object AgentReceipt(string id,bool cancel)=>id.StartsWith("player:")?(cancel?playerExecutor.Cancel(id):playerExecutor.Poll(id)):
        JsonDocument.Parse(cancel?api!.CancelAction(id):api!.PollAction(id)).RootElement.Clone();
    internal void AgentWait(int seconds)=>agentRequestedWait=DateTime.UtcNow.AddSeconds(Math.Clamp(seconds,1,60));
}
