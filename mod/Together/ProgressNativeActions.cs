using System.Text.Json;
using StardewValley;
using StardewValley.Extensions;
using StardewValley.Locations;
using StardewValley.Quests;

namespace Together;
public sealed partial class ModEntry {
    private bool ReconcilePursuitTasks(ProgressPursuit pursuit) {
        if(pursuit.Tasks.Count==0)return true;
        var tasks=pursuit.Tasks.Select(id=>Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==id)).ToArray();
        if(tasks.Any(t=>t==null)){pursuit.Tasks.Clear();PursuitState(pursuit,"blocked","progress_task_receipt_missing_reobserve");return false;}
        if(tasks.Any(t=>!t!.Terminal)){if(tasks.Any(t=>t!.state=="needs_review"))PursuitState(pursuit,"blocked","progress_task_interrupted_reobserve");return false;}
        pursuit.Tasks.Clear();
        if(tasks.Any(t=>t!.state!="succeeded")){PursuitState(pursuit,"blocked",tasks.First(t=>t!.state!="succeeded")!.error??"progress_action_failed");return false;}
        return true;
    }
    private bool QueuePursuit(ProgressPursuit pursuit,IEnumerable<(string Tool,object Args)> operations,int cost=0) {
        var policy=Data.Autoplay.Campaign;
        if(cost>policy.BudgetPerDay-policy.ReservedGold||cost>0&&Game1.player.Money-cost<policy.KeepGold){PursuitState(pursuit,"waiting","progress_daily_budget_or_reserve_insufficient");return false;}
        if(pursuit.AttemptsDay!=Game1.Date.TotalDays){pursuit.AttemptsDay=Game1.Date.TotalDays;pursuit.Attempts=0;}
        if(pursuit.Attempts>=12){PursuitState(pursuit,"waiting","progress_daily_batch_limit_replan_or_continue_tomorrow");return false;}
        var tasks=operations.Select(o=>{var args=JsonSerializer.SerializeToElement(o.Args);return new AgentTaskSpec{actor=o.Tool is "work.run" or "companion.assign"?AgentToolRegistry.Text(args,"actor_id","player"):"player",id="pursuit-"+Guid.NewGuid().ToString("N"),tool=o.Tool,args=args,purpose="推进原生目标 "+pursuit.Target,day=Game1.Date.TotalDays,deadline=2200};}).ToList();
        if(tasks.Count==0)return false;
        if(Data.Autoplay.Schedule.Tasks.Count+tasks.Count>180)Data.Autoplay.Schedule.Archive();
        Data.Autoplay.Schedule.Submit("pursuit-"+Guid.NewGuid().ToString("N"),Data.Autoplay.Schedule.Revision,tasks,Game1.Date.TotalDays);
        policy.ReservedGold+=cost;pursuit.Attempts++;pursuit.Tasks=tasks.Select(t=>t.id).ToList();PursuitState(pursuit,"running","已按实际条件排队；等待原生结果后重新核验");return true;
    }
    private bool TryClaimPursuitReward(ProgressPursuit pursuit) {
        if(pursuit.State=="blocked")return false;
        IQuest? quest=pursuit.Target.StartsWith("quest:")?Game1.player.questLog.FirstOrDefault(q=>NativeQuestIdentity.Id(q)==pursuit.Target[6..]):pursuit.Target.StartsWith("order:")?Game1.player.team.specialOrders.FirstOrDefault(q=>q.questKey.Value==pursuit.Target[6..]):null;
        return quest?.ShouldDisplayAsComplete()==true&&quest.HasMoneyReward()&&QueuePursuit(pursuit,new[]{("player.claim_reward",(object)new{quest_id=pursuit.Target[6..]})});
    }
    // Withdraw exact real stock first. Only delegate acquisition to a shared goal
    // when its native source is supported; high quality is never fabricated by a base recipe.
    private bool PursuitMaterials(ProgressPursuit pursuit,IEnumerable<(string Item,int Count,int Quality)> requirements,List<(string Tool,object Args)> actions) {
        var bag=new GoalLedger(Game1.player.Items.Where(i=>i!=null).Select(i=>new GoalStock{Item=i.QualifiedItemId,Count=i.Stack,Quality=i.Quality,Category=i.Category}));
        var stored=SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer().Where(i=>i!=null)).ToArray();
        var store=new GoalLedger(stored.Select(i=>new GoalStock{Item=i.QualifiedItemId,Count=i.Stack,Quality=i.Quality,Category=i.Category}));
        foreach(var need in requirements.OrderByDescending(n=>n.Quality)) {
            int missing=need.Count-bag.Take(need.Item,need.Count,need.Quality);
            bool category=int.TryParse(need.Item.Replace("(O)",""),out int c)&&c<0;
            foreach(var group in stored.Where(i=>i.Quality>=need.Quality&&(i.QualifiedItemId==need.Item||category&&i.Category==c)).GroupBy(i=>i.QualifiedItemId)) {
                if(missing<=0)break;int take=store.Take(group.Key,missing,need.Quality);if(take==0)continue;
                actions.Add(("work.run",new{goal="withdraw",item=group.Key,count=take,quality=need.Quality}));missing-=take;
            }
            if(missing<=0)continue;
            actions.Clear();
            if(category){PursuitState(pursuit,"waiting","需要合格库存或明确获得路线："+need.Item+" 品质≥"+need.Quality+" 缺"+missing);return false;}
            if(!CanPreparePursuitItem(need.Item,need.Count,need.Quality)){PursuitState(pursuit,"waiting","需要先取得当前采集/生产链之外的材料："+need.Item+" 缺"+missing);return false;}
            var goal=(SharedGoal)AgentGoalCreate(JsonSerializer.SerializeToElement(new{request_id="pursuit-"+FailureKnowledge.Hash(pursuit.Target)[..12]+"-"+(++pursuit.Revision),entity=need.Item,count=need.Count,quality=need.Quality,completion="owned"}));
            pursuit.ChildGoal=goal.Id;
            if(goal.Status=="active")AgentGoalRun(JsonSerializer.SerializeToElement(new{id=goal.Id}));
            PursuitState(pursuit,"running","先完成原生目标的材料依赖："+need.Item);return false;
        }
        return true;
    }
    private bool CanPreparePursuitItem(string item,int count,int quality=0) {
        var preview=new SharedGoal{Entity=item,Item=item,Count=count,MinimumQuality=quality};var ledger=new GoalLedger(Facts.Stock.Select(s=>new GoalStock{Item=s.Item,Count=s.Count,Quality=s.Quality,Category=s.Category}));
        foreach(var reserve in AllReservations().OrderByDescending(r=>r.Quality))ledger.Take(reserve.Item,reserve.Count,reserve.Quality);
        GoalPlanner.Rebuild(preview,goalRecipes,ledger,Game1.Date.TotalDays,id=>id);
        return !preview.Nodes.Any(n=>n.Status is "locked" or "blocked")&&preview.Nodes.Where(n=>n.Kind=="gather"&&n.ToPrepare>0).All(n=>n.Quality==0&&(n.Item is "(O)388" or "(O)390" or "(O)771"||n.Item=="(O)709"&&FindGoalResourceLocation(n.Item,"hardwood")!=null||ResourceRules.Nodes.Values.Contains(n.Item)&&FindGoalResourceLocation(n.Item,"resource")!=null||FishingLocations(n.Item).Any())||HasLivingMaterialRoute(n.Item));
    }
    private bool PursueHouse(ProgressPursuit pursuit,int target) {
        var p=Game1.player;var policy=Data.Autoplay.Campaign;var actions=new List<(string Tool,object Args)>();
            if(p.HouseUpgradeLevel>=target||p.daysUntilHouseUpgrade.Value>=0){PursuitState(pursuit,"waiting","等待原生房屋施工或成就登记");return true;}
            int level=p.HouseUpgradeLevel+1,cost=level==1?10000:level==2?65000:100000;
            if(cost>policy.BudgetPerDay-policy.ReservedGold||p.Money-cost<policy.KeepGold){PursuitState(pursuit,"waiting","房屋升级需要预算及实际现金 "+cost);return true;}
            var needs=level<3?new[]{(level==1?"(O)388":"(O)709",level==1?450:100,0)}:Array.Empty<(string,int,int)>();
            if(!PursuitMaterials(pursuit,needs,actions))return true;
            actions.Add(("player.upgrade_house",new{budget=cost,keep_gold=policy.KeepGold}));QueuePursuit(pursuit,actions,cost);return true;
        }
    private bool TryAdvanceNativePursuit(ProgressPursuit pursuit,NativeGoalDefinition definition) {
        string id=pursuit.Target;var p=Game1.player;var policy=Data.Autoplay.Campaign;var actions=new List<(string Tool,object Args)>();
        if(id.StartsWith("achievement:")&&int.TryParse(id[12..],out int incomeAchievement)&&incomeAchievement is >=0 and <=4)return PursueIncome(pursuit,incomeAchievement);
        if(id is "achievement:29" or "achievement:30")return PursueQuestAchievement(pursuit,int.Parse(id[12..]));
        if(id is "family:marriage" or "family:children" or "platform-condition:Achievement_FullHouse")return PursueFamily(pursuit,id!="family:marriage");
        if(id.StartsWith("friendship:")) {
            string npc=id[11..];int threshold=Game1.characterData.TryGetValue(npc,out var character)&&character.CanBeRomanced?2000:2500;
            return PursueFriendship(pursuit,threshold,npc);
        }
        if(id is "achievement:6" or "achievement:7" or "achievement:9" or "achievement:11" or "achievement:12" or "achievement:13") {
            var rule=JsonSerializer.SerializeToElement(AchievementRules.Native(int.Parse(id[12..])));
            if(rule.GetProperty("condition_satisfied").GetBoolean()){PursuitState(pursuit,"waiting","关系条件已满足，等待原生成就登记");return true;}
            return PursueFriendship(pursuit,rule.GetProperty("details").GetProperty("minimum_points").GetInt32());
        }
        if(id=="scope:perfection"||id.StartsWith("perfection:")) {
            PursuitState(pursuit,"waiting","持续推进已展开分项；只以原生比例和完成记录结束本目标");return true;
        }
        if(id.StartsWith("build:")) {
            var key=id[6..];var data=DataLoader.Buildings(Game1.content).GetValueOrDefault(key);if(data==null)return false;
            if(!GameStateQuery.CheckConditions(data.BuildCondition,Game1.getFarm(),p,random:new Random(0))){PursuitState(pursuit,"waiting","建筑原生解锁条件未满足");return true;}
            if(!string.IsNullOrEmpty(data.BuildingToUpgrade)&&!Game1.IsBuildingConstructed(data.BuildingToUpgrade)){PursuitState(pursuit,"waiting","先完成前置建筑："+data.BuildingToUpgrade);return true;}
            if(Game1.getFarm().buildings.Any(b=>b.daysOfConstructionLeft.Value>0||b.daysUntilUpgrade.Value>0)){PursuitState(pursuit,"waiting","等待已有建筑按原生工期完工");return true;}
            if(data.Builder=="Wizard"&&!p.mailReceived.Contains("hasPickedUpMagicInk")&&!p.hasMagicInk){PursuitState(pursuit,"waiting","巫师建筑需要原生魔法墨水解锁");return true;}
            if(!PursuitMaterials(pursuit,(data.BuildMaterials??new()).Select(m=>(ItemRegistry.QualifyItemId(m.ItemId)??m.ItemId,m.Amount,0)),actions))return true;
            actions.Add(("player.service",new{location=data.Builder=="Wizard"?"WizardHouse":"ScienceHouse",service="build"}));actions.Add(("player.build",new{blueprint=key,budget=data.BuildCost,keep_gold=policy.KeepGold}));QueuePursuit(pursuit,actions,data.BuildCost);return true;
        }
        if(id.StartsWith("fish:")||id is "achievement:24" or "achievement:25" or "achievement:26" or "achievement:27") {
            string wanted=id.StartsWith("fish:")?id[5..]:"";
            if(wanted.Length>0&&PlayerExecutor.IsTrapFish(wanted))return PursueTrapFish(pursuit,wanted);
            if(!p.Items.OfType<StardewValley.Tools.FishingRod>().Any()){PursuitState(pursuit,"waiting","先通过原生领取或商店取得鱼竿");return true;}
            if(id.StartsWith("achievement:")) {
                int achievement=int.Parse(id[12..]);var rule=JsonSerializer.SerializeToElement(AchievementRules.Native(achievement));
                if(rule.GetProperty("condition_satisfied").GetBoolean()){PursuitState(pursuit,"waiting","捕获记录已达标，等待原生成就登记");return true;}
                if(achievement!=27&&p.fishCaught.Any())wanted=rule.GetProperty("details").GetProperty("missing").EnumerateArray().Select(i=>i.GetString()!).FirstOrDefault(i=>FishingLocations(i).Any()||PlayerExecutor.IsTrapFish(i))??"unavailable";
            }
            if(wanted.Length>0&&PlayerExecutor.IsTrapFish(wanted))return PursueTrapFish(pursuit,wanted);
            if(wanted=="unavailable"||!FishingLocations(wanted).Any()){PursuitState(pursuit,"waiting","未捕获鱼类当前无可达合格水域；需其它季节/天气/等级/解锁或蟹笼");return true;}
            QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="fish",item=wanted,count=wanted.Length==0?3:1,until=2100})});return true;
        }
        if(id=="region:caldera") {
            if(!Game1.MasterPlayer.mailReceived.Contains("willyBoatFixed")){PursuitState(pursuit,"waiting","先实际修复并开放船坞");return true;}
            int cost=p.currentLocation is IslandLocation?0:(Game1.getLocationFromName("BoatTunnel") as BoatTunnel)?.TicketPrice??1000;
            QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="volcano_trip",target_level=10,travel_budget=cost,keep_gold=policy.KeepGold,until=2000})},cost);return true;
        }
        if(id.StartsWith("island-upgrade:")) {
            var parts=id.Split(':');var perch=PlayerExecutor.IslandPerches().FirstOrDefault(t=>t.Location.NameOrUniqueName==parts[1]&&t.Perch.upgradeName.Value==parts[2]).Perch;
            if(perch==null||!perch.IsAvailable()){PursuitState(pursuit,"waiting","鹦鹉建设尚缺原生前置条件");return true;}
            int cost=perch.requiredNuts.Value;
            if(cost>policy.NutsPerDay-policy.ReservedNuts||Game1.netWorldState.Value.GoldenWalnuts-cost<policy.KeepNuts){PursuitState(pursuit,"waiting","需要真实金核桃及专用每日核桃预算");return true;}
            if(QueuePursuit(pursuit,new[]{("player.island_upgrade",(object)new{location=parts[1],upgrade=parts[2],budget_nuts=cost,keep_nuts=policy.KeepNuts})}))policy.ReservedNuts+=cost;
            return true;
        }
        if(id=="achievement:42") {
            if(p.Items.Any(i=>i?.QualifiedItemId is "(W)62" or "(W)63" or "(W)64")){PursuitState(pursuit,"waiting","已拥有无尽武器，等待原生成就登记");return true;}
            var soul=ItemRegistry.Create("(O)896");var weapon=p.Items.OfType<StardewValley.Tools.MeleeWeapon>().FirstOrDefault(w=>w.CanForge(soul));
            if(weapon==null){PursuitState(pursuit,"waiting","先取得可接受银河之魂的原生武器");return true;}
            int level=weapon.enchantments.OfType<StardewValley.Enchantments.GalaxySoulEnchantment>().Select(e=>e.GetLevel()).FirstOrDefault(),need=Math.Max(1,3-level);
            if(!PursuitMaterials(pursuit,new[]{("(O)896",need,0),("(O)848",20*need,0)},actions))return true;
            if(actions.Count>0){QueuePursuit(pursuit,actions);return true;}
            if(Game1.currentLocation is not Caldera&&!p.hasOrWillReceiveMail("volcanoShortcutUnlocked")) {
                int travel=p.currentLocation is IslandLocation?0:(Game1.getLocationFromName("BoatTunnel") as BoatTunnel)?.TicketPrice??1000;
                QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="volcano_trip",target_level=10,travel_budget=travel,keep_gold=policy.KeepGold,until=2000})},travel);return true;
            }
            int left=p.Items.IndexOf(weapon),right=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]?.QualifiedItemId=="(O)896",-1);
            if(right<0)return false;
            if(p.Items.Count(i=>i==null)<2){QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="store"})});return true;}
            int count=Math.Min(need,p.Items[right].Stack);QueuePursuit(pursuit,new[]{("player.forge",(object)new{left_slot=left,right_slot=right,count,budget_shards=20*count})});return true;
        }
        if(id.StartsWith("arcade:")||id is "platform-condition:Achievement_PrairieKing" or "platform-condition:Achievement_FectorsChallenge") {
            string mode=id is "arcade:deathless" or "platform-condition:Achievement_FectorsChallenge"?"deathless":id=="arcade:kart"?"progress":"continue";
            string stat=mode=="deathless"?"completedPrairieKingWithoutDying":mode=="progress"?"completedJunimoKart":"completedPrairieKing";
            if(p.stats.Get(stat)>0){PursuitState(pursuit,"waiting","小游戏原生条件已满足；平台成就须独立核验");return true;}
            if(Game1.timeOfDay<1200||Game1.timeOfDay>2000){PursuitState(pursuit,"waiting","街机安排在酒吧开放且有返程时间的时段");return true;}
            QueuePursuit(pursuit,new[]{("player.arcade",(object)new{game=mode=="progress"?"kart":"prairie",mode,seconds=1800,attempts=3})});return true;
        }
        if(id.StartsWith("mastery:")&&int.TryParse(id[8..],out int mastery)) {
            if(StardewValley.Menus.MasteryTrackerMenu.getCurrentMasteryLevel()<=Game1.stats.Get("masteryLevelsSpent")){PursuitState(pursuit,"waiting","需通过原生劳动继续获得精通经验");return true;}
            if(p.Items.Count(i=>i==null)<3)actions.Add(("work.run",new{goal="store"}));
            actions.Add(("player.mastery",new{skill=mastery}));QueuePursuit(pursuit,actions);return true;
        }
        if(id.StartsWith("book:")||id=="achievement:35") {
            string[] missing=id.StartsWith("book:")?new[]{id[5..]}:JsonSerializer.SerializeToElement(AchievementRules.Native(35)).GetProperty("details").GetProperty("missing").EnumerateArray().Select(v=>"(O)"+v.GetString()).ToArray();
            int slot=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]!=null&&missing.Contains(p.Items[i].QualifiedItemId),-1);
            if(slot>=0){QueuePursuit(pursuit,new[]{("player.read_book",(object)new{slot,item=p.Items[slot].QualifiedItemId})});return true;}
            string? stored=SharedStorage().SelectMany(s=>s.Chest.GetItemsForPlayer()).FirstOrDefault(i=>i!=null&&missing.Contains(i.QualifiedItemId))?.QualifiedItemId;
            if(stored!=null){QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="withdraw",item=stored,count=1})});return true;}
            PursuitState(pursuit,"waiting","尚未拥有目标书籍，需采购或探索取得后继续原生阅读");return true;
        }
        if(id.StartsWith("house:")&&int.TryParse(id[6..],out int house))return PursueHouse(pursuit,house);
        if(id is "achievement:18" or "achievement:19")return PursueHouse(pursuit,id=="achievement:18"?1:2);
        if(id.StartsWith("boat:")) {
            var part=id[5..];var requirement=part switch{"hull"=>("(O)709",200,0),"anchor"=>("(O)337",5,0),"ticket_machine"=>("(O)787",5,0),_=>("",0,0)};
            if(requirement.Item1.Length==0)return false;
            if(!PursuitMaterials(pursuit,new[]{requirement},actions))return true;
            actions.Add(("player.repair_boat",new{part}));QueuePursuit(pursuit,actions);return true;
        }
        if(id.StartsWith("joja:")) {
            if(policy.Route!="joja"){PursuitState(pursuit,"waiting","需明确选择 Joja 路线后才执行建设付款");return true;}
            var progress=Facts.Goals.First(g=>g.Id==id);int cost=progress.Gold;
            if(p.mailForTomorrow.Any(m=>m.StartsWith("joja",StringComparison.Ordinal))){PursuitState(pursuit,"waiting","等待上一项 Joja 建设过夜完成");return true;}
            QueuePursuit(pursuit,new[]{("player.joja",(object)new{route="joja",mode="project",project=id[5..],budget=cost,keep_gold=policy.KeepGold})},cost);return true;
        }
        if(id.StartsWith("bundle:")) {
            if(p.hasOrWillReceiveMail("JojaMember")){PursuitState(pursuit,"blocked","原生Joja路线无法继续社区中心献祭");return true;}
            var bundle=Facts.Bundles.First(b=>"bundle:"+b.Id==id);int remaining=Math.Max(0,bundle.RequiredSlots-bundle.CompletedSlots);
            var selected=bundle.Missing.OrderBy(n=>n.Missing>0).ThenBy(n=>n.Quality).ThenBy(n=>n.Missing).Take(remaining).ToArray();
            int cost=selected.Where(n=>n.Item is "-1" or "(O)-1").Sum(n=>n.Count);
            if(cost>policy.BudgetPerDay-policy.ReservedGold||cost>0&&p.Money-cost<policy.KeepGold){PursuitState(pursuit,"waiting","献祭金库预算不足");return true;}
            if(!PursuitMaterials(pursuit,selected.Where(n=>n.Item is not ("-1" or "(O)-1")).Select(n=>(n.Item,n.Count,n.Quality)),actions))return true;
            actions.Add(("player.bundle",new{bundle=int.Parse(bundle.Id),budget=cost,keep_gold=policy.KeepGold}));QueuePursuit(pursuit,actions,cost);return true;
        }
        if(id=="platform-condition:Achievement_TheBottom") {
            if(p.deepestMineLevel>=120){PursuitState(pursuit,"waiting","矿底原生条件已满足，平台成就独立核验");return true;}
            if(p.health<60||p.Stamina<60||Game1.timeOfDay>1500){PursuitState(pursuit,"waiting","矿洞行程需要充足生命/体力及白天时间");return true;}
            QueuePursuit(pursuit,new[]{("work.run",(object)new{goal="mine_trip",target_level=Math.Min(120,(p.deepestMineLevel/5+1)*5),until=2100})});return true;
        }
        if(id is "achievement:5" or "achievement:28") {
            var museum=Game1.RequireLocation<LibraryMuseum>("ArchaeologyHouse");
            if(!p.Items.Any(i=>i!=null&&museum.isItemSuitableForDonation(i))){PursuitState(pursuit,"waiting","背包暂无未捐馆藏，需继续采矿/开矿球/探索获得");return true;}
            QueuePursuit(pursuit,new[]{("player.service",(object)new{location="ArchaeologyHouse",service="museum_donate"}),("player.donate_museum",(object)new{count=0})});return true;
        }
        if(id.StartsWith("quest:")) {
            var quest=NativeQuestIdentity.Find(id[6..]);return quest!=null&&PursueQuest(pursuit,quest);
        }
        if(id.StartsWith("ship:")||id is "achievement:31" or "achievement:32" or "achievement:34") {
            var needs=new List<(string Item,int Count,int Quality)>();
            if(id.StartsWith("ship:"))needs.Add((id[5..],1,0));
            else if(id=="achievement:34")needs.AddRange(ShippingCollection().Where(o=>!p.basicShipped.ContainsKey(o.ItemId)).Select(o=>(o.QualifiedItemId,1,0)));
            else {
                bool poly=id=="achievement:31";var crops=Game1.cropData.Values.Where(c=>poly?c.CountForPolyculture:c.CountForMonoculture).DistinctBy(c=>c.HarvestItemId);
                needs.AddRange(crops.Select(c=>("(O)"+c.HarvestItemId,Math.Max(0,(poly?15:300)-p.basicShipped.GetValueOrDefault(c.HarvestItemId)),0)).Where(n=>n.Item2>0));
                if(!poly)needs=needs.OrderBy(n=>Math.Max(0,n.Count-Facts.Stock.Where(s=>s.Item==n.Item).Sum(s=>s.Count))).ToList();
            }
            var bin=Game1.getFarm().getShippingBin(p);needs=needs.Select(n=>(n.Item,Count:Math.Max(0,n.Count-bin.Where(i=>i?.QualifiedItemId==n.Item).Sum(i=>i.Stack)),n.Quality)).Where(n=>n.Count>0).OrderByDescending(n=>Facts.Stock.Where(s=>s.Item==n.Item).Sum(s=>s.Count)).ToList();
            if(needs.Count==0){PursuitState(pursuit,"waiting","已出货物等待过夜原生结算");return true;}
            var feasible=needs.Where(n=>DisposableStock(n.Item,n.Quality)>0||CanPreparePursuitItem(n.Item,Math.Min(n.Count,24),n.Quality)).ToArray();
            if(feasible.Length==0){PursuitState(pursuit,"waiting","缺可出货余量或本季可执行材料来源；保留献祭/任务需要的数量后继续其它工作");return true;}
            var need=feasible[0];int available=DisposableStock(need.Item,need.Quality);
            if(available==0){PursuitMaterials(pursuit,new[]{(need.Item,Math.Min(999,Math.Min(need.Count,24)+DisposableStock(need.Item,need.Quality,false)),need.Quality)},actions);if(actions.Count>0)QueuePursuit(pursuit,actions);return true;}
            need.Count=Math.Min(999,Math.Min(need.Count,available));
            if(!PursuitMaterials(pursuit,new[]{need},actions))return true;
            actions.Add(("player.ship_items",new{items=new[]{new{item=need.Item,count=need.Count}}}));QueuePursuit(pursuit,actions);return true;
        }
        return false;
    }
    private static IEnumerable<StardewValley.ItemTypeDefinitions.ParsedItemData> ShippingCollection()=>ItemRegistry.GetObjectTypeDefinition().GetAllData().Where(d=>d.Category is not (-7 or -2)&&StardewValley.Object.isPotentialBasicShipped(d.ItemId,d.Category,d.ObjectType));
}
