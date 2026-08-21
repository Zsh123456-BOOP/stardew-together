using System.Text.Json;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Minigames;

namespace Together;
public sealed partial class PlayerExecutor {
    private string arcadeGame="",arcadeMode="",arcadeStopReason="";
    private int arcadeSeconds,arcadeAttempts,arcadeMaxAttempts,arcadeFrame,arcadeTargetScore,arcadeSubmittedScore;
    private bool arcadeEntered,arcadeWasOver;
    private Point? arcadeCabinet;
    private uint arcadeWinsBefore,arcadePerfectBefore;
    private DateTime arcadePlayingSince;
    internal bool OwnsArcade=>Busy&&Current?.skill=="player.arcade";
    internal static object ReadArcade()=>new {
        native_game=Game1.currentMinigame?.GetType().Name,
        prairie_wins=Game1.player.stats.Get("completedPrairieKing"),
        prairie_deathless=Game1.player.stats.Get("completedPrairieKingWithoutDying"),
        kart_wins=Game1.player.stats.Get("completedJunimoKart"),
        observation=ArcadeInput.ReadState(),
        note="控制器通过原生输入游玩；通关和无伤分别核验原生统计，不承诺启发式必胜。"
    };
    private void StartArcade(JsonElement args) {
        arcadeGame=AgentToolRegistry.Text(args,"game","prairie");arcadeMode=AgentToolRegistry.Text(args,"mode",arcadeGame=="prairie"?"continue":"progress");
        arcadeSeconds=AgentToolRegistry.Number(args,"seconds",1800);arcadeMaxAttempts=AgentToolRegistry.Number(args,"attempts",3);arcadeTargetScore=AgentToolRegistry.Number(args,"target_score",50000);arcadeSubmittedScore=0;
        if(arcadeGame is not ("prairie" or "kart")||arcadeSeconds is <60 or >7200||arcadeMaxAttempts is <1 or >10||arcadeGame=="prairie"&&arcadeMode is not ("continue" or "new" or "deathless")||arcadeGame=="kart"&&arcadeMode is not ("progress" or "endless"))throw new InvalidOperationException("invalid_arcade_policy");
        if(arcadeTargetScore is <1 or >1000000)throw new InvalidOperationException("invalid_arcade_score_target");
        arcadeEntered=arcadeWasOver=false;arcadeAttempts=1;arcadeFrame=0;arcadeCabinet=null;arcadeStopReason="";ArcadeInput.ResetPolicy();
        arcadeWinsBefore=Game1.player.stats.Get(arcadeGame=="prairie"?"completedPrairieKing":"completedJunimoKart");arcadePerfectBefore=Game1.player.stats.Get("completedPrairieKingWithoutDying");
        destination="Saloon";Current!.phase="arcade_travel";
    }
    private bool ArcadeWon=>arcadeMode=="endless"?arcadeSubmittedScore>=arcadeTargetScore:arcadeMode=="deathless"?Game1.player.stats.Get("completedPrairieKingWithoutDying")>arcadePerfectBefore:Game1.player.stats.Get(arcadeGame=="prairie"?"completedPrairieKing":"completedJunimoKart")>arcadeWinsBefore;
    internal void ObserveArcadeScore(int score){if(OwnsArcade&&arcadeGame=="kart"&&arcadeMode=="endless"){arcadeSubmittedScore=Math.Max(arcadeSubmittedScore,score);Current!.effects.Add(new{kind="native_kart_score_submitted",score,target=arcadeTargetScore});}}
    internal void ArcadeInputFault(string reason) {arcadeStopReason="arcade_controller_"+reason;}
    private void TickArcade() {
        if(Game1.currentMinigame is {} mini) {
            if(arcadeGame=="prairie"&&mini is not AbigailGame||arcadeGame=="kart"&&mini is not MineCart)throw new InvalidOperationException("different_minigame_interrupted_arcade");
            if(!arcadeEntered){arcadeEntered=true;arcadePlayingSince=DateTime.UtcNow;Current!.phase="arcade_playing";}
            if((DateTime.UtcNow-arcadePlayingSince).TotalSeconds>arcadeSeconds)arcadeStopReason="arcade_time_budget_reached";
            // MineCart handles Escape in receiveKeyPress, not UpdateInput.
            if(mini is MineCart cart&&(ArcadeWon||arcadeStopReason!=""))cart.receiveKeyPress(Keys.Escape);
            return;
        }
        if(arcadeEntered) {
            Current!.effects.Add(new{kind="native_arcade_result",game=arcadeGame,mode=arcadeMode,attempts=arcadeAttempts,won=ArcadeWon,wins_before=arcadeWinsBefore,wins_after=Game1.player.stats.Get(arcadeGame=="prairie"?"completedPrairieKing":"completedJunimoKart"),deathless_before=arcadePerfectBefore,deathless_after=Game1.player.stats.Get("completedPrairieKingWithoutDying"),reason=arcadeStopReason});
            Current.completed=ArcadeWon?1:0;Finish(ArcadeWon?"succeeded":"failed",ArcadeWon?null:arcadeStopReason!=""?arcadeStopReason:"arcade_ended_without_native_completion");return;
        }
        if(Game1.eventUp)throw new InvalidOperationException("arcade_travel_event_interrupted");
        if(Game1.locationRequest!=null||Game1.fadeToBlack)return;
        if(Game1.activeClickableMenu is DialogueBox dialogue) {
            if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(300);dialogue.finishTyping();
            if(!dialogue.isQuestion)throw new InvalidOperationException("arcade_unavailable_read_native_dialogue");
            string expected=arcadeGame=="prairie"?"CowboyGame":"MinecartGame";
            if(Game1.currentLocation.lastQuestionKey!=expected)throw new InvalidOperationException("arcade_question_changed");
            string response=arcadeGame=="prairie"?(arcadeMode=="continue"?"Continue":"NewGame"):(arcadeMode=="progress"?"Progress":"Endless");
            int index=Array.FindIndex(dialogue.responses,r=>r.responseKey==response);
            if(index<0||dialogue.responseCC==null||index>=dialogue.responseCC.Count)throw new InvalidOperationException("native_arcade_mode_unavailable");
            var b=dialogue.responseCC[index].bounds;dialogue.performHoverAction(b.Center.X,b.Center.Y);dialogue.receiveLeftClick(b.Center.X,b.Center.Y);return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("arcade_menu_interrupted");
        if(!Game1.player.CanMove)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(arcadeCabinet==null) {
            var l=Game1.currentLocation;string action=arcadeGame=="prairie"?"Arcade_Prairie":"Arcade_Minecart";
            for(int y=0;y<l.Map.Layers[0].LayerHeight&&arcadeCabinet==null;y++)for(int x=0;x<l.Map.Layers[0].LayerWidth;x++)if(l.doesTileHaveProperty(x,y,"Action","Buildings")==action)try{var p=new Point(x,y);Walk(Approach(p,true));arcadeCabinet=p;break;}catch(InvalidOperationException){}
            if(arcadeCabinet==null)throw new InvalidOperationException("native_arcade_cabinet_unreachable");
        }
        if(Game1.player.TilePoint!=target){MonitorWalk();return;}StopWalk();Adjacent(arcadeCabinet.Value);Face(arcadeCabinet.Value);NativeMenuInput.InteractWorld(arcadeCabinet.Value);
        if(Game1.currentMinigame==null&&Game1.activeClickableMenu==null)throw new InvalidOperationException("native_arcade_did_not_open");
    }
    internal Keys[] ArcadeKeys(AbigailGame game) {
        arcadeFrame++;
        if(arcadeGame!="prairie"||AbigailGame.playingWithAbigail)return Array.Empty<Keys>();
        if(ArcadeWon||arcadeStopReason!="")return new[]{Keys.Escape};
        // The deathless objective cannot be satisfied by a run that already died.
        // Exit with real evidence instead of erasing native progress or pretending success.
        if(arcadeMode=="deathless"&&game.died){arcadeStopReason="deathless_attempt_died_replan_new_run";return new[]{Keys.Escape};}
        if(AbigailGame.gameOver) {
            if(!arcadeWasOver){arcadeWasOver=true;if(arcadeAttempts>=arcadeMaxAttempts){arcadeStopReason="arcade_attempt_budget_reached";return new[]{Keys.Escape};}arcadeAttempts++;}
            if(arcadeFrame%8!=0)return Array.Empty<Keys>();
            return game.gameOverOption==0?new[]{Keys.Enter}:new[]{ArcadeInput.PrairieMoveKey(game,0)};
        }
        arcadeWasOver=false;
        return ArcadeInput.PrairieKeys(game,arcadeFrame);
    }
    internal bool ArcadeJump(MineCart game) {
        arcadeFrame++;
        if(arcadeGame!="kart"||ArcadeWon||arcadeStopReason!="")return false;
        if(ArcadeInput.KartGameOver(game)&&!arcadeWasOver){arcadeWasOver=true;if(arcadeAttempts>=arcadeMaxAttempts)arcadeStopReason="arcade_attempt_budget_reached";else arcadeAttempts++;}
        if(!ArcadeInput.KartGameOver(game)&&game.gameState==MineCart.GameStates.Ingame)arcadeWasOver=false;
        return arcadeStopReason==""&&ArcadeInput.KartJump(game,arcadeFrame);
    }
}
