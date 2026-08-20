using System.Text.Json;
using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    internal object ConfigureDailyRoutine(JsonElement args) {
        var policy=Data.Autoplay.Routine;
        if(args.TryGetProperty("enabled",out var enabled)&&enabled.ValueKind is not (JsonValueKind.True or JsonValueKind.False))throw new InvalidOperationException("routine_enabled_must_be_boolean");
        if(args.TryGetProperty("assignments",out var raw)) {
            if(raw.ValueKind!=JsonValueKind.Object)throw new InvalidOperationException("routine_assignments_required");
            var assignments=JsonSerializer.Deserialize<Dictionary<string,string>>(raw.GetRawText())!;
            if(assignments.Count>10)throw new InvalidOperationException("routine_size_limit");
            var actors=World().GetProperty("actors").EnumerateArray().Select(a=>a.GetProperty("id").GetString()).Append("player").ToHashSet();
            foreach(var pair in assignments) {
                if(pair.Key is not ("mail" or "cooking_tv" or "water" or "harvest" or "pet" or "feed" or "milk" or "shear" or "animal_collect")||!actors.Contains(pair.Value)||pair.Value!="player"&&pair.Key is "mail" or "cooking_tv" or "milk" or "shear" or "animal_collect")throw new InvalidOperationException("unsupported_routine_assignment");
            }
            policy.Assignments=assignments;policy.Version++;policy.SubmittedDay=-1;
        }
        if(enabled.ValueKind is JsonValueKind.True or JsonValueKind.False)policy.Enabled=enabled.GetBoolean();
        policy.LastError="";
        return new{policy,note="持续政策在每个游戏日按原生缺项生成一次队列；已排队/执行的任务仍需plan.cancel单独取消。普通子动作不请求模型。"};
    }
    private void TickDailyAutomation() {
        var policy=Data.Autoplay.Routine;
        if(!AutoplayRunning||!policy.Enabled||policy.SubmittedDay==Game1.Date.TotalDays||Game1.timeOfDay>=1800||Game1.eventUp||Game1.fadeToBlack||Game1.activeClickableMenu!=null)return;
        if(Data.Autoplay.Schedule.Tasks.Any(t=>!t.Terminal&&policy.Assignments.Values.Contains(t.spec.actor)))return;
        policy.SubmittedDay=Game1.Date.TotalDays;
        try {
            RefreshFacts(true);var tasks=new List<AgentTaskSpec>();var skips=new List<object>();
            var actors=World().GetProperty("actors").EnumerateArray().Select(a=>a.GetProperty("id").GetString()).Append("player").ToHashSet();
            foreach(string goal in new[]{"cooking_tv","mail","harvest","water","feed","pet","animal_collect","milk","shear"}) {
                if(!policy.Assignments.TryGetValue(goal,out string? actor))continue;
                if(!actors.Contains(actor)){skips.Add(new{goal,reason="assigned_companion_not_available"});continue;}
                if(goal is "mail" or "cooking_tv") {
                    bool available=goal=="mail"?Game1.mailbox.Count>0:Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)=="Sun"||Game1.shortDayNameFromDayOfSeason(Game1.dayOfMonth)=="Wed"&&Game1.stats.DaysPlayed>7;
                    if(available)tasks.Add(new(){id=$"routine-{Game1.Date.TotalDays}-{policy.Version}-{goal}",actor="player",tool=goal=="mail"?"player.read_mail":"player.watch_tv",args=JsonSerializer.SerializeToElement(new{channel="cooking"}),purpose="按持续政策阅读今日邮件/烹饪节目",day=Game1.Date.TotalDays,deadline=1800});
                    continue;
                }
                bool needed=goal switch{"harvest"=>Facts.RipeCrops>0,"water"=>Facts.DryCrops>0,"pet"=>Facts.AnimalsUnpetted>0,"feed"=>Facts.FeedNeeded>0,_=>Game1.getFarm().getAllFarmAnimals().Any()};
                if(!needed)continue;
                if(actor=="player"&&(goal=="milk"&&!Game1.player.Items.Any(i=>i is StardewValley.Tools.MilkPail)||goal=="shear"&&!Game1.player.Items.Any(i=>i is StardewValley.Tools.Shears))){skips.Add(new{goal,reason="animal_care_tool_missing"});continue;}
                tasks.Add(new(){id=$"routine-{Game1.Date.TotalDays}-{policy.Version}-{goal}",actor=actor,tool="work.run",args=JsonSerializer.SerializeToElement(new{actor_id=actor,goal,location="Farm",count=0,until=1800}),purpose="按持续分工完成今日"+goal,day=Game1.Date.TotalDays,deadline=1800});
            }
            if(tasks.Count>0) {
                if(Data.Autoplay.Schedule.Tasks.Count+tasks.Count>180)Data.Autoplay.Schedule.Archive();
                Data.Autoplay.Schedule.Submit($"routine-{Game1.Date.TotalDays}-{policy.Version}",Data.Autoplay.Schedule.Revision,tasks,Game1.Date.TotalDays);
            }
            Data.Autoplay.Record("daily_routine",AgentJson.Encode(new{day=Game1.Date.TotalDays,tasks,skips}));
            if(skips.Count>0)WakeAgent("daily_routine_missing_conditions");
        }catch(Exception e){policy.LastError=e is InvalidOperationException?e.Message:e.GetType().Name;Data.Autoplay.Record("daily_routine_blocked",policy.LastError);WakeAgent("daily_routine_blocked");}
    }
}
