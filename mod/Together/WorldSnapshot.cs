using System.Text.Json;
using StardewValley;
using StardewValley.TerrainFeatures;

namespace Together;
public sealed partial class ModEntry {
    internal object WithExecutionContract(object result)=>ExecutionContract.Receipt(result,agentSaveEpoch,Data.Autoplay.Schedule.Revision);
    internal object SnapshotStamp()=>new{save_id=Game1.uniqueIDForThisGame.ToString(),farmer_id=Game1.player.UniqueMultiplayerID.ToString(),epoch=agentSaveEpoch,day=Game1.Date.TotalDays,time=Game1.timeOfDay,plan_revision=Data.Autoplay.Schedule.Revision,event_version=Data.Autoplay.Schedule.EventVersion};
    internal object ScanMap(JsonElement args) {
        string actor=AgentToolRegistry.Text(args,"actor_id","player");var l=AgentMapOrigin(actor).Location;
        int offset=AgentToolRegistry.Number(args,"offset",0),limit=Math.Clamp(AgentToolRegistry.Number(args,"limit",60),1,120);
        if(offset<0)throw new InvalidOperationException("invalid_offset");
        var cells=l.objects.Keys.Concat(l.terrainFeatures.Keys).Distinct().OrderBy(v=>v.Y).ThenBy(v=>v.X).ToArray();
        var rows=cells.Skip(offset).Take(limit).Select(v=>{
            l.objects.TryGetValue(v,out var o);l.terrainFeatures.TryGetValue(v,out var f);var d=f as HoeDirt;
            return new{x=(int)v.X,y=(int)v.Y,item=AgentToolRegistry.ItemInfo(o),terrain=f?.GetType().Name,
                tree=f is Tree tree?new{growth=tree.growthStage.Value,stump=tree.stump.Value,tapped=tree.tapped.Value,health=tree.health.Value}:null,
                crop=d?.crop is {} c?new{seed=c.netSeedIndex.Value,harvest=c.indexOfHarvest.Value,dead=c.dead.Value,ready=d.readyForHarvest(),watered=d.state.Value==1,raised=c.raisedSeeds.Value}:null};
        }).ToArray();
        return new{stamp=SnapshotStamp(),actor_id=actor,location=l.NameOrUniqueName,total=cells.Length,offset,next_offset=offset+limit<cells.Length?(int?)(offset+limit):null,cells=rows,
            note="当前地图对象/地形全量分页；顺序可能随世界变化，行动前再次核验。空地/碰撞/站位由执行器判定，未枚举不代表不可达。"};
    }
}
