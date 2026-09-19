using StardewValley;

namespace Together;
public sealed partial class ModEntry {
    private string FailureConditions(string actor,string tool, System.Text.Json.JsonElement args,string reason="") {
        try {
            var origin=AgentMapOrigin(actor);var l=origin.Location;var p=Game1.player;
            if(tool=="player.sleep") {
                RefreshFacts(true);
                return FailureKnowledge.Hash(AgentJson.Encode(new{day=Game1.Date.TotalDays,period=Game1.timeOfDay/100,energy=(int)p.Stamina/10,Facts.DryCrops,Facts.RipeCrops,Facts.FeedNeeded,reviewed=dayReviewed==Game1.Date.TotalDays}));
            }
            if(reason.StartsWith("social_basis_required"))return FailureKnowledge.Hash(AgentJson.Encode(new{social=SocialObservation.Read(p),basis=SocialBasis(args),day=Game1.Date.TotalDays}));
            if(reason=="native_shop_not_open")return Game1.activeClickableMenu?.GetType().Name??"none";
            bool collection=tool=="work.run"&&AgentToolRegistry.Text(args,"goal") is "wood" or "stone" or "fiber" or "resource" or "forage";
            if(collection&&PlayerExecutor.LoadedLocation(AgentToolRegistry.Text(args,"location",l.NameOrUniqueName)) is {} target)l=target;
            string family=FailureKnowledge.Family(reason);
            if(family=="access") {
                var npc=tool=="player.social"?Game1.getCharacterFromName(AgentToolRegistry.Text(args,"npc")):null;
                string destination=npc?.currentLocation?.NameOrUniqueName??AgentToolRegistry.Text(args,"location",l.NameOrUniqueName);
                var window=ServiceWindow(destination);
                return FailureKnowledge.Hash(AgentJson.Encode(new{day=Game1.Date.TotalDays,destination,window.Reason,open_now=Game1.timeOfDay>=window.Open&&Game1.timeOfDay<window.Close,
                    npc=npc?.Name,npc_tile=npc==null?null:new[]{npc.TilePoint.X,npc.TilePoint.Y},sleeping=npc?.isSleeping.Value,invisible=npc?.IsInvisible,
                    friendship=npc==null?0:p.getFriendshipHeartLevelForNPC(npc.Name),p.HasTownKey,
                    origin=l.NameOrUniqueName,obstacles=l.objects.Pairs.Select(o=>new{o.Key,o.Value.QualifiedItemId}),
                    local_characters=l.characters.Select(n=>new{n.Name,n.TilePoint}),mail=p.mailReceived.ToArray()}));
            }
            string goal=AgentToolRegistry.Text(args,"goal"),item=goal switch{"wood"=>"(O)388","stone"=>"(O)390","fiber"=>"(O)771","hardwood"=>"(O)709",_=>AgentToolRegistry.Text(args,"item")};
            if(family=="material_policy")return FailureKnowledge.Hash(AgentJson.Encode(new{day=Game1.Date.TotalDays,item,owned=item.Length>0?TeamStock(item):0,
                approved=Data.Operating.MaterialTargets.GetValueOrDefault(item),agenda=Data.Autoplay.Agenda.Resources.Where(r=>r.Item==item).Select(r=>r.Count).ToArray()}));
            if(family=="capacity") {
                var capacityActor=actor=="player"?default:WorkActor(actor);
                return FailureKnowledge.Hash(AgentJson.Encode(new{day=Game1.Date.TotalDays,
                    bag=actor=="player"?p.Items.Select(i=>i==null?null:new{id=i.QualifiedItemId,count=i.Stack,quality=i.Quality}).ToArray():null,
                    cargo=capacityActor.ValueKind==System.Text.Json.JsonValueKind.Object?capacityActor.GetProperty("cargo").GetRawText():null,
                    storage=SharedStorage().Select(c=>c.Chest.GetItemsForPlayer().Select(i=>i==null?null:new{id=i.QualifiedItemId,count=i.Stack,quality=i.Quality}).ToArray()).ToArray(),
                    reservations=AllReservations().Select(r=>new{r.Item,r.Count,r.Quality}).ToArray()}));
            }
            if(family=="targets")return FailureKnowledge.Hash(AgentJson.Encode(new{location=l.NameOrUniqueName,
                objects=l.objects.Pairs.Where(o=>goal switch{"wood"=>o.Value.IsTwig(),"stone"=>o.Value.BaseName=="Stone","fiber"=>o.Value.IsWeeds(),"forage"=>o.Value.isForage(),"resource"=>ResourceRules.Nodes.GetValueOrDefault(o.Value.ItemId)==item,_=>true})
                    .OrderBy(o=>o.Key.X).ThenBy(o=>o.Key.Y).Select(o=>new{x=o.Key.X,y=o.Key.Y,id=o.Value.QualifiedItemId}),
                terrain=l.terrainFeatures.Pairs.Where(t=>goal is "wood" or "hardwood"?t.Value is StardewValley.TerrainFeatures.Tree:t.Value is StardewValley.TerrainFeatures.HoeDirt)
                    .Select(t=>new{x=t.Key.X,y=t.Key.Y,kind=t.Value.GetType().Name,ripe=t.Value is StardewValley.TerrainFeatures.HoeDirt dirt&&dirt.readyForHarvest(),water=t.Value is StardewValley.TerrainFeatures.HoeDirt soil?soil.state.Value:-1}),
                topology=l.objects.Pairs.OrderBy(o=>o.Key.X).ThenBy(o=>o.Key.Y).Select(o=>new{x=o.Key.X,y=o.Key.Y,o.Value.QualifiedItemId}),
                obstacles=l.terrainFeatures.Pairs.OrderBy(t=>t.Key.X).ThenBy(t=>t.Key.Y).Select(t=>new{x=t.Key.X,y=t.Key.Y,type=t.Value.GetType().Name}),
                tools=p.Items.OfType<StardewValley.Tool>().Select(t=>new{t.QualifiedItemId,t.UpgradeLevel}),
                cargo=actor=="player"?null:WorkActor(actor).GetProperty("cargo").GetRawText()}));
            // Detached scalar evidence only. Changes invalidate an old failure;
            // nothing from this record overrides a current native precondition.
            return FailureKnowledge.Hash(AgentJson.Encode(new {
                day=Game1.Date.TotalDays,location=l.NameOrUniqueName,tile=collection?null:new[]{origin.Tile.X,origin.Tile.Y},money=p.Money,health=p.health,stamina=(int)p.Stamina,
                menu=Game1.activeClickableMenu?.GetType().Name,
                bag=p.Items.Select(i=>i==null?null:new{id=i.QualifiedItemId,count=i.Stack,quality=i.Quality,water=i is StardewValley.Tools.WateringCan w?w.WaterLeft:-1}),
                objects=l.objects.Pairs.Select(o=>new{x=o.Key.X,y=o.Key.Y,item=o.Value.QualifiedItemId,ready=o.Value.readyForHarvest.Value,held=o.Value.heldObject.Value?.QualifiedItemId}),
                characters=collection?null:l.characters.Select(n=>new{n.Name,tile=new[]{n.TilePoint.X,n.TilePoint.Y}}),
                quests=p.questLog.Select(q=>new{id=NativeQuestIdentity.Id(q),done=q.completed.Value}),
                reservations=Data.Reservations.OrderBy(x=>x.Key)
            }));
        }catch{return "unavailable";}
    }
    private IEnumerable<FailureExperience> ActiveFailureRules() {
        foreach(var e in Data.Autoplay.Failures.Entries.ToArray()) {
            if(e.Reason.StartsWith("no_approved_material_demand")||!e.UntilChanged&&(e.Day!=Game1.Date.TotalDays||DailyBudget.Minutes(Game1.timeOfDay)>=e.RetryAfterMinute)) {Data.Autoplay.Failures.Entries.Remove(e);continue;}
            if(e.Arguments.Length>0) {
                using var args=System.Text.Json.JsonDocument.Parse(e.Arguments);
                string current=FailureConditions(e.Actor,e.Tool,args.RootElement,e.Reason);
                if(current!="unavailable"&&current!=e.Conditions){Data.Autoplay.Failures.Entries.Remove(e);Data.Autoplay.Record("failure_condition_released",AgentJson.Encode(new{e.Key,e.Reason,source=e.TaskEvidence}));continue;}
            }
            yield return e;
        }
    }
    private void CheckKnownFailure(ScheduledAgentTask task) {
        GuardCapacity(task.spec.actor,task.spec.tool,task.spec.args);
        string semantic=FailureKnowledge.ConditionKey(task.spec.actor,task.spec.tool,task.spec.args.GetRawText(),task.spec.location);
        string exact=FailureKnowledge.Key(task.spec.actor,task.spec.tool,task.spec.args.GetRawText(),task.spec.location);
        string selection=FailureKnowledge.SelectionKey(task.spec.actor,task.spec.tool,task.spec.args.GetRawText(),task.spec.location);
        Data.Autoplay.Failures.Entries.RemoveAll(e=>e.Reason.StartsWith("no_approved_material_demand"));
        foreach(var known in Data.Autoplay.Failures.Entries.Where(e=>e.Actor==task.spec.actor&&e.Tool==task.spec.tool&&(e.Key==semantic||e.Key==exact||e.Key==selection||task.spec.tool=="player.social"&&e.Arguments.Length>0&&FailureKnowledge.Key(e.Actor,e.Tool,e.Arguments)==FailureKnowledge.Key(task.spec.actor,task.spec.tool,task.spec.args.GetRawText()))).ToArray()) {
            string conditions=FailureConditions(task.spec.actor,task.spec.tool,task.spec.args,known.Reason);if(conditions=="unavailable")continue;
            var old=Data.Autoplay.Failures.Block(known.Key,conditions,Game1.Date.TotalDays,DailyBudget.Minutes(Game1.timeOfDay));
            if(old!=null){old.Suppressed++;throw new InvalidOperationException("known_failure_conditions_unchanged:"+old.Reason+":evidence="+old.TaskEvidence);}
        }
    }
    private void LearnActionResult(ScheduledAgentTask task,string state,string? error) {
        string key=FailureKnowledge.Key(task.spec.actor,task.spec.tool,task.spec.args.GetRawText(),task.spec.location);
        if(state=="succeeded"){Data.Autoplay.Failures.Success(key);Data.Autoplay.Failures.Success(FailureKnowledge.ConditionKey(task.spec.actor,task.spec.tool,task.spec.args.GetRawText(),task.spec.location));Data.Autoplay.Failures.Success(FailureKnowledge.SelectionKey(task.spec.actor,task.spec.tool,task.spec.args.GetRawText(),task.spec.location));return;}
        if(state!="failed"||string.IsNullOrEmpty(error)||error.StartsWith("known_failure_conditions_unchanged"))return;
        // Capacity constraints already have a versioned authoritative store.
        if(CapacityState.IsConstraint(error))return;
        bool untilChanged=FailureKnowledge.Family(error)!="transient";
        if(untilChanged)key=FailureKnowledge.ConditionKey(task.spec.actor,task.spec.tool,task.spec.args.GetRawText(),task.spec.location);
        string conditions=FailureConditions(task.spec.actor,task.spec.tool,task.spec.args,error);if(conditions=="unavailable")return;
        Data.Autoplay.Failures.Record(key,task.spec.actor,task.spec.tool,error,conditions,task.spec.id,Game1.Date.TotalDays,DailyBudget.Minutes(Game1.timeOfDay),untilChanged);
        var learned=Data.Autoplay.Failures.Entries.Last();learned.Arguments=task.spec.args.GetRawText();learned.Location=task.spec.location;
        Data.Autoplay.Record("failure_experience",AgentJson.Encode(Data.Autoplay.Failures.Entries.Last()));
    }
}
