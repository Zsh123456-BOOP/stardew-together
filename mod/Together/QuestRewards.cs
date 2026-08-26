using StardewValley;
using StardewValley.BellsAndWhistles;
using StardewValley.Menus;
using StardewValley.Quests;

namespace Together;

// Expose only the native journal's current page; navigation and reward claiming
// still run receiveLeftClick. Never call reward callbacks or change money here.
internal sealed class AgentQuestLog:QuestLog {
    public IReadOnlyList<IQuest> Visible=>pages[currentPage];
    public bool CanNext=>currentPage<pages.Count-1;
}
public sealed partial class PlayerExecutor {
    private AgentQuestLog? rewardMenu;
    private IQuest? rewardQuest;
    private string rewardId="";
    private int rewardBefore,rewardAmount;
    private void StartQuestReward(System.Text.Json.JsonElement args) {
        rewardId=AgentToolRegistry.Text(args,"quest_id");
        rewardQuest=Game1.player.questLog.FirstOrDefault(q=>NativeQuestIdentity.Id(q)==rewardId);
        rewardQuest??=Game1.player.team.specialOrders.FirstOrDefault(q=>q.questKey.Value==rewardId);
        if(rewardQuest==null||!rewardQuest.ShouldDisplayAsComplete()||!rewardQuest.HasMoneyReward())throw new InvalidOperationException("completed_unclaimed_money_reward_required");
        rewardBefore=Game1.player.Money;rewardAmount=rewardQuest.GetMoneyReward();rewardMenu=new();Game1.activeClickableMenu=rewardMenu;Current!.phase="reward_find";
    }
    private void TickQuestReward() {
        if(Game1.activeClickableMenu!=rewardMenu||rewardMenu==null||rewardQuest==null)throw new InvalidOperationException("reward_journal_replaced");
        if(DateTime.UtcNow<nextInteraction)return;nextInteraction=DateTime.UtcNow.AddMilliseconds(180);
        if(Current!.phase=="reward_verify") {
            bool done=!rewardQuest.HasMoneyReward()&&Game1.player.Money-rewardBefore==rewardAmount;
            Current.effects.Add(new{kind="native_quest_reward",quest=rewardId,expected=rewardAmount,money_before=rewardBefore,money_after=Game1.player.Money,unclaimed=rewardQuest.HasMoneyReward()});
            if(rewardMenu.readyToClose())rewardMenu.exitThisMenu();rewardMenu=null;
            Finish(done?"succeeded":"failed",done?null:"quest_reward_not_verified");return;
        }
        if(Current.phase=="reward_claim") {
            int offset=rewardQuest.IsTimedQuest()&&rewardQuest.GetDaysLeft()>0&&SpriteText.getWidthOfString(rewardQuest.GetName())>rewardMenu.width/2?-48:0;
            var box=rewardMenu.rewardBox.bounds;rewardMenu.receiveLeftClick(box.Center.X,box.Center.Y-offset);Current.phase="reward_verify";return;
        }
        int index=rewardMenu.Visible.ToList().FindIndex(q=>ReferenceEquals(q,rewardQuest));
        if(index>=0){var box=rewardMenu.questLogButtons[index].bounds;rewardMenu.receiveLeftClick(box.Center.X,box.Center.Y);Current.phase="reward_claim";return;}
        if(!rewardMenu.CanNext)throw new InvalidOperationException("reward_not_in_native_journal");
        var next=rewardMenu.forwardButton.bounds;rewardMenu.receiveLeftClick(next.Center.X,next.Center.Y);
    }
}
