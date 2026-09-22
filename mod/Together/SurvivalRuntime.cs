using System.Text.Json;
using StardewValley;
using StardewValley.Menus;

namespace Together;
public sealed partial class ModEntry {
    private DateTime survivalRetryAt;
    private string? survivalSleepId;
    private bool survivalResetPending;
    private int nightWarningDay=-1;
    private bool SurvivalOwnsDay=>Data.Autoplay.Survival.Mode!="model";
    private string ResumeConsentPath=>Path.Combine(Helper.DirectoryPath,"data",$"resume-{Game1.uniqueIDForThisGame}.json");
    private void SetResumeConsent(bool allowed) {
        Data.Autoplay.Survival.AutoResume=allowed;
        if(!StardewModdingAPI.Context.IsWorldReady)return;
        Directory.CreateDirectory(Path.GetDirectoryName(ResumeConsentPath)!);
        File.WriteAllText(ResumeConsentPath,AgentJson.Encode(new{allowed}));
    }
    private void SurvivalRecord(string kind,object value) {
        string json=AgentJson.Encode(value);Data.Autoplay.Record(kind,json);
    }
    private void EnterSurvival(string mode,string reason) {
        if(!AutoplayRunning)return;
        var s=Data.Autoplay.Survival;
        if(s.Mode=="sleep"||s.Mode==mode)return;
        s.Mode=mode;s.Reason=reason;s.Day=Game1.Date.TotalDays;
        var quality=Data.Autoplay.Quality.Current(s.Day,Game1.player.Money,NativeAssetValue());
        if(!quality.DegradationTime.HasValue){quality.DegradationTime=Game1.timeOfDay;quality.DegradationReason=reason;}
        agentGeneration++;agentCancellation?.Cancel();agentPending=null;
        survivalResetPending=true;survivalSleepId=null;survivalRetryAt=DateTime.MinValue;
        SurvivalRecord("survival_transition",new{mode,reason,day=s.Day,time=Game1.timeOfDay,snapshot=AgentSnapshot()});
    }
    internal object ModelRequestedStop(string reason) {
        EnterSurvival("sleep","model_requested_end_day:"+reason);
        return new{status="safe_end_day",reason,note="模型结束今日安排；程序负责原生返家过夜。玩家仍可手动暂停。"};
    }
    private DateTime modelRecoveryAt;
    private int schemaFailures;
    private string? decisionBlockedReason;
    private void ModelRecovered() {
        var s=Data.Autoplay.Survival;s.ModelFailures=0;schemaFailures=0;modelRecoveryAt=DateTime.MinValue;decisionBlockedReason=null;
        if(s.RoutineBeforeFallback!=null){Data.Autoplay.Routine=s.RoutineBeforeFallback;s.RoutineBeforeFallback=null;}
    }
    private void ModelUnavailable(Exception e,string? reply,bool applying) {
        string stage=ModelFailurePolicy.Classify(e,applying);
        SurvivalRecord("decision_failure",new{stage,error=e.Message,applying,reply});
        if(SurvivalState.Fatal(e.Message)){PauseAutoplay(stage+":"+e.Message);return;}
        if(stage=="local_context_pack"){decisionBlockedReason=stage+":"+e.Message;WakeAgent("decision_blocked_drain_native_work_then_hold_clock");return;}
        var s=Data.Autoplay.Survival;
        if(stage!="transport") {
            if(++schemaFailures>=3){decisionBlockedReason="decision_contract_failed_three_attempts:"+e.Message;return;}
            WakeAgent("decision_contract_error:"+e.Message);modelRecoveryAt=DateTime.UtcNow.AddSeconds(2);return;
        }
        s.ModelFailed();
        SurvivalRecord("model_unavailable",new{stage,error=e.Message,attempt=s.ModelFailures,retained_tasks=Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).Select(t=>t.spec.id)});
        if(s.ModelFailures>=6){decisionBlockedReason="transport_unavailable_after_bounded_retries";return;}
        if(s.ModelFailures>=3&&!Data.Autoplay.Routine.Enabled) {
            s.RoutineBeforeFallback??=Data.Autoplay.Routine;
            Data.Autoplay.Routine=new(){Enabled=true,Version=Data.Autoplay.Routine.Version+1,Assignments=new(){["harvest"]="player",["water"]="player",["feed"]="player",["pet"]="player",["animal_collect"]="player"}};
        }
        WakeAgent("transport_retry_preserve_work");modelRecoveryAt=DateTime.UtcNow.AddSeconds(Math.Min(60,2*Math.Pow(2,s.ModelFailures)));
    }
    private void SurvivalNewDay() {
        var s=Data.Autoplay.Survival;
        if(!s.NewDay(Game1.Date.TotalDays))return;
        if(s.RoutineBeforeFallback!=null){Data.Autoplay.Routine=s.RoutineBeforeFallback;s.RoutineBeforeFallback=null;}
        survivalResetPending=false;survivalSleepId=null;survivalRetryAt=DateTime.MinValue;agentFailures.Clear();ModelRecovered();
        if(AutoplayRunning)SurvivalRecord("survival_new_day",new{day=s.Day,s.LastSavedDay,Data.Autoplay.SleepDays,mode=s.Mode});
    }
    private bool TickSurvival() {
        if(!AutoplayRunning)return false;
        var s=Data.Autoplay.Survival;
        bool effective=OperationActorOccupied("player");
        if(s.Mode=="model"&&Game1.timeOfDay>=2200&&nightWarningDay!=Game1.Date.TotalDays) {
            nightWarningDay=Game1.Date.TotalDays;
            SurvivalRecord("night_warning",NightStatus());WakeAgent("night_warning_review_remaining_work_and_return");
        }
        if(s.Mode=="model"&&SurvivalState.NightGuard(Game1.timeOfDay,effective,ReturnReserve()))EnterSurvival("sleep","night_guard_no_effective_plan");
        if(s.Mode=="model")return false;
        if(survivalResetPending) {
            // Respect an in-flight tool impact or save; do not lose its native receipt.
            foreach(var j in semanticJobs.Values.Where(j=>j.status=="running").ToArray())SemanticReceipt(j.command_id,true);
            foreach(var t in Data.Autoplay.Schedule.Tasks.Where(t=>t.state=="running").ToArray()) {
                t.cancellation_requested_by="survival:"+s.Reason;
                var r=JsonSerializer.SerializeToElement(AgentReceipt(t.command_id!,true),AgentJson.Options);
                if(r.TryGetProperty("status",out var status)&&status.GetString()=="running")return true;
                CompleteScheduled(t,r);
            }
            if(playerExecutor.Busy){playerExecutor.Cancel();if(playerExecutor.Busy)return true;}
            Data.Autoplay.Schedule.CancelPending(Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).Select(t=>t.spec.id).ToArray(),"cancelled_by_survival:"+s.Reason);
            survivalResetPending=false;
            if(s.Mode=="routine") {
                // Reuse the existing maintenance executor, without purchases or new investment.
                s.RoutineBeforeFallback??=Data.Autoplay.Routine;
                Data.Autoplay.Routine=new(){Enabled=true,Version=Data.Autoplay.Routine.Version+1,Assignments=new(){["harvest"]="player",["water"]="player",["feed"]="player",["pet"]="player",["animal_collect"]="player"}};
            }
        }
        if(TickSurvivalMenus())return true;
        TickAgentSchedule();
        if(s.Mode=="routine") {
            TickDailyAutomation();
            if(Game1.timeOfDay<2200&&(playerExecutor.Busy||Data.Autoplay.Schedule.Tasks.Any(t=>!t.Terminal)))return true;
            EnterSurvival("sleep","fallback_routine_finished");return true;
        }
        if(survivalSleepId!=null) {
            var receipt=JsonSerializer.SerializeToElement(playerExecutor.Poll(survivalSleepId),AgentJson.Options);
            if(receipt.GetProperty("status").GetString()=="running")return true;
            SurvivalRecord("survival_sleep_result",receipt);survivalSleepId=null;
            if(receipt.TryGetProperty("error",out var error)&&error.ValueKind==JsonValueKind.String&&SurvivalState.Fatal(error.GetString()!)){PauseAutoplay(error.GetString()!);return true;}
            if(s.SleepAttempts>=3){PauseAutoplay("survival_return_failed_three_attempts");return true;}
            survivalRetryAt=DateTime.UtcNow.AddSeconds(2);
        }
        if(playerExecutor.Busy||Game1.fadeToBlack||Game1.eventUp||Game1.currentMinigame!=null||Game1.activeClickableMenu!=null||Game1.player.UsingTool||!Game1.player.CanMove||DateTime.UtcNow<survivalRetryAt)return true;
        try {
            // This bypasses the strategic 'more work is worthwhile' review only.
            // Start still checks native world/menu/movement and uses the real bed/save chain.
            var result=JsonSerializer.SerializeToElement(playerExecutor.Start("player.sleep",JsonSerializer.SerializeToElement(new{})),AgentJson.Options);
            survivalSleepId=result.GetProperty("command_id").GetString();s.SleepAttempts++;
            SurvivalRecord("survival_sleep_started",new{attempt=s.SleepAttempts,result});
        }catch(Exception e){survivalRetryAt=DateTime.UtcNow.AddSeconds(2);if(++s.SleepAttempts>=3)PauseAutoplay("survival_return_failed_three_attempts:"+e.Message);}
        return true;
    }
    private bool TickSurvivalMenus() {
        if(DateTime.UtcNow<survivalRetryAt||Game1.activeClickableMenu==null)return false;
        if(Game1.activeClickableMenu is LevelUpMenu chooser)return SurvivalProfession(chooser);
        // Decline optional questions instead of inventing purchases or story choices.
        if(Game1.activeClickableMenu is DialogueBox {isQuestion:true} dialogue) {
            var index=Array.FindIndex(dialogue.responses,r=>r.responseKey is "No" or "Cancel" or "Leave" or "Not");
            if(index>=0&&dialogue.responseCC!=null&&index<dialogue.responseCC.Count) {
                dialogue.finishTyping();NativeMenuInput.ClickMenu(dialogue,dialogue.responseCC[index].bounds,false);
                SurvivalRecord("survival_menu_declined",new{response=index});survivalRetryAt=DateTime.UtcNow.AddMilliseconds(500);return true;
            }
        }
        if(!Game1.eventUp&&Game1.activeClickableMenu is GameMenu or ShopMenu or QuestLog) {
            var menus=new NativeMenuTools();try{menus.Close();SurvivalRecord("survival_menu_closed",new{reason="return_home"});}catch(InvalidOperationException){}
            survivalRetryAt=DateTime.UtcNow.AddSeconds(1);return true;
        }
        return false;
    }
    private bool SurvivalProfession(LevelUpMenu menu) {
        if(!SurvivalOwnsDay||!menu.isActive||!menu.isProfessionChooser||!menu.CanReceiveInput()||!menu.readyToClose())return false;
        var state=NativeMenuInput.ProfessionState(menu);
        if(state.Choices.Count==0)return false;
        int expected=Data.Autoplay.ProfessionChoices.GetValueOrDefault(state.Skill+":"+state.Level,state.Choices[0]);
        int choice=state.Choices.IndexOf(expected);if(choice is not (0 or 1))choice=0;
        NativeMenuInput.ChooseProfession(menu,(choice==0?menu.leftProfession:menu.rightProfession).bounds);
        RememberProfession(state.Skill,state.Level,state.Choices[choice]);
        SurvivalRecord("survival_profession",new{state.Skill,state.Level,selected=state.Choices[choice],reason="saved_policy_or_first_native_option"});return true;
    }
    private void ResumeNativeCheckpoint() {
        var s=Data.Autoplay.Survival;
        if(!s.AutoResume||Data.Autoplay.Status!="running")return;
        bool allowed=false;
        try{allowed=File.Exists(ResumeConsentPath)&&JsonDocument.Parse(File.ReadAllText(ResumeConsentPath)).RootElement.GetProperty("allowed").GetBoolean();}catch{}
        if(!allowed){Data.Autoplay.Status="paused";s.AutoResume=false;Data.Autoplay.Detail="自动恢复授权已撤回或无法读取";return;}
        // Reload only intentions: interrupted coordinates/menus/receipts are never replayed.
        Data.Autoplay.Schedule.CancelPending(Data.Autoplay.Schedule.Tasks.Where(t=>!t.Terminal).Select(t=>t.spec.id).ToArray(),"cancelled_by_checkpoint_restore");
        Data.Autoplay.Schedule.Prepare=PrepareOperation;s.Resumes++;SurvivalNewDay();
        survivalResetPending=false;survivalSleepId=null;agentKnownActors.Clear();agentKnownActors.Add("player");
        foreach(var a in World().GetProperty("actors").EnumerateArray())agentKnownActors.Add(a.GetProperty("id").GetString()!);
        agentStarting=true;WakeAgent("native_checkpoint_restored_read_current_world");
        SurvivalRecord("native_checkpoint_resumed",new{s.Resumes,day=Game1.Date.TotalDays,Data.Autoplay.RunId,snapshot=AgentSnapshot()});
    }
}
