using System.Collections;
using System.Reflection;
using StardewValley;
using StardewValley.Locations;
using StardewValley.Objects;

namespace Together;
public static class ProgressReader {
    private static object? Field(object o,string name) {
        var raw=o.GetType().GetField(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(o)
            ?? o.GetType().GetProperty(name,BindingFlags.Public|BindingFlags.Instance)?.GetValue(o);
        return raw?.GetType().GetProperty("Value")?.GetValue(raw)??raw;
    }
    private static string Text(object o,params string[] names)=>names.Select(n=>Field(o,n)?.ToString()).FirstOrDefault(s=>!string.IsNullOrEmpty(s))??"";
    private static int Number(object o,params string[] names)=>int.TryParse(Text(o,names),out int v)?v:0;
    public static void Read(WorldFacts f) {
        var player=Game1.player;
        foreach(var q in player.questLog) {
            string type=q.GetType().Name;
            var goal=new ProgressGoal{Id="quest:"+q.id.Value,Kind=type,Title=q.questTitle,Complete=q.completed.Value,
                Deadline=q.daysLeft.Value>0?f.Day+q.daysLeft.Value:-1};
            string item=Text(q,"ItemId","itemId","fishId","resource","item");
            int count=Math.Max(1,Number(q,"number","numberToCollect","numberToFish","numberToDeliver"));
            if(item.Length>0 && !item.Contains(" ") && type is "ItemDeliveryQuest" or "ResourceCollectionQuest" or "FishingQuest") {
                string id=ItemRegistry.QualifyItemId(item)??item;
                if(!ItemRegistry.GetDataOrErrorItem(id).IsErrorItem)goal.Needs.Add(new(){Item=id,Name=ItemRegistry.GetDataOrErrorItem(id).DisplayName,Count=count,
                    Owned=f.Stock.Where(s=>s.Item==id).Sum(s=>s.Count),Source=goal.Id});
            }
            goal.PlayerStep=type switch {
                "ItemDeliveryQuest"=>"伙伴可备料、送回收货箱并陪同；由玩家亲手向目标 NPC 交付，游戏确认完成。",
                "ResourceCollectionQuest"=>"备料与采集计数不同：观察任务实际计数，不能只凭库存判完成；由玩家交付。",
                "FishingQuest"=>"伙伴渔获可供备料；是否计入指定钓鱼任务只以游戏实际计数为准，不能替代玩家亲自钓鱼要求。",
                "SlayMonsterQuest"=>"可一起战斗；击杀归属与目标类型由原生任务计数确认，由玩家领奖。",
                _=>"读取目标与期限，陪同和备料；本任务的特殊交互由玩家完成。"
            };
            f.Goals.Add(goal);
        }
        foreach(var order in player.team.specialOrders) {
            var goal=new ProgressGoal{Id="order:"+Text(order,"questKey"),Kind="special_order",Title=order.GetName(),Complete=order.questState.Value.ToString()=="Complete",
                Deadline=order.dueDate.Value,PlayerStep="特殊订单使用各自目标的实际计数；当前提供备料与陪同，不代替专属交付交互。"};
            f.Goals.Add(goal);
            foreach(var objective in order.objectives) {
                int current=Number(objective,"currentCount"),max=Number(objective,"maxCount");
                f.Objectives.Add(new{goal=goal.Id,type=objective.GetType().Name,current,max,
                    description=objective.GetType().GetMethod("GetDescription",Type.EmptyTypes)?.Invoke(objective,null)?.ToString()??""});
            }
        }
        if(f.Route=="joja") {
            string[] flags={"ccVault","ccBoilerRoom","ccCraftsRoom","ccPantry","ccFishTank"};
            string[] names={"修复巴士","修复矿车","修复桥梁","修复温室","移走闪光巨石"};
            // These prices are the native JojaCDMenu 1.6 contract (not a guessed market price).
            int[] prices={40000,15000,25000,35000,20000};
            for(int i=0;i<5;i++)f.Goals.Add(new(){Id="joja:"+flags[i],Kind="joja",Title=names[i],Gold=prices[i],
                Complete=Utility.doesAnyFarmerHaveOrWillReceiveMail(flags[i]),PlayerStep="共同攒钱；由玩家在 Joja 建设表选择并确认购买。"});
        }
        foreach(var pair in DataLoader.Crops(Game1.content)) {
            var crop=pair.Value;string seed=ItemRegistry.QualifyItemId(pair.Key)??pair.Key;
            f.Seeds.Add(new(){Item=seed,Name=ItemRegistry.GetDataOrErrorItem(seed).DisplayName,Harvest=ItemRegistry.QualifyItemId(crop.HarvestItemId)??crop.HarvestItemId,
                Days=crop.DaysInPhase.Sum(),Seasons=crop.Seasons.Select(s=>s.ToString().ToLowerInvariant()).ToList()});
        }
        var farm=Game1.getFarm();
        foreach(var b in farm.buildings)if(b.GetIndoors() is AnimalHouse house) {
            int placed=house.objects.Pairs.Count(p=>p.Value.QualifiedItemId=="(O)178" && house.doesTileHaveProperty((int)p.Key.X,(int)p.Key.Y,"Trough","Back")!=null);
            int missing=Math.Max(0,house.animalsThatLiveHere.Count-placed);
            if(missing>0){f.FeedNeeded+=missing;f.CareLocations.Add(new(){Location=house.NameOrUniqueName,Skill="feed",Count=missing,Reason="食槽缺少干草"});}
            int pets=house.animals.Values.Count(a=>!a.wasPet.Value);
            if(pets>0)f.CareLocations.Add(new(){Location=house.NameOrUniqueName,Skill="pet",Count=pets,Reason="屋里的动物还没有被照料"});
        }
        f.HayInSilo=farm.piecesOfHay.Value;
        if(f.FeedNeeded>0)f.Alerts.Add($"食槽还缺 {f.FeedNeeded} 份干草（农场筒仓 {f.HayInSilo} 份）");
        foreach(string recipe in player.craftingRecipes.Keys) {
            var r=new CraftingRecipe(recipe,false);
            var goal=new ProgressGoal{Id="craft:"+recipe,Kind="craft",Title="准备制作："+r.DisplayName,PlayerStep="材料准备好后，由玩家制作并选址放置。"};
            foreach(var pair in r.recipeList) {
                string id=ItemRegistry.QualifyItemId(pair.Key)??pair.Key;
                goal.Needs.Add(new(){Item=id,Count=pair.Value,Name=ItemRegistry.GetDataOrErrorItem(id).DisplayName,
                    Owned=f.Stock.Where(s=>s.Item==id || (int.TryParse(pair.Key,out int cat) && cat<0 && cat==s.Category)).Sum(s=>s.Count),Source=goal.Id});
            }
            f.Goals.Add(goal);
        }
    }
}
public sealed class SeedFact {
    public string Item {get;set;}="";
    public string Name {get;set;}="";
    public string Harvest {get;set;}="";
    public int Days {get;set;}
    public List<string> Seasons {get;set;}=new();
}
