using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Objects;
using StardewValley.Tools;
namespace Together;

public sealed partial class ModEntry {
    // Only reachable through RunLabScenario's lab/world/AgentLab gates.
    private object MemoryRebuildFixture(string mode,JsonElement fixture) {
        if(mode=="read")return new{card=TaskCard(),memory=AgentMemoryContext(),professions=Game1.player.professions.ToArray(),menu=Game1.activeClickableMenu?.GetType().Name,world_ready=StardewModdingAPI.Context.IsWorldReady,Game1.fadeToBlack};
        if(mode=="store") {
            PauseAutoplay("lab_memory_storage_fixture");Game1.activeClickableMenu=null;
            var farm=Game1.getFarm();var tile=new Vector2(65,17);
            farm.objects.Remove(tile);farm.terrainFeatures.Remove(tile);farm.objects.Remove(new(65,18));farm.terrainFeatures.Remove(new(65,18));
            var chest=new Chest(true);chest.modData[WorkChestRole]="output";farm.objects[tile]=chest;
            Game1.player.Items.Clear();for(int i=0;i<Game1.player.MaxItems;i++)Game1.player.Items.Add(null);
            Game1.player.Items[0]=new FishingRod();Game1.player.Items[4]=ItemRegistry.Create("(O)RiverJelly",2);
            PlayerSelection.Set(Game1.player,0);Game1.warpFarmer("Farm",65,18,false);
            return new{ready=true};
        }
        if(mode=="night") {
            PauseAutoplay("lab_memory_night_fixture");Game1.activeClickableMenu=null;
            var house=Utility.getHomeOfFarmer(Game1.player);var spot=house.GetPlayerBedSpot();
            Game1.warpFarmer(house.NameOrUniqueName,spot.X,spot.Y,false);Game1.timeOfDay=2300;
            Game1.player.fishingLevel.Value=5;Game1.player.newLevels.Add(new Point(1,5));
            StartAutoplay("原生回归：以钓鱼出售收入为主，完成钓鱼职业选择并正常睡觉；不招募伙伴。");agentLabProbe=true;
            Data.Autoplay.TrialTargetDay=Game1.Date.TotalDays+1;Data.Autoplay.TrialTargetSleeps=Data.Autoplay.SleepDays+1;
            return new{ready=true,wait_for_native_warp=true};
        }
        if(mode=="reflection") {
            // Replay the native storage receipt collected by the test while autoplay
            // was paused. This tests summarization, not automatic receipt collection.
            var receipt=fixture.GetProperty("native_receipt");
            if(receipt.GetProperty("status").GetString()!="succeeded")throw new InvalidOperationException("successful_native_fixture_receipt_required");
            memoryArchive!.Append(Game1.Date.TotalDays-1,"player","action_result",receipt.GetRawText());
            StartAutoplay("回归检查昨日真实行动摘要，不执行新的经营动作。");agentLabProbe=true;
            reflectionRequestedDay=-1;TickMemoryReflection();
            PauseAutoplay("lab_reflection_wait_only");
            return new{requested=reflectionRequest!=null};
        }
        if(mode=="recovery") {
            PauseAutoplay("lab_recovery_policy");Game1.timeOfDay=1200;
            StartAutoplay("可恢复任务故障不停止整个经营；玩家主动暂停必须保留。");agentLabProbe=true;
            for(int i=0;i<3;i++)ObserveExecutionFailure("fixture-recovery:"+i,"player","capacity_all_candidates_infeasible","lab_policy_probe");
            bool continued=AutoplayRunning;PauseAutoplay("玩家暂停");
            ObserveExecutionFailure("fixture-after-user-pause","player","capacity_all_candidates_infeasible","lab_policy_probe");
            return new{continued,manual_pause_preserved=!AutoplayRunning,scope="injected failure receipt policy; storage separately tested natively"};
        }
        if(mode=="resume") {
            PauseAutoplay("玩家暂停");string goal=new string('长',150)+"保留原生七天测试目标";
            StartAutoplay(goal);agentLabProbe=true;Data.Autoplay.SleepDays=3;Data.Autoplay.TrialTargetDay=10;Data.Autoplay.TrialTargetSleeps=7;
            PauseAutoplay("玩家暂停");StartAutoplay(Data.Autoplay.Goal);agentLabProbe=true;
            return new{goal_preserved=Data.Autoplay.Goal==goal,Data.Autoplay.SleepDays,Data.Autoplay.TrialTargetDay,Data.Autoplay.TrialTargetSleeps};
        }
        throw new InvalidOperationException("unknown_memory_fixture");
    }
}
