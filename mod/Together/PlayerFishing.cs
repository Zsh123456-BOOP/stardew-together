using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Pathfinding;
using StardewValley.Tools;

namespace Together;
public sealed partial class PlayerExecutor {
    internal bool OwnsFishing=>Busy&&Current?.skill=="player.fish";
    private int fishRequested,fishBaseline,fishCastBaseline,fishDirection,fishCasts,fishReserve;
    private bool fishCastPending,fishLandingChecked;
    private string fishTarget="";
    internal float FishingCastPower {get;private set;}=.8f;
    private static int NativeFishCount()=>Game1.player.fishCaught.Pairs.Sum(p=>p.Value.Length>0?p.Value[0]:0);
    private void StartFishing(JsonElement args) {
        if(!Game1.currentLocation.canFishHere())throw new InvalidOperationException("location_not_fishable");
        fishTarget=AgentToolRegistry.Text(args,"item");
        fishRequested=AgentToolRegistry.Number(args,"count",3);fishReserve=AgentToolRegistry.Number(args,"reserve_stamina",20);
        if(fishRequested is <1 or >20||fishReserve is <15 or >270)throw new InvalidOperationException("invalid_fishing_limits");
        int slot=Enumerable.Range(0,Game1.player.Items.Count).Where(i=>Game1.player.Items[i] is FishingRod).OrderByDescending(i=>((FishingRod)Game1.player.Items[i]).UpgradeLevel).FirstOrDefault(-1);
        if(slot<0)throw new InvalidOperationException("fishing_rod_missing");
        Game1.player.CurrentToolIndex=slot;fishBaseline=fishCastBaseline=NativeFishCount();fishCasts=0;fishCastPending=fishLandingChecked=false;
        var l=Game1.currentLocation;var rod=(FishingRod)Game1.player.Items[slot];
        foreach(var site in FishingRules.Sites(l,rod,fishTarget).OrderByDescending(s=>s.Depth).ThenBy(s=>Vector2.DistanceSquared(s.Stand.ToVector2(),Game1.player.Tile)).Take(160)) {
            var route=PreviewPath(l,site.Stand);
            if(site.Stand!=Game1.player.TilePoint&&route?.Count is not >0)continue;
            fishDirection=site.Direction;FishingCastPower=site.Power;Walk(site.Stand);Current!.phase="fishing_walk";return;
        }
        throw new InvalidOperationException("no_reachable_native_cast_site");
    }
    private void TickFishing() {
        if(Game1.currentLocation.NameOrUniqueName!=origin)throw new InvalidOperationException("fishing_location_changed");
        if(Game1.player.CurrentTool is not FishingRod rod)throw new InvalidOperationException("fishing_rod_changed");
        if(Current!.phase=="fishing_walk") {
            if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Current.phase="fishing";
        }
        if(Game1.activeClickableMenu is BobberBar){Current.phase="fishing_minigame";return;}
        if(Game1.activeClickableMenu is ItemGrabMenu reward&&reward.context is FishingRod) {
            if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(180);
            Current.effects.Add(NativeRewards.Step(reward).Evidence);return;
        }
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("fishing_reward_menu_requires_collection");
        if(rod.isFishing&&!rod.castedButBobberStillInAir&&!fishLandingChecked) {
            var bobber=(rod.bobber.Value/64).ToPoint();
            var actual=new FishingRules.Site(Game1.player.TilePoint,fishDirection,FishingCastPower,bobber,rod.clearWaterDistance);
            if(!FishingRules.Eligible(Game1.currentLocation,rod,fishTarget,actual))throw new InvalidOperationException("target_fish_conditions_or_native_landing_changed");
            fishLandingChecked=true;Current.effects.Add(new{kind="native_bobber_landing",item=fishTarget,tile=bobber,depth=rod.clearWaterDistance});
        }
        if(rod.isNibbling&&!rod.hit) {rod.DoFunction(Game1.currentLocation,(int)rod.bobber.X,(int)rod.bobber.Y,1,Game1.player);return;}
        if(rod.fishCaught){rod.doneHoldingFish(Game1.player);return;}
        if(rod.isFishing||rod.isTimingCast||rod.isCasting||rod.isReeling||rod.pullingOutOfWater||rod.castedButBobberStillInAir||Game1.player.UsingTool||!Game1.player.CanMove)return;
        if(fishCastPending) {
            int count=NativeFishCount();Current.completed=Math.Max(0,count-fishBaseline);
            Current.effects.Add(new{kind="native_fishing_attempt",attempt=fishCasts,fish_before=fishCastBaseline,fish_after=count,recorded_catches=Math.Max(0,count-fishCastBaseline)});
            fishCastBaseline=count;fishCastPending=false;
        }
        if(Current.completed>=fishRequested){Finish("succeeded");return;}
        if(fishCasts>=Math.Max(10,fishRequested*4))throw new InvalidOperationException("fishing_attempt_budget_reached");
        if(Game1.player.Stamina<fishReserve+8||Game1.player.health<30||Game1.timeOfDay>=2200)throw new InvalidOperationException("fishing_resource_or_time_reserve_reached");
        if(Game1.player.Items.Count(i=>i==null)<2)throw new InvalidOperationException("fishing_inventory_space_required");
        if(!FishingRules.Eligible(Game1.currentLocation,rod,fishTarget))throw new InvalidOperationException("target_fish_window_closed");
        fishLandingChecked=false;Game1.player.faceDirection(fishDirection);Game1.player.BeginUsingTool();
        if(!Game1.player.UsingTool)throw new InvalidOperationException("native_cast_not_started");
        fishCasts++;fishCastPending=true;Current.phase="fishing_cast";
    }
    private void ReleaseFishing() {
        if(Game1.player.CurrentTool is not FishingRod rod)return;
        if(Game1.activeClickableMenu is BobberBar)Game1.exitActiveMenu();
        if(rod.fishCaught)rod.doneHoldingFish(Game1.player);
        else if(Game1.activeClickableMenu==null)rod.doneFishing(Game1.player);
    }
}
