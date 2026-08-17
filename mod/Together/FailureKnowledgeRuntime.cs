using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private string FailureConditions(string actor) {
        try {
            var origin=AgentMapOrigin(actor);var l=origin.Location;var p=Game1.player;
            // Detached scalar evidence only. Changes invalidate an old failure;
            // nothing from this record overrides a current native precondition.
            return FailureKnowledge.Hash(AgentJson.Encode(new {
                day=Game1.Date.TotalDays,location=l.NameOrUniqueName,tile=origin.Tile,money=p.Money,health=p.health,stamina=(int)p.Stamina,
                menu=Game1.activeClickableMenu?.GetType().Name,
                bag=p.Items.Select(i=>i==null?null:new{id=i.QualifiedItemId,count=i.Stack,quality=i.Quality,water=i is StardewValley.Tools.WateringCan w?w.WaterLeft:-1}),
                objects=l.objects.Pairs.Select(o=>new{x=o.Key.X,y=o.Key.Y,item=o.Value.QualifiedItemId,ready=o.Value.readyForHarvest.Value,held=o.Value.heldObject.Value?.QualifiedItemId}),
                characters=l.characters.Select(n=>new{n.Name,tile=n.TilePoint}),
                quests=p.questLog.Select(q=>new{id=q.id.Value,done=q.completed.Value}),
                reservations=Data.Reservations.OrderBy(x=>x.Key)
            }));
        }catch{return "unavailable";}
    }
    private void CheckKnownFailure(ScheduledAgentTask task) {
        string conditions=FailureConditions(task.spec.actor);if(conditions=="unavailable")return;
        var old=Data.Autoplay.Failures.Block(FailureKnowledge.Key(task.spec.actor,task.spec.tool,task.spec.args.GetRawText()),conditions,Game1.Date.TotalDays,DailyBudget.Minutes(Game1.timeOfDay));
        if(old!=null)throw new InvalidOperationException("known_failure_conditions_unchanged:"+old.Reason+":evidence="+old.TaskEvidence);
    }
    private void LearnActionResult(ScheduledAgentTask task,string state,string? error) {
        string key=FailureKnowledge.Key(task.spec.actor,task.spec.tool,task.spec.args.GetRawText());
        if(state=="succeeded"){Data.Autoplay.Failures.Success(key);return;}
        if(state!="failed"||string.IsNullOrEmpty(error)||error.StartsWith("known_failure_conditions_unchanged"))return;
        string conditions=FailureConditions(task.spec.actor);if(conditions=="unavailable")return;
        Data.Autoplay.Failures.Record(key,task.spec.actor,task.spec.tool,error,conditions,task.spec.id,Game1.Date.TotalDays,DailyBudget.Minutes(Game1.timeOfDay));
        Data.Autoplay.Record("failure_experience",AgentJson.Encode(Data.Autoplay.Failures.Entries.Last()));
    }
}
