using System.Text.Json;
using StardewValley;
using StardewValley.Menus;
using StardewValley.Tools;

namespace Together;
public sealed partial class ModEntry {
    private ProgressDependency FishingAcquisition() {
        var p=Game1.player;var node=new ProgressDependency{id="capability:fishing",title="取得可用鱼竿",kind="capability",state="locked"};
        bool owned=p.Items.OfType<FishingRod>().Any()||SharedStorage().Any(s=>s.Chest.GetItemsForPlayer().OfType<FishingRod>().Any());
        if(owned){node.state="complete";node.evidence=new{source="native_inventory_and_shared_storage",owned};return node;}
        var beach=Game1.getLocationFromName("Beach");
        // Native event 739330 grants BambooPole (including its native skip path).
        // Evaluate the loaded event's full conditions, not a guessed day/time rule.
        var entry=Game1.content.Load<Dictionary<string,string>>("Data/Events/Beach").FirstOrDefault(e=>e.Key.Split('/')[0]=="739330");
        bool route=beach!=null&&(beach==Game1.currentLocation||PlayerExecutor.NextExit(Game1.currentLocation,"Beach")!=null);
        bool eligible=beach!=null&&entry.Key!=null&&beach.checkEventPrecondition(entry.Key)!="-1";
        string? invitationMail=entry.Key?.Split('/').Where(c=>c.StartsWith("*n ")).Select(c=>c[3..]).FirstOrDefault(mail=>Game1.mailbox.Contains(mail));
        bool invitation=invitationMail!=null;
        bool capacity=CapacityAdapter.CanReceive(p,ItemRegistry.Create("(T)BambooPole"));
        node.evidence=new{source="Data/Events/Beach",event_key=entry.Key,native_preconditions=eligible,route,capacity,invitation_in_mailbox=invitation,seen=p.eventsSeen.Contains("739330")};
        if(invitation&&route&&capacity){node.state="available";node.actions.Add(new{tool="player.read_mail",args=new{count=1}});node.dependencies.Add(new("mail:"+invitationMail,relation:"read_native_invitation"));}
        else if(eligible&&route&&capacity){node.state="available";node.actions.Add(new{tool="player.travel",args=new{location="Beach"}});}
        else node.gaps.Add("等待原生事件条件/可达路线/接收容量；不能直接授予鱼竿");
        return node;
    }
    private void AddCapabilityOpportunities(List<OperatingOpportunity> rows) {
        var p=Game1.player;var rod=FishingAcquisition();
        if(rod.state=="available") {
            var action=JsonSerializer.SerializeToElement(rod.actions[0]);
            rows.Add(new("acquire:fishing","先读 Willy 的真实邀请信，再进入满足原生条件的海滩事件；依赖可查 capability:fishing",action.GetProperty("tool").GetString()!,action.GetProperty("args").Clone(),AgentJson.Encode(rod.evidence),0,40));
        }
        // Boards and shops must actually be open: static catalogues are not proof
        // of available offers or authorization to spend money.
        if(Game1.activeClickableMenu is Billboard board&&board.acceptQuestButton.visible&&Game1.questOfTheDay is {} quest&&!quest.accepted.Value)
            rows.Add(new("accept:"+NativeQuestIdentity.Id(quest),"接受当前任务板上实际可接任务","player.accept_quest",new{id=NativeQuestIdentity.Id(quest)},AgentJson.Encode(PlayerExecutor.ReadQuestBoard()),0,1));
        if(Game1.activeClickableMenu is SpecialOrdersBoard orders&&(orders.acceptLeftQuestButton.visible||orders.acceptRightQuestButton.visible))
            rows.Add(new("inspect:orders","比较当前任务板的真实任务及期限","quest_board.read",new{},AgentJson.Encode(PlayerExecutor.ReadQuestBoard()),0,1));
        var missing=Data.SharedGoals.Where(g=>g.Status=="active").SelectMany(g=>g.Nodes.Where(n=>n.ToPrepare>0).Select(n=>(Goal:g,Node:n))).Take(12).ToArray();
        if(Game1.activeClickableMenu==null&&Data.Autoplay.Capacity.Constraints.Count>0&&Game1.currentLocation.Name=="SeedShop"&&p.MaxItems is 12 or 24) {
            // Native GameLocation BuyBackpack offers 12->24 for 2000, 24->36 for
            // 10000. This action only opens the quote; no automatic purchase.
            int price=p.MaxItems==12?2000:10000;var l=Game1.currentLocation;
            if(p.Money-OtherCommittedCash("")>=price) {
                bool found=false;
                for(int y=0;y<l.Map.Layers[0].LayerHeight&&!found;y++)for(int x=0;x<l.Map.Layers[0].LayerWidth&&!found;x++)
                    if(l.GetTilePropertySplitBySpaces("Action","Buildings",x,y).FirstOrDefault()=="BuyBackpack"&&WorkStand(l,new(x,y))!=null) {
                        rows.Add(new("inspect:backpack","容量受阻，查看原生背包扩容报价；购买须显式授权","player.interact",new{x,y},$"map_action=BuyBackpack;slots={p.MaxItems};native_price={price};cash={p.Money};capacity_version={Data.Autoplay.Capacity.Version}",0,5));found=true;
                    }
            }
        }
        if(Game1.activeClickableMenu==null&&p.Money>OtherCommittedCash(""))foreach(var m in missing.Take(2)) {
            var source=PlayerExecutor.ShopSources(m.Node.Item).FirstOrDefault();if(source.Shop==null)continue;
            var window=ServiceWindow(source.Location);if(window.Reason!="available"||Game1.timeOfDay<window.Open||Game1.timeOfDay>=window.Close-100)continue;
            if(source.Location!=Game1.currentLocation.NameOrUniqueName&&PlayerExecutor.NextExit(Game1.currentLocation,source.Location)==null)continue;
            string owner=source.Location switch{"SeedShop"=>"Pierre","FishShop"=>"Willy","AnimalShop"=>"Marnie","Blacksmith"=>"Clint",_=>""};
            if(owner.Length==0||Game1.getCharacterFromName(owner)?.currentLocation?.NameOrUniqueName!=source.Location)continue;
            rows.Add(new("quote:"+m.Node.Item,"已批准目标缺料：到营业且店主在场的商店核对真实报价；不是购买承诺","player.service",new{location=source.Location,shop=source.Shop,service="shop"},$"goal={m.Goal.Id};missing={m.Node.ToPrepare};source=Data/Shops;window={window.Open}-{window.Close};owner={owner};cash={p.Money}",0,40));break;
        }
        if(Game1.activeClickableMenu is ShopMenu shop&&shop.currency==0) {
            var offer=shop.itemPriceAndStock.FirstOrDefault(o=>missing.Any(m=>m.Node.Item==o.Key.QualifiedItemId)&&o.Value.Stock>0&&o.Value.Price>=0&&o.Value.Price<=p.Money&&o.Value.TradeItem==null);
            if(offer.Key!=null)rows.Add(new("procurement:"+offer.Key.QualifiedItemId,"已批准项目缺料；读取现场报价后明确预算采购","shop.read",new{},$"native_shop={shop.ShopId};money={p.Money};price={offer.Value.Price};approved_goals="+string.Join(",",missing.Where(m=>m.Node.Item==offer.Key.QualifiedItemId).Select(m=>m.Goal.Id)),0,1));
            var upgrade=shop.itemPriceAndStock.FirstOrDefault(o=>o.Key is Tool&&o.Value.Price>0&&o.Value.Price<=p.Money&&o.Value.Stock>0&&o.Value.TradeItem==null&&o.Key.CanBuyItem(p));
            if(upgrade.Key!=null&&Data.Autoplay.Capacity.Constraints.Count>0)rows.Add(new("inspect:upgrade","容量已受阻，核对现场工具升级报价；购买仍需显式预算","shop.read",new{},$"native_shop={shop.ShopId};price={upgrade.Value.Price};capacity_version={Data.Autoplay.Capacity.Version}",0,1));
        }
        foreach(var m in missing.Where(m=>m.Node.Status is "blocked" or "missing" or "locked").Take(2)) {
            if(Knowledge.Ready&&Knowledge.Index.Entries.Any(e=>e.Id==m.Node.Item&&Knowledge.Visible(e)))
                rows.Add(new("knowledge:"+m.Node.Item,"查询已批准目标的未解决物资依赖及取得途径","knowledge.get",new{id=m.Node.Item},$"goal={m.Goal.Id};missing={m.Node.ToPrepare};status={m.Node.Status};encyclopedia_entry_present",0,1));
        }
    }
}
