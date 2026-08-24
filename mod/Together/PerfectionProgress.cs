using StardewValley;

namespace Together;
internal static class PerfectionProgress {
    internal sealed record Part(string Id,double Fraction,int Weight,string Evidence,string[] Goals);
    internal static Part[] Parts() {
        var p=Game1.player;
        return new[]{
            new Part("shipping",Utility.GetFarmCompletion(f=>Utility.getFarmerItemsShippedPercent(f)).Value,15,"原生全出货集合",new[]{"achievement:34"}),
            new Part("obelisks",Math.Min(4,Utility.GetObeliskTypesBuilt())/4d,4,"原生不同图腾建筑",new[]{"build:Water Obelisk","build:Earth Obelisk","build:Desert Obelisk","build:Island Obelisk"}),
            new Part("clock",Game1.IsBuildingConstructed("Gold Clock")?1:0,10,"原生金钟建筑",new[]{"build:Gold Clock"}),
            new Part("monster_slayer",Utility.GetFarmCompletion(f=>f.hasCompletedAllMonsterSlayerQuests.Value).Value?1:0,10,"原生怪物讨伐完成标记",new[]{"platform-condition:Achievement_KeeperOfTheMysticRings"}),
            new Part("friends",Utility.GetFarmCompletion(f=>Utility.getMaxedFriendshipPercent(f)).Value,11,"原生人物完美度友情门槛",Game1.characterData.Where(t=>t.Value.PerfectionScore&&!GameStateQuery.IsImmutablyFalse(t.Value.CanSocialize)).Select(t=>"friendship:"+t.Key).ToArray()),
            new Part("skills",Utility.GetFarmCompletion(f=>Math.Min(f.Level,25f)/25f).Value,5,"原生角色等级",new[]{"platform-condition:Achievement_MasterOfTheFiveWays"}),
            new Part("stardrops",Utility.GetFarmCompletion(f=>Utility.foundAllStardrops(f)).Value?1:0,10,"原生全部星之果实",new[]{"platform-condition:Achievement_Stardrop"}),
            new Part("cooking",Utility.GetFarmCompletion(f=>Utility.getCookedRecipesPercent(f)).Value,10,"原生已烹饪配方比例",new[]{"achievement:17"}),
            new Part("crafting",Utility.GetFarmCompletion(f=>Utility.getCraftedRecipesPercent(f)).Value,10,"原生已制作配方比例",new[]{"achievement:22"}),
            new Part("fishing",Utility.GetFarmCompletion(f=>Utility.getFishCaughtPercent(f)).Value,10,"原生捕获收集比例",new[]{"achievement:26"}),
            new Part("walnuts",Math.Min(130,Game1.netWorldState.Value.GoldenWalnutsFound)/130d,5,"原生已发现核桃数量",new[]{"scope:walnuts"})
        };
    }
    internal static object Read()=>new{raw_fraction=Utility.percentGameComplete(),waivers=Game1.netWorldState.Value.PerfectionWaivers,parts=Parts(),native_overnight_completion=Game1.MasterPlayer.hasOrWillReceiveMail("Farm_Eternal"),note="读取原生完美度；原生总分、单项实绩、豁免券与过夜解锁分开，未将付款豁免当作任务完成，也不授予成就。"};
}
