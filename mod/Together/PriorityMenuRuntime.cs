using System.Text.Json;
using StardewValley;
using StardewValley.Menus;
namespace Together;

public sealed partial class ModEntry {
    private Task<ModelReply>? priorityMenuRequest;
    private IClickableMenu? priorityMenuOwner;
    private string priorityMenuRun="";
    private DateTime priorityMenuRetry;
    private int priorityMenuFailures;
    private readonly NativeMenuTools priorityMenus=new();

    // Night menus exist while IsWorldReady is false and fadeToBlack is true.
    // This lane accesses only the live menu/player; never builds a world snapshot.
    private bool TickPriorityMenu() {
        if(!AutoplayRunning||Game1.player==null)return false;
        var menu=Game1.activeClickableMenu;
        if(priorityMenuRequest is {IsCompleted:true}) {
            var request=priorityMenuRequest;priorityMenuRequest=null;
            try {
                var reply=request.GetAwaiter().GetResult();Data.Tokens+=reply.Tokens;RecordUsage();RecordAgentUsage(reply);
                if(menu!=priorityMenuOwner||priorityMenuRun!=Data.Autoplay.RunId){Data.Autoplay.Record("menu_decision_stale","菜单或运行已改变，未执行旧选择");return menu is LevelUpMenu;}
                var turn=AgentTurn.Parse(reply.Json);
                if(turn.calls.Count!=1||turn.calls[0].tool!="menu.choose")throw new InvalidOperationException("profession_requires_one_native_choice");
                priorityMenus.ProfessionSelected=RememberProfession;
                var result=priorityMenus.Choose(turn.calls[0].args);
                Data.Autoplay.Record("priority_menu_result",AgentJson.Encode(new{turn.plan,result}));priorityMenuFailures=0;
            }catch(Exception e){
                Data.Autoplay.Record("priority_menu_error",AgentJson.Encode(new{error=e.Message}));priorityMenuRetry=DateTime.UtcNow.AddSeconds(2);
                if(++priorityMenuFailures>=3)PauseAutoplay("native_menu_choice_failed_preserved:"+e.Message);
            }
            return true;
        }
        if(menu is not LevelUpMenu level)return false;
        if(!level.isActive||!level.CanReceiveInput())return true;
        if(!level.isProfessionChooser){level.okButtonClicked();Data.Autoplay.Record("native_notice","已确认原生无分支升级");return true;}
        if(priorityMenuRequest!=null||DateTime.UtcNow<priorityMenuRetry)return true;
        try {
            if(ApplyProfessionPolicy(level))return true;
            if(!level.readyToClose())return true;
            EnsureBudget();
            if(Data.Calls>=Math.Clamp(Settings.AutoplayMaxCallsPerDay,1,2000)){PauseAutoplay("menu_decision_daily_budget_reached");return true;}
            var ui=priorityMenus.Read();var state=NativeMenuInput.ProfessionState(level);
            var context=AgentJson.Encode(new{goal=Data.Autoplay.Goal,plan=Data.Autoplay.Plan,ui,profession_effects=state.Choices.Select(id=>new{profession_number=id,description=LevelUpMenu.getProfessionDescription(id)}),task_card=TaskCard(),instruction="只用ui.choices里的id点击；profession_number不是菜单id。依据当前长期目标选择，不要调用世界动作。"});
            string file=Path.IsPathRooted(Settings.ApiKeyFile)?Settings.ApiKeyFile:Path.Combine(Helper.DirectoryPath,Settings.ApiKeyFile);
            string model=Settings.Model;priorityMenuOwner=menu;priorityMenuRun=Data.Autoplay.RunId;
            string? trace=Settings.RecordModelTrace?Path.Combine(Helper.DirectoryPath,"logs",Game1.uniqueIDForThisGame.ToString(),agentSaveEpoch,"model-"+Data.Autoplay.RunId+".jsonl"):null;
            Data.Autoplay.Record("priority_menu_request",AgentJson.Encode(new{state.Skill,state.Level,world_ready=StardewModAPIReady(),Game1.fadeToBlack}));
            Data.Calls++;RecordUsage();
            priorityMenuRequest=Task.Run(()=>AutoplayModel.Ask(file,model,context,CancellationToken.None,trace,Settings.AutoplayInputTokenBudget,menuOnly:true));
        }catch(Exception e){priorityMenuRetry=DateTime.UtcNow.AddSeconds(2);Data.Autoplay.Record("priority_menu_error",AgentJson.Encode(new{error=e.Message}));if(++priorityMenuFailures>=3)PauseAutoplay("native_menu_setup_failed:"+e.Message);}
        return true;
    }
    private static bool StardewModAPIReady()=>StardewModdingAPI.Context.IsWorldReady;
}
