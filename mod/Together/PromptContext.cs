namespace Together;
public sealed partial class ModEntry {
    private object PromptFarm() {
        var active=Data.Projects.Where(p=>p.Status=="active").Select(p=>p.Kind).ToHashSet();
        var needed=Data.Projects.Where(p=>p.Status=="active").SelectMany(p=>p.Needs).Select(n=>n.Item).ToHashSet();
        return new {Facts.Day,Facts.Time,Facts.Season,Facts.Route,Facts.Money,Facts.SpentToday,Facts.Purchased,Facts.PurchasedItems,transactions=Facts.Transactions.TakeLast(5),Facts.DryCrops,Facts.RipeCrops,Facts.DeadCrops,
            Facts.AnimalsUnpetted,Facts.FeedNeeded,Facts.HayInSilo,Facts.MachinesReady,Facts.Progress,Facts.Alerts,Facts.Errors,
            Goals=Facts.Goals.Where(g=>active.Contains(g.Id) || g.Kind.EndsWith("Quest") || g.Kind=="special_order").Take(12),
            Bundles=Facts.Bundles.Where(b=>active.Contains("bundle:"+b.Id)).Take(4),
            Objectives=Facts.Objectives.Take(8),Stock=Facts.Stock.OrderByDescending(s=>needed.Contains(s.Item)).Take(36),
            Crops=Facts.Crops.Take(12),Machines=Facts.Machines.Take(8),
            permissions=new{Data.FarmPolicy.DailyBudget,Data.FarmPolicy.KeepGold,areas=Data.FarmPolicy.Areas.Take(8),shopping=Data.FarmPolicy.Shopping.Where(o=>o.Enabled).Take(8)},
            note="列表是有界摘要；未列出不等于不存在，不能猜测未观察的数量。"};
    }
    private static object PromptSocial(Companion p)=>new {
        p.Social.Mode,p.Social.TheirTurn,
        relationship=new{p.Social.Relationship.Trust,p.Social.Relationship.Comfort,p.Social.Relationship.Cooperation},
        Preferences=p.Social.Preferences.TakeLast(12),Topics=p.Social.Topics.TakeLast(3),Diary=p.Social.Diary.TakeLast(3),
        Habits=p.Social.Habits.Where(h=>h.Enabled && h.Confirmed).Take(4),Wishes=p.Social.Wishes.TakeLast(3),p.Social.Challenge
    };
}
