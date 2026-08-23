using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.GameData.Locations;
using StardewValley.Tools;

namespace Together;

// Deterministic eligibility only. Never call getFish/CheckGenericFishRequirements:
// those sample the live RNG and may evaluate catch side effects.
internal static class FishingRules {
    internal sealed record Site(Point Stand,int Direction,float Power,Point Bobber,int Depth);
    internal static int Caught(string item)=>item.Length==0?Game1.player.fishCaught.Pairs.Sum(p=>p.Value.Length>0?p.Value[0]:0):Game1.player.fishCaught.TryGetValue(item,out var counts)&&counts.Length>0?counts[0]:0;
    internal static IEnumerable<SpawnFishData> Spawns(GameLocation location,string item) => (location.GetData()?.Fish??new()).Concat(Game1.locationData.TryGetValue("Default",out var defaults)?defaults.Fish??new():new()).Where(s=>ItemRegistry.QualifyItemId(s.ItemId??"")==item);
    internal static string? Block(GameLocation l,FishingRod rod,string item,SpawnFishData rule,Site? site=null) {
        bool magic=rod.HasMagicBait();var p=Game1.player;
        if(!DataLoader.Fish(Game1.content).TryGetValue(item.StartsWith("(O)")?item[3..]:item,out var raw))return "not_rod_fish_data";
        var f=raw.Split('/');if(KnowledgeRules.Field(f,1)=="trap")return "crab_pot_required";
        if(rule.Season.HasValue&&!magic&&rule.Season.Value!=Game1.GetSeasonForLocation(l))return "season";
        if(rule.RequireMagicBait&&!magic)return "magic_bait";
        if(p.FishingLevel<rule.MinFishingLevel)return "level";
        if(rule.CatchLimit>=0&&Caught(item)>=rule.CatchLimit)return "native_catch_limit";
        if(rod.QualifiedItemId=="(T)TrainingRod"&&(rule.CanUseTrainingRod==false||rule.CanUseTrainingRod==null&&KnowledgeRules.Integer(KnowledgeRules.Field(f,1))>=50))return "training_rod";
        if(!rule.IgnoreFishDataRequirements) {
            if(!magic&&!KnowledgeRules.InTime(KnowledgeRules.Field(f,5),Game1.timeOfDay))return "time";
            string weather=KnowledgeRules.Field(f,7);if(!magic&&weather is "rainy" or "sunny"&&(weather=="rainy")!=l.IsRainingHere())return "weather";
            if(p.FishingLevel<KnowledgeRules.Integer(KnowledgeRules.Field(f,12)))return "fish_level";
        }
        if(!p.fishCaught.Any()&&KnowledgeRules.Field(f,13)!="true")return "first_catch_tutorial";
        if(!string.IsNullOrWhiteSpace(rule.Condition)) {
            // Chance-dependent/custom position queries are not a promise of eligibility.
            if(rule.Condition.Contains("RANDOM",StringComparison.OrdinalIgnoreCase))return "random_condition_requires_runtime";
            if(l!=Game1.currentLocation&&(rule.Condition.Contains("PLAYER_LOCATION",StringComparison.Ordinal)||rule.Condition.Contains("PLAYER_TILE",StringComparison.Ordinal)))return "position_condition_requires_arrival";
            if(!GameStateQuery.CheckConditions(rule.Condition,l,p,random:new Random(0),ignoreQueryKeys:magic?GameStateQuery.MagicBaitIgnoreQueryKeys:null))return "native_unlock_condition";
        }
        if(site!=null) {
            if(rule.PlayerPosition is {} playerArea&&!playerArea.Contains(site.Stand))return "stand_area";
            if(rule.BobberPosition is {} bobberArea&&!bobberArea.Contains(site.Bobber))return "bobber_area";
            if(site.Depth<rule.MinDistanceFromShore||rule.MaxDistanceFromShore>=0&&site.Depth>rule.MaxDistanceFromShore)return "depth";
            if(rule.FishAreaId!=null&&(!l.TryGetFishAreaForTile(site.Bobber.ToVector2(),out var area,out _)||area!=rule.FishAreaId))return "fish_area";
        }
        return null;
    }
    internal static bool Eligible(GameLocation l,FishingRod rod,string item,Site? site=null)=>item.Length==0||Spawns(l,item).Any(r=>Block(l,rod,item,r,site)==null);
    internal static Point BobberAt(Vector2 standingPixel,int direction,float power,int level) {
        int added=level>=15?4:level>=8?3:level>=4?2:level>=1?1:0;
        float distance=Math.Max(128,power*(added+(direction%2==1?4:3))*64)-(direction%2==1?8:0);
        var delta=direction switch{0=>new Vector2(0,-distance),1=>new Vector2(distance,0),2=>new Vector2(0,distance),_=>new Vector2(-distance,0)};
        return ((standingPixel+delta)/64).ToPoint();
    }
    internal static IEnumerable<Site> Sites(GameLocation l,FishingRod rod,string item) {
        if(!Eligible(l,rod,item))yield break;
        var layer=l.Map.Layers[0];var p=Game1.player;
        for(int y=1;y<layer.LayerHeight-1;y++)for(int x=1;x<layer.LayerWidth-1;x++) {
            var at=new Point(x,y);if(l.isWaterTile(x,y)||!PlayerExecutor.Passable(l,at))continue;
            for(int dir=0;dir<4;dir++)foreach(float power in new[]{.98f,.8f,.55f,.35f}) {
                var bobber=BobberAt(at.ToVector2()*64+new Vector2(32),dir,power,p.FishingLevel);
                if(bobber.X<0||bobber.Y<0||bobber.X>=layer.LayerWidth||bobber.Y>=layer.LayerHeight||!l.isTileFishable(bobber.X,bobber.Y))continue;
                int depth=FishingRod.distanceToLand(bobber.X,bobber.Y,l);var site=new Site(at,dir,power,bobber,depth);
                if(!Eligible(l,rod,item,site))continue;
                // Release is sampled once per native update. Require both ends of
                // the small cast-power interval to stay in the same eligible area.
                var late=BobberAt(at.ToVector2()*64+new Vector2(32),dir,Math.Min(1,power+.025f),p.FishingLevel);
                if(late.X<0||late.Y<0||late.X>=layer.LayerWidth||late.Y>=layer.LayerHeight||!l.isTileFishable(late.X,late.Y)||!Eligible(l,rod,item,site with{Bobber=late,Depth=FishingRod.distanceToLand(late.X,late.Y,l)}))continue;
                yield return site;
            }
        }
    }
}
