using System.Text.Json;
using StardewValley;
using StardewValley.Tools;

namespace Together;
public sealed partial class ModEntry {
    private IEnumerable<GameLocation> FishingLocations(string item,string requested="") {
        if(item.Length>0&&!DataLoader.Fish(Game1.content).ContainsKey(item.StartsWith("(O)")?item[3..]:item))yield break;
        var rod=Game1.player.Items.OfType<FishingRod>().OrderByDescending(r=>r.UpgradeLevel).FirstOrDefault();if(rod==null)yield break;
        var locations=new[]{Game1.currentLocation}.Concat(Game1.locations).Distinct();
        foreach(var l in locations.OrderBy(l=>l.IsFarm?2:l==Game1.currentLocation?0:1)) {
            if(requested.Length>0&&l.NameOrUniqueName!=requested||!l.canFishHere())continue;
            if(l!=Game1.currentLocation&&Knowledge.Index.Get("location:"+l.Name) is {} entry&&!Knowledge.Visible(entry))continue;
            if(l!=Game1.currentLocation&&PlayerExecutor.NextExit(Game1.currentLocation,l.NameOrUniqueName)==null)continue;
            if(FishingRules.Eligible(l,rod,item))yield return l;
        }
    }
    internal object ReadFishingOptions(JsonElement args) {
        string item=AgentToolRegistry.Text(args,"item"),location=AgentToolRegistry.Text(args,"location");
        var rod=Game1.player.Items.OfType<FishingRod>().OrderByDescending(r=>r.UpgradeLevel).FirstOrDefault();
        if(rod==null)return new{available=false,reason="fishing_rod_missing"};
        var rows=(location.Length>0?new[]{PlayerExecutor.LoadedLocation(location)}:Game1.locations.ToArray()).Where(l=>l!=null&&l.canFishHere()).Select(l=>new{
            location=l!.NameOrUniqueName,reachable=l==Game1.currentLocation||PlayerExecutor.NextExit(Game1.currentLocation,l.NameOrUniqueName)!=null,
            conditions=FishingRules.Spawns(l,item).Select(s=>new{rule=s.Id,blocked=FishingRules.Block(l,rod,item,s),s.FishAreaId,s.MinDistanceFromShore,s.MaxDistanceFromShore,s.PlayerPosition,s.BobberPosition}).ToArray()
        }).Where(r=>r.conditions.Length>0).Take(32).ToArray();
        return new{item,caught=FishingRules.Caught(item),rod=rod.QualifiedItemId,locations=rows,note="时段/季节/原生条件与水域限制；到达后检查真实可达抛竿位与落点。概率不模拟、不保证下一竿；动态鱼池和蟹笼单独适配。"};
    }
    private void TickFishingTrip(SemanticJob j) {
        j.gained=Math.Max(0,FishingRules.Caught(j.Item)-j.FishBaseline);
        if(j.gained>=j.requested){StopSemanticWork(j,"native_target_catches_verified",true);return;}
        if(Game1.timeOfDay>=j.Until||(DateTime.UtcNow-j.Started).TotalMinutes>=35||j.Attempts>=200){StopSemanticWork(j,"fishing_trip_time_or_attempt_budget");return;}
        if(j.Storing||!OperationsPolicy.CapacityReady(Game1.player.freeSpotsInInventory(),j.RequiredSlots)){j.Storing=true;TickWorkStorage(j);return;}
        if(Game1.player.Stamina<j.Reserve+10||Game1.player.health<35){if(TryWorkFood(j))return;StopSemanticWork(j,"fishing_trip_supply_reserve");return;}
        var location=FishingLocations(j.Item,j.FishLocation).FirstOrDefault(l=>!j.Excluded.Contains("fish:"+l.NameOrUniqueName));
        if(location==null){StopSemanticWork(j,"target_fish_no_current_reachable_conditions_or_sites");return;}
        j.location=location.NameOrUniqueName;
        if(location!=Game1.currentLocation){WorkChild(j,"player.travel",new{location=j.location},"fish_travel");return;}
        WorkChild(j,"player.fish",new{item=j.Item,count=1,reserve_stamina=j.Reserve},"fish_attempt",j.location);
    }
}
