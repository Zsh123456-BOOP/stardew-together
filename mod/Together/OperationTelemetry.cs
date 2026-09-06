using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private DateTime operationTraceAt;
    private readonly Dictionary<string,(string Location,int X,int Y,string Phase,DateTime At)> operationPositions=new();
    private void TickOperationTelemetry() {
        if(!AutoplayRunning||DateTime.UtcNow<operationTraceAt)return;operationTraceAt=DateTime.UtcNow.AddMilliseconds(500);
        void Trace(string actor,Character character) {
            string location=character.currentLocation?.NameOrUniqueName??"unknown";var tile=character.TilePoint;
            var task=Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.actor==actor&&t.state=="running");
            var work=semanticJobs.Values.FirstOrDefault(j=>j.actor==actor&&j.status=="running");
            string phase=work?.phase??(actor=="player"&&playerExecutor.Busy?playerExecutor.Current?.phase:null)??(agentPending!=null?"waiting_model":OperationActorOccupied(actor)?"ready_to_dispatch":"idle_no_ready_work");
            bool had=operationPositions.TryGetValue(actor,out var old);
            int distance=had&&old.Location==location?Math.Abs(old.X-tile.X)+Math.Abs(old.Y-tile.Y):-1;
            if(had&&distance==0&&old.Phase==phase&&(DateTime.UtcNow-old.At).TotalSeconds<15)return;
            WriteBusinessLog("actor_route",AgentJson.Encode(new{actor,location,tile=new[]{tile.X,tile.Y},phase,task_id=task?.spec.id,intent_id=task?.spec.intent_id,purpose=task?.spec.purpose,
                observed_delta=distance,event_type=!had?"first":old.Location!=location?"map_transition":distance>2?"sample_gap":distance>0?"move":"waiting_or_animation",
                elapsed_since_sample=had?(double?)(DateTime.UtcNow-old.At).TotalSeconds:null,
                work=work==null?null:new{work.goal,work.completed,work.gained,work.deposited,work.requested,work.child_id},
                waiting=task==null?Data.Autoplay.Schedule.Tasks.Where(t=>t.spec.actor==actor&&t.state=="queued").Take(4).Select(t=>new{t.spec.id,t.wait_reason,t.spec.not_before,t.spec.after}):null}));
            operationPositions[actor]=(location,tile.X,tile.Y,phase,DateTime.UtcNow);
        }
        Trace("player",Game1.player);
        if(Data.Partner.Enabled&&Game1.getCharacterFromName(PartnerName) is {} partner) {
            string actor=agentKnownActors.FirstOrDefault(a=>a.EndsWith(":"+PartnerName))??"npc:"+PartnerName;
            Trace(actor,partner);
        }
    }
}
