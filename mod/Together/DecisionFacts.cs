using System.Text.Json;
using StardewValley;
using StardewValley.Quests;
using StardewValley.TerrainFeatures;
namespace Together;
public sealed partial class ModEntry {
    private object WateringObservation(string name) {
        var l=PlayerExecutor.LoadedLocation(name);var crops=l?.terrainFeatures.Values.OfType<HoeDirt>().Where(d=>d.crop!=null&&!d.crop.dead.Value).ToArray()??Array.Empty<HoeDirt>();
        int dry=crops.Count(d=>d.needsWatering()&&d.state.Value!=1);
        return new{location=name,raining=l?.IsRainingHere(),crop_count=crops.Length,dry_crops=dry,needs_watering=dry>0,note=dry>0?"只浇原生状态仍干燥且需要水的作物":crops.Length>0?"作物已湿或无需水，今天此刻无需浇水":"此地图没有活作物，无需安排浇水"};
    }
    private object? WateringNoWork(JsonElement args) {
        string name=AgentToolRegistry.Text(args,"location","Farm");var l=PlayerExecutor.LoadedLocation(name);if(l==null)return null;
        var crops=l.terrainFeatures.Values.OfType<HoeDirt>().Where(d=>d.crop!=null&&!d.crop.dead.Value).ToArray();
        if(crops.Any(d=>d.needsWatering()&&d.state.Value!=1))return null;
        return new{status="succeeded",stop_reason=crops.Length>0?"crops_already_watered_or_no_water_needed":"no_crops_at_requested_location",disposition="already_satisfied",resume_policy="do_not_retry_until_world_condition_changes",completed=0,gained=0,executed=false,evidence=WateringObservation(name)};
    }
    internal object SocialAccess(string name) {
        var npc=Game1.getCharacterFromName(name);if(npc==null)return new{npc=name,available_now=false,reason="unknown_npc"};
        var reach=PlayerExecutor.SocialReach(npc);var window=ServiceWindow(reach.location);
        bool open=window.Reason=="available"&&Game1.timeOfDay>=window.Open&&Game1.timeOfDay<window.Close;
        return new{npc=name,available_now=open&&reach.reachable,entry=ServiceHours(reach.location),reach,note="零体力不等于零时间：approach_steps仅为目的地图步数，另计跨图运输；人物移动或门禁变化后重新核验。任务是否值得推进由模型选择。"};
    }
    internal object[] SocialAvailability()=>Game1.player.questLog.OfType<SocializeQuest>().Where(q=>!q.completed.Value).SelectMany(q=>q.whoToGreet).Distinct().Select(n=> {
        var npc=Game1.getCharacterFromName(n);string location=npc?.currentLocation?.NameOrUniqueName??"";var reach=npc==null?null:PlayerExecutor.SocialReach(npc);var window=ServiceWindow(location);
        return (object)new{npc=n,location,same_map=location==Game1.currentLocation.NameOrUniqueName,available_now=reach?.reachable==true&&window.Reason=="available"&&Game1.timeOfDay>=window.Open&&Game1.timeOfDay<window.Close,reason=reach?.reason,approach_steps=reach?.approach_steps,map_transitions=reach?.map_transitions,entry=ServiceHours(location),deadline="介绍任务无当天截止时间",access_details=new{tool="services.read",args=new{npc=n}},note="待认识不等于当前可接近；出发前工具核验站位，失败改派可执行工作或查询此人物条件"};
    }).ToArray();
    private object SocialAccessCondition(string name) {
        var npc=Game1.getCharacterFromName(name);var reach=npc==null?null:PlayerExecutor.SocialReach(npc);var w=ServiceWindow(npc?.currentLocation?.NameOrUniqueName??"");
        return new{npc=name,location=npc?.currentLocation?.NameOrUniqueName,tile=npc==null?null:new[]{npc.TilePoint.X,npc.TilePoint.Y},sleeping=npc?.isSleeping.Value,invisible=npc?.IsInvisible,reachable=reach?.reachable,doors=reach?.doors,w.Reason,open_now=w.Reason=="available"&&Game1.timeOfDay>=w.Open&&Game1.timeOfDay<w.Close,Game1.player.HasTownKey};
    }
    private object? SocialPreflight(JsonElement args) {
        string name=AgentToolRegistry.Text(args,"npc"),mode=AgentToolRegistry.Text(args,"mode","talk"),id=AgentToolRegistry.Text(args,"quest_id");
        if(SocialObservation.Satisfied(Game1.player,name,mode,id)!=null)return null;
        var npc=Game1.getCharacterFromName(name);if(npc==null)return null;
        var reach=PlayerExecutor.SocialReach(npc);if(reach.reachable)return null;
        return new{status="blocked",error="social_access_unavailable",stop_reason="social_access_unavailable",completed=0,executed=false,evidence=SocialAccess(name),resume_policy="recheck_when_npc_position_or_access_changes",next=new{tool="services.read",args=new{npc=name}},mechanism_reference=new{tool="knowledge.get",args=new{id="npc:"+name}},note="出发前已发现人物不可接近，尚未执行行程；等待人物或门禁变化，期间可以选择其他工作"};
    }
}
