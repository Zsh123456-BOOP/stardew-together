using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Locations;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Together;
public sealed partial class PlayerExecutor {
    private sealed record NutSite(Point Tile,string Kind,string Key);
    private NutSite? nutSite;
    private int nutRequested,nutFoundBefore;
    private DateTime nutCollectDeadline;
    private readonly HashSet<string> nutVisited=new();
    private readonly Queue<Point> nutPickupRoute=new();
    private static IEnumerable<NutSite> NutSites(IslandLocation location) {
        foreach(var bush in location.largeTerrainFeatures.OfType<Bush>().Where(b=>b.size.Value==4&&b.readyForHarvest())) {
            var tile=bush.Tile.ToPoint();string key="Bush_"+location.Name+"_"+tile.X+"_"+tile.Y;
            if(!Game1.player.team.collectedNutTracker.Contains(key))yield return new(tile,"bush",key);
        }
        foreach(var tile in location.buriedNutPoints) {
            string key="Buried_"+location.Name+"_"+tile.X+"_"+tile.Y;
            if(!Game1.player.team.collectedNutTracker.Contains(key))yield return new(tile,"buried",key);
        }
    }
    internal static object ReadWalnuts()=>new{found=Game1.netWorldState.Value.GoldenWalnutsFound,available=Game1.netWorldState.Value.GoldenWalnuts,sites=Game1.locations.OfType<IslandLocation>().SelectMany(l=>NutSites(l).Select(s=>new{location=l.NameOrUniqueName,x=s.Tile.X,y=s.Tile.Y,s.Kind,s.Key})),limited_pools=Game1.player.team.limitedNutDrops.Pairs.Select(p=>new{id=p.Key,dropped=p.Value}),note="此处枚举加载地图原生核桃灌木/埋藏点，非全部130颗谜题。地图存在目标不代表当前路径可达。"};
    private void StartWalnuts(JsonElement args) {
        destination=AgentToolRegistry.Text(args,"location",origin);nutRequested=AgentToolRegistry.Number(args,"count",0);
        if(nutRequested is <0 or >30||Game1.getLocationFromName(destination) is not IslandLocation)throw new InvalidOperationException("observed_island_location_and_walnut_count_required");
        nutSite=null;nutVisited.Clear();nutPickupRoute.Clear();Current!.phase="walnuts_travel";
    }
    private void TickWalnuts() {
        if(Game1.activeClickableMenu!=null)throw new InvalidOperationException("walnut_collection_menu_interrupted");
        if(Game1.locationRequest!=null||Game1.fadeToBlack||!Game1.player.CanMove||Game1.player.UsingTool)return;
        if(Game1.currentLocation.NameOrUniqueName!=destination){Travel();return;}
        if(Game1.currentLocation is not IslandLocation location)throw new InvalidOperationException("walnut_location_changed");
        if(Current!.phase=="walnuts_action") {
            if(nutSite==null||!Game1.player.team.collectedNutTracker.Contains(nutSite.Key)) {
                if(DateTime.UtcNow<nutCollectDeadline)return;throw new InvalidOperationException("native_walnut_source_not_collected");
            }
            nutPickupRoute.Clear();
            for(int y=nutSite.Tile.Y-1;y<=nutSite.Tile.Y+2;y++)for(int x=nutSite.Tile.X-1;x<=nutSite.Tile.X+1;x++)if(Passable(location,new(x,y)))nutPickupRoute.Enqueue(new(x,y));
            Current.phase="walnuts_pickup";nutCollectDeadline=DateTime.UtcNow.AddSeconds(12);
        }
        if(Current.phase=="walnuts_pickup") {
            if(Game1.netWorldState.Value.GoldenWalnutsFound>nutFoundBefore) {
                StopWalk();Current.effects.Add(new{kind="native_walnut_collected",source=nutSite!.Key,location=destination,tile=nutSite.Tile,found_before=nutFoundBefore,found_after=Game1.netWorldState.Value.GoldenWalnutsFound});
                Current.completed++;nutSite=null;Current.phase="walnuts_next";return;
            }
            if(DateTime.UtcNow>nutCollectDeadline)throw new InvalidOperationException("walnut_dropped_but_not_picked_up");
            if(!AtWalkTarget){MonitorWalk();return;}
            StopWalk();while(nutPickupRoute.TryDequeue(out var next))try{Walk(next);return;}catch(InvalidOperationException){}
            return;
        }
        if(nutRequested>0&&Current.completed>=nutRequested){Finish("succeeded");return;}
        if(nutSite==null) {
            foreach(var site in NutSites(location).Where(s=>!nutVisited.Contains(s.Key)).OrderBy(s=>Vector2.DistanceSquared(s.Tile.ToVector2(),Game1.player.Tile))) {
                nutVisited.Add(site.Key);
                if(site.Kind=="buried"&&!Game1.player.Items.OfType<Hoe>().Any())continue;
                try{Walk(Approach(site.Tile,true));nutSite=site;break;}catch(InvalidOperationException){}
            }
            if(nutSite==null) {
                bool remaining=NutSites(location).Any();if(remaining||nutRequested>Current.completed)throw new InvalidOperationException("remaining_walnuts_unreachable_missing_hoe_or_need_other_source");
                Finish("succeeded");return;
            }
        }
        if(!AtWalkTarget){MonitorWalk();return;}StopWalk();Adjacent(nutSite.Tile);Face(nutSite.Tile);nutFoundBefore=Game1.netWorldState.Value.GoldenWalnutsFound;
        if(nutSite.Kind=="buried") {
            int slot=Enumerable.Range(0,Game1.player.Items.Count).FirstOrDefault(i=>Game1.player.Items[i] is Hoe,-1);
            if(slot<0||Game1.player.Stamina<12)throw new InvalidOperationException("walnut_dig_needs_hoe_and_stamina");
            PlayerSelection.Set(Game1.player,slot);Game1.player.netItemStowed.Value=false;Game1.player.lastClick=nutSite.Tile.ToVector2()*64+new Vector2(32);Game1.player.BeginUsingTool();
            if(!Game1.player.UsingTool)throw new InvalidOperationException("native_walnut_dig_did_not_start");
        }else {
            if(!NativeMenuInput.InteractWorld(nutSite.Tile))throw new InvalidOperationException("native_walnut_bush_interaction_failed");
        }
        Current.phase="walnuts_action";nutCollectDeadline=DateTime.UtcNow.AddSeconds(5);
    }
}
