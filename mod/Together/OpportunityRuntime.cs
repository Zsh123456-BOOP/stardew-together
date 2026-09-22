using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Xna.Framework;
using StardewValley;
using StardewValley.Buildings;
using StardewValley.Menus;
using StardewValley.Quests;
using StardewValley.TerrainFeatures;
namespace Together;
public sealed partial class ModEntry {
    private List<OperatingOpportunity> lastOpportunities=new();
    private static string OpportunityFamily(OperatingOpportunity r)=>r.Id.StartsWith("sell:")?"monetize":r.Id.StartsWith("plan:owned")||r.Id.StartsWith("inspect:seed")||r.Id=="select:seeds"?"expand":r.Id.StartsWith("accept:")||r.Id=="open:quest-board"||r.Id=="inspect:quest-board"?"quest":r.Tool.StartsWith("knowledge.")?"knowledge":r.Id.StartsWith("advance:")?"goal":r.Id.Split(':')[0];
    private bool OpportunityFeasible(OperatingOpportunity row) {
        var a=JsonSerializer.SerializeToElement(row.Args);string subject=ServiceSubject(row.Tool,a);
        if(subject.Length>0) {
            var window=ServiceWindow(subject);
            if(window.Reason!="available"||Game1.timeOfDay<window.Open||Game1.timeOfDay>=window.Close||Data.Autoplay.Operations.Blocking(subject,window.Conditions,Game1.Date.TotalDays,Game1.timeOfDay)!=null)return false;
        }
        try{if(CapacityAdmission(row.Tool,a) is {Feasible:false})return false;}catch(InvalidOperationException){return false;}
        string location=AgentToolRegistry.Text(a,"location");
        if(location.Length>0&&location!=Game1.currentLocation.NameOrUniqueName&&PlayerExecutor.NextExit(Game1.currentLocation,location)==null)return false;
        if(row.Tool=="player.service"&&PlayerExecutor.ServiceParameterError(a)!=null)return false;
        if(row.Tool=="player.social"&&(SocialBasis(a)==null||SocialPreflight(a)!=null))return false;
        // Observation must not increment retry/suppression counters or acquire leases.
        foreach(var e in Data.Autoplay.Failures.Entries.Where(e=>e.Tool==row.Tool&&e.Arguments.Length>0)) {
            if(FailureKnowledge.ConditionKey("player",row.Tool,a.GetRawText(),"")!=FailureKnowledge.ConditionKey("player",e.Tool,e.Arguments,e.Location))continue;
            if(FailureConditions("player",row.Tool,a,e.Reason)==e.Conditions)return false;
        }
        if(Game1.activeClickableMenu!=null&&row.Tool is not ("shop.read" or "quest_board.read" or "player.accept_quest" or "farm.select_seeds")&&!row.Tool.StartsWith("knowledge.")&&!row.Tool.StartsWith("progress.")&&!row.Tool.StartsWith("goal.requirements"))return false;
        return true;
    }
    private List<object> ExpansionLand() {
        var farm=Game1.getFarm();var cells=new List<object>();
        // This is evidence of accessible empty land, not a replacement layout algorithm.
        for(int y=0;y<farm.Map.Layers[0].LayerHeight;y++)for(int x=0;x<farm.Map.Layers[0].LayerWidth;x++) {
            var tile=new Point(x,y);var v=new Vector2(x,y);farm.terrainFeatures.TryGetValue(v,out var feature);
            if(feature is not null and not HoeDirt {crop:null}||farm.objects.ContainsKey(v)||IsPlacementProtected("Farm",tile)||farm.doesTileHaveProperty(x,y,"Diggable","Back")==null||farm.doesTileHaveProperty(x,y,"NoSpawn","Back")=="All"||farm.doesTileHaveProperty(x,y,"Action","Buildings")!=null||farm.doesTileHaveProperty(x,y,"TouchAction","Back")!=null||!PlayerExecutor.Passable(farm,tile))continue;
            var zone=CleanupArea(new(x,y));if(zone.Zone is "roads" or "pasture" or "woodland" or "reserve")continue;
            cells.Add(new{x,y,tilled=feature is HoeDirt});
        }
        return cells;
    }
    private object ObservedSeedBasis()=>selectionDay==Game1.Date.TotalDays&&selectionEpoch==agentSaveEpoch?(object)new{source="current_day_native_quote",offers=selectionOffers}:new{source="quote_not_observed",next="shop.read 提供原生价格、生长期、回报、照料成本与可买数量；不凭静态价格购买"};
    private void AddDevelopmentOpportunities(List<OperatingOpportunity> rows) {
        var p=Game1.player;var farm=Game1.getFarm();
        if(Game1.activeClickableMenu==null) {
            var sale=BusinessSaleStock();var bin=farm.buildings.OfType<ShippingBin>().FirstOrDefault(b=>b.daysOfConstructionLeft.Value<=0);
            if(sale.Length>0&&bin!=null&&(farm==Game1.currentLocation||PlayerExecutor.NextExit(Game1.currentLocation,"Farm")!=null)) {
                Point tile=new(bin.tileX.Value,bin.tileY.Value);var stand=WorkStand(farm,tile);
                if(stand.HasValue) {
                    var steps=BusinessShipment();if(steps.Count>0) {
                        var first=steps[0];var path=Game1.currentLocation==farm?PlayerExecutor.PreviewPath(farm,stand.Value):null;
                        if(Game1.currentLocation!=farm||path!=null||p.TilePoint==stand.Value)rows.Add(new("sell:surplus","把真实可售余量投入出货箱，原生收入次日到账；取货后继续出货",first.Tool,first.Args,"native_sellable_stock;protected_stock_excluded;shipping_bin_present",0,Game1.currentLocation==farm?20:50,new{items=sale,estimated_total_upper=sale.Sum(s=>(long)s.count*s.price),price_note="品质影响实际总价；上限估计，不是已到账现金",bin=new{location="Farm",x=tile.X,y=tile.Y,local_path_steps=path?.Count,cross_map=Game1.currentLocation!=farm},next_steps=steps.Select(s=>new{tool=s.Tool,args=s.Args})}));
                    }
                }
            }
            var quest=Game1.questOfTheDay;
            if(quest!=null&&!quest.accepted.Value&&!p.questLog.Contains(quest)&&PlayerExecutor.ServiceParameterError(JsonSerializer.SerializeToElement(new{location="Town",service="daily_quests"}))==null)
                rows.Add(new("open:quest-board","查看今天原生任务板，比较实际要求与奖励后决定是否接取","player.service",new{location="Town",service="daily_quests"},AgentJson.Encode(new{id=NativeQuestIdentity.Id(quest),condition=quest.currentObjective,reward_gold=quest.moneyReward.Value,deadline=PlayerExecutor.DailyQuestTiming(quest),accepted=false,next="quest_board.read 然后按现场 id 接取"}),0,30));
            foreach(var q in p.questLog.Where(q=>q.ShouldDisplayAsComplete()&&q.HasMoneyReward()).Take(2))rows.Add(new("advance:reward:"+NativeQuestIdentity.Id(q),"领取已完成任务的真实奖励","player.claim_reward",new{quest_id=NativeQuestIdentity.Id(q)},$"native_complete;reward={q.moneyReward.Value}",0,1));
            foreach(var q in p.questLog.OfType<SocializeQuest>().Where(q=>!q.completed.Value)) {
                var npc=Game1.currentLocation.characters.Where(n=>q.whoToGreet.Contains(n.Name)).OrderBy(n=>Vector2.DistanceSquared(n.Tile,p.Tile)).FirstOrDefault(n=>WorkStand(n.currentLocation,n.TilePoint)!=null);
                if(npc!=null)rows.Add(new("advance:quest:"+NativeQuestIdentity.Id(q),"顺路推进介绍任务，向还未认识的当前地图村民打招呼","player.social",new{npc=npc.Name,mode="greet",quest_id=NativeQuestIdentity.Id(q)},$"native_quest={NativeQuestIdentity.Id(q)};whoToGreet_contains={npc.Name};same_map;reachable_stand",0,10));
            }
            foreach(var q in p.questLog.OfType<ItemDeliveryQuest>().Where(q=>!q.completed.Value).Take(3)) {
                var npc=Game1.getCharacterFromName(q.target.Value);if(npc?.currentLocation==null)continue;
                int slot=Enumerable.Range(0,p.Items.Count).FirstOrDefault(i=>p.Items[i]!=null&&q.OnItemOfferedToNpc(npc,p.Items[i],true),-1);
                if(slot>=0)rows.Add(new("advance:delivery:"+NativeQuestIdentity.Id(q),"把已具备且原生任务接受的物品交给任务对象","player.social",new{npc=npc.Name,mode="deliver",quest_id=NativeQuestIdentity.Id(q),slot},$"quest={NativeQuestIdentity.Id(q)};native_delivery_probe=true;carried_slot={slot};recipient_location={npc.currentLocation.NameOrUniqueName}",0,30));
            }
            foreach(var g in Data.SharedGoals.Where(g=>g.Status=="active"&&!g.AutoExecute).Take(3)) {
                var batch=AgentGoalPrepare(JsonSerializer.SerializeToElement(new{id=g.Id}));
                if(batch.tasks.Count>0) {
                    var t=batch.tasks[0];var step=new OperatingOpportunity("advance:goal:"+g.Id,g.Purpose,t.tool,t.args,"native_dependency_ready",0,20);
                    if(OpportunityFeasible(step))rows.Add(new("advance:goal:"+g.Id,"继续目标的当前可执行依赖步骤","goal.run",new{id=g.Id},"goal="+g.Id+";next="+t.tool,0,20,new{next_action=t,missing=batch.gaps}));
                }
            }
        }
        foreach(var failure in Data.Autoplay.Failures.Entries.Where(e=>e.Tool=="player.social"&&e.Reason=="social_access_unavailable").TakeLast(2)) {
            if(!Knowledge.Ready||string.IsNullOrEmpty(failure.Arguments))continue;
            var a=JsonSerializer.Deserialize<JsonElement>(failure.Arguments);string id="npc:"+AgentToolRegistry.Text(a,"npc");
            bool read=Data.Autoplay.Journal.Any(e=>e.Kind=="tool_result"&&e.Text.Contains("knowledge.get")&&e.Text.Contains(id));
            if(!read&&Knowledge.Index.Get(id) is {} entry&&Knowledge.Visible(entry))rows.Add(new("knowledge:access:"+id,"查询受阻人物的原生资料；实时站位仍以services.read为准","knowledge.get",new{id},"native_social_access_blocked;read_before_replanning",0,1));
        }
        // An unresolved native quest requirement is an actual question, not a
        // recommendation to query the whole encyclopedia on every decision.
        foreach(var q in p.questLog.OfType<ItemDeliveryQuest>().Where(q=>!q.completed.Value).Take(2)) {
            string item=ItemRegistry.QualifyItemId(q.ItemId.Value)??q.ItemId.Value;
            if(TeamStock(item)>=q.number.Value)continue;
            if(Knowledge.Ready&&Knowledge.Index.Get(item) is {} entry&&Knowledge.Visible(entry))rows.Add(new("knowledge:quest:"+NativeQuestIdentity.Id(q),"查询当前交付任务缺少物品的真实来源","knowledge.get",new{id=item},$"quest={NativeQuestIdentity.Id(q)};unresolved_item={item};required={q.number.Value};owned={TeamStock(item)}",0,1));
        }
        foreach(var g in Data.SharedGoals.Where(g=>g.Status=="active").Take(3))foreach(var n in g.Nodes.Where(n=>n.ToPrepare>0&&n.Status is "missing" or "locked" or "blocked").Take(1)) {
            string id=Knowledge.Index.Get(n.Item)!=null?n.Item:g.Entity;
            if(Knowledge.Ready&&Knowledge.Index.Get(id) is {} entry&&Knowledge.Visible(entry)&&!rows.Any(r=>r.Tool=="knowledge.get"&&AgentToolRegistry.Text(JsonSerializer.SerializeToElement(r.Args),"id")==id))rows.Add(new("knowledge:dependency:"+g.Id,"查询目标尚未解决的配料或解锁条件","knowledge.get",new{id},$"goal={g.Id};unresolved={n.Item};missing={n.ToPrepare};reason={n.Reason}",0,1));
        }
    }
    private object? SocialBasis(JsonElement args) {
        string name=AgentToolRegistry.Text(args,"npc"),mode=AgentToolRegistry.Text(args,"mode","talk");var npc=Game1.getCharacterFromName(name);if(npc==null)return null;
        string id=AgentToolRegistry.Text(args,"quest_id");
        if(SocialObservation.Satisfied(Game1.player,name,mode,id) is {} satisfied)return new{kind="already_satisfied",reason=satisfied,npc=name};
        var q=Game1.player.questLog.FirstOrDefault(q=>NativeQuestIdentity.Id(q)==id&&!q.completed.Value);
        if(q is SocializeQuest intro&&intro.whoToGreet.Contains(name))return new{kind="quest",quest_id=id,npc=name};
        string? recipient=q switch{ItemDeliveryQuest d=>d.target.Value,ResourceCollectionQuest r=>r.target.Value,FishingQuest f=>f.target.Value,SlayMonsterQuest m=>m.target.Value,_=>null};
        if(mode=="deliver"&&recipient==name)return new{kind="quest_delivery",quest_id=id,npc=name};
        string order=AgentToolRegistry.Text(args,"order");
        if(mode=="order_deliver"&&order.Length>0&&Game1.player.team.specialOrders.Any(o=>o.questKey.Value==order))return new{kind="special_order",order,npc=name};
        var introImplicit=Game1.player.questLog.OfType<SocializeQuest>().FirstOrDefault(q=>!q.completed.Value&&q.whoToGreet.Contains(name));
        if(mode=="talk"&&introImplicit!=null)return new{kind="quest",quest_id=NativeQuestIdentity.Id(introImplicit),npc=name};
        if(npc.isBirthday())return new{kind="birthday",npc=name};
        string item=AgentToolRegistry.Text(args,"item");
        if(mode=="gift"&&Game1.player.Items.FirstOrDefault(i=>i?.QualifiedItemId==item) is StardewValley.Object gift&&npc.getGiftTasteForThisItem(gift) is 0 or 2)return new{kind="gift",npc=name,item,taste=npc.getGiftTasteForThisItem(gift)};
        var target=Data.Autoplay.Campaign.Targets.FirstOrDefault(t=>!t.CompletionObserved&&t.Target=="friendship:"+name);
        return target==null?null:new{kind="relationship_target",npc=name,target=target.Target};
    }
    private JsonElement WithBlockedAlternatives(JsonElement result) {
        if(result.ValueKind!=JsonValueKind.Object)return result;
        string status=AgentToolRegistry.Text(result,"status"),error=AgentToolRegistry.Text(result,"error");
        if(error.Length==0&&status is not ("failed" or "blocked" or "rejected"))return result;
        var obj=JsonNode.Parse(result.GetRawText())!.AsObject();
        try {obj["alternatives"]=JsonSerializer.SerializeToNode(OperatingOpportunities().Take(8),AgentJson.Options);}
        catch(Exception e){obj["alternatives"]=new JsonArray();obj["alternatives_unavailable"]=e.GetType().Name;Data.Autoplay.Record("alternatives_observation_error",e.Message);}
        if(error.StartsWith("parameter_service_location_mismatch")||error is "shop_closed" or "no_reachable_native_service_counter")obj["service_hours"]=JsonSerializer.SerializeToNode(KnownServiceHours());
        if(error.Contains("social")||error.Contains("npc_")||error.Contains("route_access"))obj["social_access"]=JsonSerializer.SerializeToNode(SocialAvailability());
        if(error.Contains("capacity")||error.Contains("inventory")||error.Contains("known_failure"))obj["capacity_recovery"]=JsonSerializer.SerializeToNode(ReadCapacityOptions(),AgentJson.Options);
        obj["protection_reasons"]=JsonSerializer.SerializeToNode(ProtectionReasons());return JsonSerializer.SerializeToElement(obj);
    }
    private string[] ProtectionReasons() {
        var notes=new List<string>();var farm=Game1.getFarm();
        if(farm.terrainFeatures.Pairs.Any(t=>t.Value is Grass&&CleanupArea(new((int)t.Key.X,(int)t.Key.Y)).Zone is not ("crop" or "production" or "roads")))notes.Add("田块、道路外的牧草保留给畜牧；有筒仓时可收干草");
        if(farm.terrainFeatures.Values.Any(t=>t is FruitTree||t is Tree tree&&tree.tapped.Value))notes.Add("果树和已装采集器的树保留，避免损失产能");
        if(AllReservations().Any())notes.Add("任务与生产已预留材料不出售，改计划后才能释放");
        return notes.ToArray();
    }
    private object NightStatus()=>new{time=Game1.timeOfDay,stamina=Game1.player.Stamina,minutes_until_native_passout=Math.Max(0,1560-DailyBudget.Minutes(Game1.timeOfDay)),unfinished=Data.Autoplay.Schedule.Tasks.Count(t=>!t.Terminal),warning=Game1.timeOfDay>=2200,native_passout=2600,no_effective_plan_guard_minutes=SurvivalState.GuardMinute(ReturnReserve()),return_estimate_minutes=ReturnReserve(),consequence="凌晨两点原生昏倒可能损失金钱与次日体力；请计入返家、出货和上床时间"};
    private void RecordOpportunityAdoption(AgentCall call,JsonElement observed) {
        if(call.tool=="plan.submit"&&call.args.TryGetProperty("tasks",out var tasks)&&tasks.ValueKind==JsonValueKind.Array)foreach(var raw in tasks.EnumerateArray()) {
            if(raw.ValueKind!=JsonValueKind.Object||!raw.TryGetProperty("args",out var args)||args.ValueKind!=JsonValueKind.Object)continue;
            string tool=AgentToolRegistry.Text(raw,"tool"),id=AgentToolRegistry.Text(raw,"id");if(tool.Length==0||tool=="plan.submit")continue;
            var actual=Data.Autoplay.Schedule.Tasks.FirstOrDefault(t=>t.spec.id==id);
            RecordOpportunityAdoption(new AgentCall{tool=tool,args=args.Clone()},JsonSerializer.SerializeToElement(new{status=actual?.state??DecisionBarrier.Text(observed,"status"),task_id=actual?.spec.id}));
        }
        var matches=lastOpportunities.Where(r=>r.Tool==call.tool&&JsonSerializer.SerializeToElement(r.Args).EnumerateObject().All(p=>call.args.TryGetProperty(p.Name,out var v)&&v.GetRawText()==p.Value.GetRawText())).Select(r=>new{r.Id,family=OpportunityFamily(r)}).ToArray();
        Data.Autoplay.Record("candidate_adoption",AgentJson.Encode(new{call.tool,args=call.args,candidates=matches,status=DecisionBarrier.Text(observed,"status"),task_id=DecisionBarrier.Text(observed,"task_id"),basis=call.tool=="player.social"?SocialBasis(call.args):null}));
    }
}
