using StardewValley;
using StardewValley.GameData.Objects;
using StardewValley.Locations;
using StardewValley.Extensions;
using StardewValley.ItemTypeDefinitions;

namespace Together;

// Read-only predicates checked against Stardew Valley 1.6.15 Stats. Never call
// checkForAchievements/getAchievement here: observations must not award progress.
internal static class AchievementRules {
    public static object Native(int id) {
        var p=Game1.player;
        object Count(string metric,long value,long target,string[] skills,object? details=null)=>new{known=true,metric,current=value,target,remaining=Math.Max(0,target-value),condition_satisfied=value>=target,capabilities=skills,details,rule_version="Stardew Valley 1.6.15 Stats"};
        if(id is >=0 and <=4)return Count("totalMoneyEarned",p.totalMoneyEarned,new[]{15000L,50000,250000,1000000,10000000}[id],new[]{"F06","F11","F12"});
        if(id is 5 or 28)return Count("museum_donations",Game1.netWorldState.Value.MuseumPieces.Length,id==5?LibraryMuseum.totalArtifacts:40,new[]{"F04","F15","F17"});
        if(id is 18 or 19)return Count("houseUpgradeLevel",p.HouseUpgradeLevel,id==18?1:2,new[]{"F04","F12","F13"});
        if(id is 29 or 30)return Count("questsCompleted",p.stats.QuestsCompleted,id==29?10:40,new[]{"F16"});
        if(id is 6 or 7 or 9 or 11 or 12 or 13) {
            int points=id is 7 or 9?2500:1250,target=id switch{9=>8,11=>4,12=>10,13=>20,_=>1};
            return Count("friendships_with_points",p.friendshipData.Values.Count(f=>f.Points>=points),target,new[]{"F18"},new{minimum_points=points,people=p.friendshipData.Pairs.Select(f=>new{name=f.Key,points=f.Value.Points})});
        }
        if(id is 20 or 21 or 22) {
            var recipes=CraftingRecipe.craftingRecipes.Keys.Where(k=>k!="Wedding Ring").ToArray();
            var missing=recipes.Where(k=>!p.craftingRecipes.TryGetValue(k,out int n)||n<=0).ToArray();
            return Count("distinct_crafted_recipes",recipes.Length-missing.Length,id==20?15:id==21?30:CraftingRecipe.craftingRecipes.Count-1,new[]{"F08","F10"},new{missing=missing.Select(k=>"craft:"+k)});
        }
        if(id is 15 or 16 or 17) {
            var recipes=CraftingRecipe.cookingRecipes;
            var missing=recipes.Where(r=>!p.cookingRecipes.ContainsKey(r.Key)||!p.recipesCooked.ContainsKey(r.Value.Split('/')[2].Split(' ',StringSplitOptions.RemoveEmptyEntries)[0])).Select(r=>r.Key).ToArray();
            return Count("distinct_cooked_recipes",recipes.Count-missing.Length,id==15?10:id==16?25:recipes.Count,new[]{"F06","F10","F18"},new{missing=missing.Select(k=>"cook:"+k)});
        }
        if(id is 24 or 25 or 26 or 27) {
            var fish=ItemRegistry.GetObjectTypeDefinition().GetAllData().Where(d=>d.ObjectType=="Fish"&&d.RawData is not ObjectData{ExcludeFromFishingCollection:not false}).ToArray();
            var missing=fish.Where(f=>!p.fishCaught.ContainsKey(f.QualifiedItemId)).Select(f=>f.QualifiedItemId).ToArray();
            int catches=fish.Sum(f=>p.fishCaught.TryGetValue(f.QualifiedItemId,out var counts)&&counts.Length>0?counts[0]:0);
            return Count(id==27?"fish_caught":"distinct_fish",id==27?catches:fish.Length-missing.Length,id switch{24=>10,25=>24,26=>fish.Length,_=>100},new[]{"F14"},new{missing,ownership="Farmer fishCaught; NPC cargo is insufficient"});
        }
        if(id is 31 or 32) {
            var crops=Game1.cropData.Values.Where(c=>id==31?c.CountForPolyculture:c.CountForMonoculture).DistinctBy(c=>c.HarvestItemId).ToArray();
            var progress=crops.Select(c=>new{item="(O)"+c.HarvestItemId,shipped=p.basicShipped.GetValueOrDefault(c.HarvestItemId),required=id==31?15:300}).ToArray();
            return Count(id==31?"crop_types_shipped_15":"crop_types_shipped_300",progress.Count(c=>c.shipped>=c.required),id==31?progress.Length:1,new[]{"F05","F06","F12"},progress);
        }
        if(id==34)return Count("all_shipping_collection",Utility.hasFarmerShippedAllItems()?1:0,1,new[]{"F06","F11","F12"});
        if(id==42)return Count("infinity_weapon_in_inventory",p.Items.Any(i=>i?.QualifiedItemId is "(W)62" or "(W)63" or "(W)64")?1:0,1,new[]{"F15","F19"});
        if(id==35) {
            string[] books={"Book_Trash","Book_Crabbing","Book_Bombs","Book_Roe","Book_WildSeeds","Book_Woodcutting","Book_Defense","Book_Friendship","Book_Void","Book_Speed","Book_Marlon","Book_PriceCatalogue","Book_Diamonds","Book_Mystery","Book_AnimalCatalogue","Book_Speed2","Book_Artifact","Book_Horse","Book_Grass"};
            return Count("power_books_read",books.Count(b=>p.stats.Get(b)>0),books.Length,new[]{"F12","F19"},new{missing=books.Where(b=>p.stats.Get(b)==0)});
        }
        return new{known=false,gap="special_achievement_predicate_not_adapted",native_id=id};
    }
    public static IEnumerable<NativeGoalDefinition> PlatformConditions() {
        var p=Game1.player;
        NativeGoalDefinition Rule(string id,bool met,string[] capabilities,object evidence)=>new("platform-condition:"+id,id,"platform-condition",met,capabilities,Array.Empty<string>(),new{native_condition_satisfied=met,evidence},"原生存档条件；平台实际解锁需另验","执行覆盖由 capabilities 对应缺口决定；不能将条件满足当平台已解锁");
        yield return Rule("Achievement_LocalLegend",p.eventsSeen.Contains("191393"),new[]{"F17","F19"},new{event_id="191393"});
        yield return Rule("Achievement_Joja",p.eventsSeen.Contains("502261"),new[]{"F12","F17","F19"},new{event_id="502261"});
        yield return Rule("Achievement_PrairieKing",p.stats.Get("completedPrairieKing")>0,new[]{"F21"},new{stat=p.stats.Get("completedPrairieKing")});
        yield return Rule("Achievement_FectorsChallenge",p.stats.Get("completedPrairieKingWithoutDying")>0,new[]{"F21"},new{stat=p.stats.Get("completedPrairieKingWithoutDying")});
        yield return Rule("Achievement_FullHouse",p.isMarriedOrRoommates()&&p.getChildrenCount()>=2,new[]{"F18"},new{married=p.isMarriedOrRoommates(),children=p.getChildrenCount()});
        yield return Rule("Achievement_TheBottom",p.deepestMineLevel>=120,new[]{"F15"},new{p.deepestMineLevel});
        yield return Rule("Achievement_KeeperOfTheMysticRings",AdventureGuild.areAllMonsterSlayerQuestsComplete(),new[]{"F15"},new{p.hasCompletedAllMonsterSlayerQuests.Value});
        int[] levels={p.farmingLevel.Value,p.fishingLevel.Value,p.miningLevel.Value,p.foragingLevel.Value,p.combatLevel.Value};
        yield return Rule("Achievement_SingularTalent",levels.Any(l=>l>=10),new[]{"F06","F14","F15"},levels);
        yield return Rule("Achievement_MasterOfTheFiveWays",levels.All(l=>l>=10),new[]{"F06","F14","F15"},levels);
        yield return Rule("Achievement_Stardrop",Utility.foundAllStardrops(),new[]{"F17","F18","F19"},new{source="Utility.foundAllStardrops"});
    }
}
