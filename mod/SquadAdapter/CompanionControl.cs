// Project-owned adapter, compiled only into a local staged Squad checkout.
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using TheStardewSquad.Framework;
using TheStardewSquad.Framework.Wrappers;
using TheStardewSquad.Framework.Squad;
using TheStardewSquad.Pathfinding;

namespace TheStardewSquad;

public sealed partial class CompanionControl {
    private readonly ModEntry mod;
    private static CompanionControl? instance;
    private readonly Dictionary<string, Record> records = new();
    private readonly HashSet<string> managed = new();
    private readonly ConditionalWeakTable<object, Identity> targets = new();
    private sealed class Identity { public string Id = Guid.NewGuid().ToString("N"); }
    public CompanionControl(ModEntry mod) { this.mod = mod; instance = this;PatchCargo(); }
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private static int[] Tile(Point p) => new[] { p.X, p.Y };
    private static string Id(ISquadMate mate) => $"{Game1.uniqueIDForThisGame}:{mate.RecruiterUniqueId}:{mate.Npc.Name}";
    private string TargetId(object target) => targets.GetOrCreateValue(target).Id;
    private IEnumerable<ISquadMate> Members => mod.SquadManager.Members.Where(m => m.RecruiterUniqueId == Game1.player.UniqueMultiplayerID);
    private ISquadMate Mate(string id) => Members.FirstOrDefault(m => Id(m) == id) ?? throw new InvalidOperationException("actor_not_recruited");
    public static bool IsManaged(ISquadMate mate) => instance?.managed.Contains(Id(mate)) == true;
    public static bool IsManagedNpc(NPC npc) => instance?.Members.Any(m=>ReferenceEquals(m.Npc,npc) && IsManaged(m))==true;
    public static bool IsIndependentFishing(ISquadMate mate) => instance?.records.Values.Any(r => r.Actor == Id(mate) && r.Skill == "fish" && r.Status == "running") == true;
    public static void ObserveFish(ISquadMate mate, Item fish) {
        if (instance == null) return;
        foreach (var r in instance.records.Values.Where(r => r.Actor == Id(mate) && r.Skill == "fish" && r.Status == "running"))
            r.Catches.Add(fish.QualifiedItemId);
    }
    public static bool WaitForImpact(ISquadMate mate) {
        var self=instance;
        var r=self?.records.Values.FirstOrDefault(x=>x.Actor==Id(mate) && x.Status=="running" && ReferenceEquals(x.Assigned,mate.Task));
        if(r==null || r.Skill is not ("mine" or "water" or "harvest" or "pet") || !self!.PendingEffect(r))return false;
        if(r.WindupSeconds==0) {
            mate.Npc.faceGeneralDirection(r.Target.ToVector2()*64+new Vector2(32,32));
            if(r.Skill=="mine")TaskManager.AnimateMining(mate.Npc);
            else if(r.Skill=="water")TaskManager.AnimateWatering(mate.Npc);
            else mate.Npc.shake(180);
        }
        r.WindupSeconds+=Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
        if(r.WindupSeconds<.18)return true;
        r.WindupSeconds=0;return false;
    }
    public static bool BeforeTask(ISquadMate mate) => instance?.records.Values.Any(r => r.Actor == Id(mate)
        && r.Status == "running" && ReferenceEquals(r.Assigned,mate.Task) && instance.PendingEffect(r)) == true;
    public static void AfterTask(ISquadMate mate, bool wasPending) {
        if (!wasPending || instance == null) return;
        foreach (var r in instance.records.Values.Where(r => r.Actor == Id(mate) && r.Status == "running"
                 && ReferenceEquals(r.Assigned,mate.Task) && !instance.PendingEffect(r))) r.EffectByActor = true;
    }
    public static bool BlockRecoveryWarp(ISquadMate mate) {
        if(!IsManaged(mate))return false;
        if(!instance!.records.Values.Any(r=>r.Actor==Id(mate) && r.Status=="running" && r.ActiveSeconds<8))instance.FailActive(mate,"path_stalled");mate.Path.Clear();mate.Halt();mate.StuckCounter=0;
        return true;
    }
    private void FailActive(ISquadMate mate, string error) {
        foreach (var r in records.Values.Where(r => r.Actor == Id(mate) && r.Status == "running")) Finish(r, "failed", error);
    }
    public static void ObserveMine(ISquadMate mate, Point tile, StardewValley.Object rock, GameLocation location) {
        if (instance == null || location.objects.ContainsKey(tile.ToVector2())) return;
        foreach (var r in instance.records.Values.Where(r => r.Status == "running" && r.Actor == Id(mate)
                 && r.Target == tile && ReferenceEquals(r.Rock, rock) && ReferenceEquals(r.Location, location))) {
            r.MinedByActor = true;
            r.EffectByActor = true;
        }
    }
    private record Candidate(string Id, string Skill, Point Tile, object Source, Point Stand);
    private IEnumerable<Candidate> FindCandidates(ISquadMate mate) {
        foreach(var candidate in EconomyCandidates(mate).Take(8))yield return candidate;
        foreach(var candidate in ProductionCandidates(mate).Take(12))yield return candidate;
        foreach(var candidate in ResourceCandidates(mate).Take(12))yield return candidate;
        var entries = new List<(Vector2 Tile,object Source,string Skill)>();
        if (mate.CanPerformTask(TaskType.Mining) && Pouch(mate).Count(i=>i!=null)<=8) entries.AddRange(mate.Npc.currentLocation.objects.Pairs
            .Where(p => p.Value.BaseName == "Stone").Select(p => (p.Key,(object)p.Value,"mine")));
        if(mate.CanPerformTask(TaskType.Petting))entries.AddRange(Game1.getFarm().getAllFarmAnimals()
            .Where(a=>a.currentLocation==mate.Npc.currentLocation && !a.wasPet.Value).Select(a=>(a.Tile,(object)a,"pet")));
        entries.AddRange(mate.Npc.currentLocation.objects.Pairs.Where(p=>p.Value.bigCraftable.Value && p.Value.readyForHarvest.Value && p.Value.heldObject.Value!=null && p.Value is not StardewValley.Objects.Chest)
            .Select(p=>(p.Key,(object)p.Value,"collect")));
        foreach (var pair in mate.Npc.currentLocation.terrainFeatures.Pairs) {
            if (pair.Value is not HoeDirt dirt || dirt.crop == null || dirt.crop.dead.Value) continue;
            if (dirt.readyForHarvest() && HasHarvestRoom(mate) && mate.CanPerformTask(TaskType.Harvesting)) entries.Add((pair.Key,dirt,"harvest"));
            else if (dirt.state.Value == HoeDirt.dry && mate.CanPerformTask(TaskType.Watering)) entries.Add((pair.Key,dirt,"water"));
        }
        foreach (var entry in entries.OrderBy(p=>Vector2.DistanceSquared(p.Tile,mate.Npc.Tile)).Take(24)) {
            var spot=StandingSpot(mate,entry.Tile.ToPoint());
            if (spot.HasValue) yield return new Candidate(TargetId(entry.Source)+(entry.Skill=="mine"?"":":"+entry.Skill),entry.Skill,entry.Tile.ToPoint(),entry.Source,spot.Value);
        }
    }
    private object[] Candidates(ISquadMate mate) => FindCandidates(mate).Select(c=>(object)new {
        target_id=c.Id, skill=c.Skill, tile=Tile(c.Tile), item_id=(c.Source as StardewValley.Object)?.QualifiedItemId, expected_items=CandidateOutputs(c) }).ToArray();
    private static string[] CandidateOutputs(Candidate c) {
        if(c.Skill=="harvest" && c.Source is HoeDirt dirt && dirt.crop!=null)return new[]{ItemRegistry.QualifyItemId(dirt.crop.indexOfHarvest.Value)??""};
        if(c.Source is not StardewValley.Object item)return Array.Empty<string>();
        if(c.Skill=="forage")return new[]{item.QualifiedItemId};
        if(c.Skill=="collect" && item.heldObject.Value!=null)return new[]{item.heldObject.Value.QualifiedItemId};
        // Known vanilla resource nodes only. This identifies an attempt, not guaranteed drop quantities.
        if(c.Skill=="mine")return item.ItemId switch {
            "751"=>new[]{"(O)378"},"290"=>new[]{"(O)380"},"764"=>new[]{"(O)384"},"765"=>new[]{"(O)386"},_=>Array.Empty<string>()};
        return Array.Empty<string>();
    }
    private IEnumerable<object> ResourceSites(ISquadMate mate) {
        foreach(string name in Reachable(mate.Npc.currentLocation).Take(12)) {
            if(name==mate.Npc.currentLocation.NameOrUniqueName)continue;
            var location=Game1.getLocationFromName(name);if(location==null)continue;
            int emitted=0;
            foreach(var pair in location.objects.Pairs.Take(256)) {
                var item=pair.Value;string skill=item.BaseName=="Stone"?"mine":item.bigCraftable.Value&&item.readyForHarvest.Value?"collect":item.IsSpawnedObject?"forage":"";
                if(skill=="" || skill=="mine"&&!mate.CanPerformTask(TaskType.Mining))continue;
                var outputs=CandidateOutputs(new("",skill,pair.Key.ToPoint(),item,pair.Key.ToPoint()));if(outputs.Length==0)continue;
                yield return new{location=name,skill,expected_items=outputs};if(++emitted==12)break;
            }
            if(!mate.CanPerformTask(TaskType.Harvesting))continue;
            foreach(var pair in location.terrainFeatures.Pairs.Take(256))if(pair.Value is HoeDirt dirt && dirt.crop!=null && dirt.readyForHarvest()) {
                yield return new{location=name,skill="harvest",expected_items=CandidateOutputs(new("","harvest",pair.Key.ToPoint(),dirt,pair.Key.ToPoint()))};if(++emitted>=16)break;
            }
        }
    }
    private readonly Dictionary<string,(string Location,DateTime Until,bool Available)> fishingAvailability=new();
    private bool FishingAvailable(ISquadMate mate) {
        string id=Id(mate),location=mate.Npc.currentLocation.NameOrUniqueName;
        if(fishingAvailability.TryGetValue(id,out var cached) && cached.Location==location && cached.Until>DateTime.UtcNow)return cached.Available;
        bool available=FishingTask(mate)!=null;fishingAvailability[id]=(location,DateTime.UtcNow.AddSeconds(2),available);return available;
    }
    private object Actor(ISquadMate mate) {
        Game1.player.friendshipData.TryGetValue(mate.Npc.Name, out var friendship);
        return new { id = Id(mate), name = mate.Npc.Name, display_name = mate.Npc.displayName,
            location = mate.Npc.currentLocation?.NameOrUniqueName, tile = Tile(mate.Npc.TilePoint),
            task = mate.Task?.Type.ToString(), moving = mate.Npc.isMoving(), cooldown = mate.ActionCooldown,
            registered_location=mate.Npc.currentLocation is not StardewValley.Locations.MineShaft mine || StardewValley.Locations.MineShaft.activeMines.Contains(mine),
            path_preview=mate.Path.Take(6).Select(Tile).ToArray(),reachable_locations=Reachable(mate.Npc.currentLocation), returning_home=records.Values.Any(r=>r.Actor==Id(mate) && r.Skill=="dismiss" && r.Status=="running"), managed = managed.Contains(Id(mate)), can_reach_beach = mate.Npc.currentLocation.NameOrUniqueName=="Beach" || NextExit(mate.Npc.currentLocation,"Beach")!=null, can_reach_farm = mate.Npc.currentLocation.NameOrUniqueName=="Farm" || NextExit(mate.Npc.currentLocation,"Farm")!=null, candidates = Candidates(mate),
            resource_sites=ResourceSites(mate).ToArray(), fishing_available = FishingAvailable(mate),
            control_mode = stay.Contains(Id(mate)) ? "independent" : "follow",
            cargo = Counts(Pouch(mate)),
            in_combat = mate.Task?.Type == TaskType.Attacking,
            relationship = new { points = friendship?.Points ?? 0, dating = friendship?.IsDating() ?? false, married = friendship?.IsMarried() ?? false } };
    }
    public NPC? GetCharacter(string name)=>Members.FirstOrDefault(m=>m.Npc.Name==name)?.Npc ?? Game1.getCharacterFromName(name);
    public string GetState() => Json(new { performance=MovementPerformance(),backend = "squad", save_id = Game1.uniqueIDForThisGame.ToString(),
        player_id = Game1.player.UniqueMultiplayerID.ToString(), location = Game1.currentLocation.NameOrUniqueName,
        game_time = Game1.timeOfDay, day = Game1.Date.TotalDays, player = new { name = Game1.player.Name, tile = Tile(Game1.player.TilePoint), health=Game1.player.health,inventory=Counts(Game1.player.Items) },
        threats = Game1.currentLocation.characters.OfType<StardewValley.Monsters.Monster>().Where(m=>m.Health>0 && Vector2.Distance(m.Tile,Game1.player.Tile)<=12)
            .Select(m=>new {name=m.Name,tile=Tile(m.TilePoint),health=m.Health}).Take(12).ToArray(),
        actors = Members.Select(Actor).ToArray(), storage = Game1.getFarm().objects.Values.OfType<StardewValley.Objects.Chest>()
            .Where(c=>Role(c)!="none").Select(c=>new{tile=Tile(c.TileLocation.ToPoint()),role=Role(c),items=Counts(c.GetItemsForPlayer(Game1.player.UniqueMultiplayerID))}).ToArray(),
        commands = records.Values.TakeLast(20).Select(Result).ToArray() });
    public string GetMap(string actorId) {
        var mate = Mate(actorId); var location = mate.Npc.currentLocation;
        int w = location.Map.Layers[0].LayerWidth, h = location.Map.Layers[0].LayerHeight;
        var passable = new List<int[]>();
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            if (AStarPathfinder.IsTilePassableForFollower(location, new Point(x,y), mate.Npc)) passable.Add(new[] {x,y});
        var hints=new List<object>();
        for(int y=0;y<h;y++)for(int x=0;x<w;x++) {
            string action=location.doesTileHaveProperty(x,y,"Action","Buildings")??"";
            string npcPass=location.doesTileHaveProperty(x,y,"NPCPassable","Buildings")??"";
            if(action!="" || npcPass!="")hints.Add(new{tile=new[]{x,y},action,npc_passable=npcPass,interior_door=location.interiorDoors.ContainsKey(new Point(x,y))});
        }
        return Json(new { location = location.NameOrUniqueName, width = w, height = h, passable,navigation_hints=hints,exits=Exits(location,"Farm").Select(e=>new{x=e.X,y=e.Y,target=e.TargetName,target_x=e.TargetX,target_y=e.TargetY}) });
    }
    public string StartAction(string request) {
        if (Context.IsMultiplayer) throw new InvalidOperationException("singleplayer_only");
        using var doc = JsonDocument.Parse(request); var root = doc.RootElement;
        string id = root.GetProperty("command_id").GetString()!;
        if (records.ContainsKey(id)) return PollAction(id);
        string actor = root.GetProperty("actor_id").GetString()!;
        string skill = root.GetProperty("skill").GetString()!;
        if(skill=="recruit") {
            var npc=Game1.getCharacterFromName(actor) ?? throw new InvalidOperationException("npc_missing");
            if(npc.currentLocation!=Game1.currentLocation || Vector2.Distance(npc.Tile,Game1.player.Tile)>8 || !mod.BehaviorManager.CanRecruit(npc))
                throw new InvalidOperationException("recruitment_unavailable");
            bool trial=root.TryGetProperty("trial",out var t) && t.GetBoolean();
            if(!trial && !npc.isMarried() && Game1.player.getFriendshipHeartLevelForNPC(npc.Name)<mod.Config.FriendshipRequirement)
                throw new InvalidOperationException("friendship_too_low");
            var recruited=mod.SquadManager.GetMember(npc) ?? mod.SquadMateFactory.Create(npc);
            mod.RecruitmentManager.Recruit(recruited,Game1.player,isSilent:true);
            if(!Members.Contains(recruited)) throw new InvalidOperationException("squad_full");
            managed.Add(Id(recruited));
            var receipt=new Record {Id=id,Actor=Id(recruited),Skill=skill,Mate=recruited,Location=npc.currentLocation,BeforeTile=Tile(npc.TilePoint)};
            Finish(receipt,"succeeded");records[id]=receipt;return Json(Result(receipt));
        }
        var mate = Mate(actor);
        if (records.Values.Any(r => r.Actor == actor && r.Status == "running")) throw new InvalidOperationException("actor_busy");
        if (skill is not ("buy" or "ship" or "clear" or "till" or "plant" or "feed" or "tend" or "forage" or "gift" or "refill" or "deposit" or "travel" or "stay" or "pet" or "collect" or "mine" or "follow" or "water" or "harvest" or "fish" or "guard" or "rest" or "dismiss")) throw new InvalidOperationException("unsupported_skill");
        var record = new Record { Id = id, Actor = actor, Mate = mate, Skill = skill, Location = mate.Npc.currentLocation,
            Started = DateTime.UtcNow, BeforeTile = Tile(mate.Npc.TilePoint) };
        if(skill=="travel") {
            string destination=root.GetProperty("destination").GetString()!;
            if(Game1.getLocationFromName(destination)==null)throw new InvalidOperationException("unknown_destination");
            if(mate.Npc.currentLocation.NameOrUniqueName!=destination && NextExit(mate.Npc.currentLocation,destination)==null)throw new InvalidOperationException("no_route");
            record.Destination=destination;record.Duration=240;managed.Add(actor);stay.Add(actor);
            mod.FollowerManager.ClearMateTaskAndReset(mate);
        } else if(skill=="stay") {
            managed.Add(actor);stay.Add(actor);mod.FollowerManager.ClearMateTaskAndReset(mate);Finish(record,"succeeded");
        } else if (skill is "buy" or "ship" or "clear" or "till" or "plant" or "feed" or "tend" or "forage" or "gift" or "refill" or "deposit" or "mine" or "water" or "harvest" or "pet" or "collect") {
            string target = root.GetProperty("target_id").GetString()!;
            var candidate = FindCandidates(mate).FirstOrDefault(c=>c.Id==target && c.Skill==skill)
                ?? throw new InvalidOperationException("stale_target");
            if (records.Values.Any(r => r.Status == "running" && (ReferenceEquals(r.Source,candidate.Source) || r.Location==mate.Npc.currentLocation && (r.Target==candidate.Tile || r.Stand==candidate.Stand)))) throw new InvalidOperationException("target_claimed");
            record.Target = candidate.Tile; record.Source=candidate.Source; record.Rock=candidate.Source as StardewValley.Object; record.TargetId = target;
            managed.Add(actor);stay.Add(actor);
            mod.FollowerManager.ClearMateTaskAndReset(mate);
            mate.IsCatchingUp = false;
            record.Stand=candidate.Stand;record.Duration=180;
            if(skill=="refill")record.Resources=SupplyFor(mate,(StardewValley.Object)candidate.Source)??throw new InvalidOperationException("supply_changed");
            if(skill=="plant")record.Resources=Ingredient(mate,((Plot)candidate.Source).Seed)??throw new InvalidOperationException("seed_missing");
            if(skill=="feed")record.Resources=Ingredient(mate,"(O)178");
            if(skill is not ("buy" or "ship" or "clear" or "till" or "plant" or "feed" or "tend" or "forage" or "collect" or "gift" or "refill" or "deposit")) {
                var kind=skill=="mine"?TaskType.Mining:skill=="water"?TaskType.Watering:skill=="pet"?TaskType.Petting:TaskType.Harvesting;
                mod.FollowerManager.AssignAgentTask(mate, new SquadTask(kind, record.Target, candidate.Stand, isManual: true));
                record.Assigned = mate.Task;
            }
        } else if (skill is "fish" or "guard" or "rest") {
            record.Duration = root.TryGetProperty("seconds",out var duration) ? Math.Clamp(duration.GetInt32(),1,120) : 30;
            var fishing=skill=="fish" ? FishingTask(mate) ?? throw new InvalidOperationException("no_fishing_spot") : null;
            managed.Add(actor); if(skill!="guard")stay.Add(actor);else stay.Remove(actor); mod.FollowerManager.ClearMateTaskAndReset(mate);
            if (fishing!=null) {
                record.Target=fishing.Tile;
                mod.FollowerManager.AssignAgentTask(mate,new SquadTask(TaskType.Fishing,fishing.Tile,fishing.InteractionTile,isManual:true));
                record.Assigned=mate.Task;
            }
        } else if(skill=="dismiss") {
            var home=mod.RecruitmentManager.GetTargetLocationForNow(mate.Npc);
            record.Destination=home.Item1;record.Target=home.Item2;record.Duration=300;
            managed.Add(actor);stay.Add(actor);mod.FollowerManager.ClearMateTaskAndReset(mate);
        } else {
            managed.Add(actor);stay.Remove(actor);
            mod.FollowerManager.ClearMateTaskAndReset(mate);
            Finish(record, "succeeded"); // Result is switching mode, not an assertion of arrival.
        }
        records[id] = record;
        return Json(Result(record));
    }
    private void Finish(Record r, string status, string? error = null) {
        if (ReferenceEquals(r.Mate.Task, r.Assigned)) mod.FollowerManager.ClearMateTaskAndReset(r.Mate);
        r.Status = status; r.Error = error;reservationTick=-1;
        r.CargoAfter=Counts(Pouch(r.Mate));
        r.AfterTile = Tile(r.Mate.Npc.TilePoint);
        r.TargetRemaining = r.Rock != null && r.Location.objects.ContainsKey(r.Target.ToVector2());
    }
    private bool PendingEffect(Record r) {
        if(r.Skill is "buy" or "ship")return EconomyPending(r);
        if(r.Skill is "clear" or "till" or "plant" or "feed" or "tend" or "forage")return ProductionPending(r);
        if(r.Skill is "gift" or "refill" or "deposit")return ResourcePending(r);
        if (r.Skill=="mine") return r.Location.objects.TryGetValue(r.Target.ToVector2(),out var rock) && ReferenceEquals(rock,r.Source);
        if(r.Skill=="pet")return r.Source is FarmAnimal animal && animal.currentLocation==r.Location && !animal.wasPet.Value;
        if(r.Skill=="collect")return r.Source is StardewValley.Object machine && r.Location.objects.TryGetValue(r.Target.ToVector2(),out var current) && ReferenceEquals(current,machine) && machine.readyForHarvest.Value && machine.heldObject.Value!=null;
        if (r.Source is not HoeDirt dirt || !r.Location.terrainFeatures.TryGetValue(r.Target.ToVector2(),out var feature) || !ReferenceEquals(feature,dirt)) return false;
        return r.Skill=="water" ? dirt.state.Value==HoeDirt.dry : r.Skill=="harvest" && dirt.readyForHarvest();
    }
    public string PollAction(string id) {
        if (!records.TryGetValue(id, out var r)) return Json(new { command_id = id, status = "unknown" });
        if (r.Status == "running") {
            long tick=Game1.currentGameTime.TotalGameTime.Ticks;
            float dt=r.LastTick==tick?0:(float)Game1.currentGameTime.ElapsedGameTime.TotalSeconds;
            r.LastTick=tick;
            if (!Context.IsPlayerFree || (!Game1.game1.IsActive && Game1.options.pauseWhenOutOfFocus)) dt=0;
            bool combat=r.Mate.Task?.Type==TaskType.Attacking;
            if (combat) r.CombatPaused=true;
            else if(r.CombatPaused && r.Assigned==null){r.CombatPaused=false;r.Resumes++;}
            if (!combat && r.Skill!="dismiss") r.ActiveSeconds+=dt;
            if(!combat && r.Skill is "buy" or "ship" or "clear" or "till" or "plant" or "feed" or "tend" or "forage" or "gift" or "refill" or "deposit" or "mine" or "water" or "harvest" or "pet" or "collect") {
                r.StallSeconds=r.LastPosition==r.Mate.Npc.TilePoint && !r.Mate.IsOnCooldown()?r.StallSeconds+dt:0;
                r.LastPosition=r.Mate.Npc.TilePoint;
            }
            if (!Members.Contains(r.Mate)) Finish(r, "failed", "actor_dismissed");
            // Guard follows the Farmer through real exits; unlike a fixed work target,
            // its start map is not a validity constraint.
            else if (r.Skill is not ("travel" or "dismiss" or "guard") && r.Mate.Npc.currentLocation != r.Location) Finish(r, "failed", "location_changed");
            else if (combat) { /* Keep goal while Squad handles fast combat. */ }
            else if(r.Skill=="pet" && r.Source is FarmAnimal animal && animal.TilePoint!=r.Target && !r.EffectByActor)Finish(r,"failed","target_moved");
            else if(r.Skill=="dismiss") {if(r.ActiveSeconds>300)Finish(r,"failed","home_route_unavailable");}
            else if(r.Skill=="travel") {
                if(r.Mate.Npc.currentLocation.NameOrUniqueName==r.Destination)Finish(r,"succeeded");
                else if(r.ActiveSeconds>r.Duration)Finish(r,"failed","route_timeout");
            }
            else if (r.Skill is "guard" or "rest") {
                if(r.Skill=="rest") r.Mate.Halt();
                if(r.ActiveSeconds>=r.Duration) Finish(r,"succeeded");
            }
            else if (r.EffectByActor && !r.Mate.IsOnCooldown()) Finish(r, "succeeded");
            else if (r.Skill!="fish" && !r.EffectByActor && !PendingEffect(r)) Finish(r, "failed", "target_changed_without_actor_evidence");
            else if(r.StallSeconds>8) Finish(r,"failed","path_stalled");
            else {
                if(r.CombatPaused && r.Mate.Task==null) {
                    var spot=StandingSpot(r.Mate,r.Target);
                    if(spot.HasValue && r.Assigned!=null) {
                        var next=new SquadTask(r.Assigned.Type,r.Target,r.Skill=="fish"?r.Assigned.InteractionTile:spot.Value,isManual:true);
                        mod.FollowerManager.AssignAgentTask(r.Mate,next); r.Assigned=next; r.CombatPaused=false; r.Resumes++;
                    } else Finish(r,"failed","unreachable_after_combat");
                }
                if(r.Status=="running" && !ReferenceEquals(r.Mate.Task,r.Assigned) && !r.EffectByActor) Finish(r,"failed","task_interrupted");
                if(r.Status=="running" && r.Skill=="fish" && r.Mate.Npc.TilePoint==r.Assigned?.InteractionTile) {
                    r.FishingSeconds+=dt;
                    if(r.FishingSeconds-r.LastCatch>=10 && !r.Mate.IsOnCooldown()) {
                        r.LastCatch=r.FishingSeconds; using(OwnOutput(r.Mate))TaskManager.TryNpcCatchFish(r.Mate,mod.SquadManager.Count);
                    }
                    if(r.FishingSeconds>=r.Duration) Finish(r,"succeeded");
                }
                if(r.Status=="running" && r.ActiveSeconds>r.Duration+45) Finish(r,"failed","task_timeout");
            }
        }
        return Json(Result(r));
    }
    public string CancelAction(string id) {
        if (!records.TryGetValue(id, out var r)) throw new InvalidOperationException("unknown_command");
        PollAction(id);
        if (r.Status == "running") { r.Cancel = true; Finish(r, r.EffectByActor ? "succeeded" : "cancelled"); }
        return Json(Result(r));
    }
    private object Result(Record r) => new { command_id = r.Id, actor_id = r.Actor, skill = r.Skill,
        status = r.Status, error = r.Error, cancellation_requested = r.Cancel,
        evidence = new { target_id = r.TargetId, target = Tile(r.Target),
            interaction_tile = r.Assigned==null ? null : Tile(r.Assigned.InteractionTile),active_seconds=r.ActiveSeconds,actor_before = r.BeforeTile, actor_after = r.AfterTile ?? Tile(r.Mate.Npc.TilePoint),
            mined_by_actor = r.MinedByActor, target_remaining = r.TargetRemaining ?? (r.Rock != null && r.Location.objects.ContainsKey(r.Target.ToVector2())),
            effect_by_actor=r.EffectByActor, combat_paused=r.CombatPaused, resumes=r.Resumes,
            resource_changes=r.ResourceChanges,pickup_tile=r.PickupTile,cargo=r.CargoAfter??Counts(Pouch(r.Mate)),
            processing_output=r.ProcessingOutput,
            activity_seconds=r.Skill=="fish"?r.FishingSeconds:r.ActiveSeconds, caught_items=r.Catches.ToArray(),
            follow_mode_enabled = r.Skill == "follow" && r.Status == "succeeded" } };
    public void Reset() {
        foreach (var r in records.Values.Where(r => r.Status == "running").ToArray()) Finish(r, "cancelled", "session_reset");
        records.Clear();reachableCache.Clear(); managed.Clear();stay.Clear();fishingAvailability.Clear();resourceReservations.Clear();plotTargets.Clear();farmPolicy=new(){Enabled=false};
    }
    public string PrepareLab() {
        if (Context.IsMultiplayer || Game1.player.Name != "AgentLab") throw new InvalidOperationException("lab_save_required");
        Reset();
        var farm = Game1.getFarm();
        if (Game1.currentLocation != farm) throw new InvalidOperationException("lab_requires_farm");
        var area = new Rectangle(42,16,15,12);
        foreach (var p in farm.objects.Keys.ToArray()) if (area.Contains(p.ToPoint())) farm.objects.Remove(p);
        foreach (var p in farm.terrainFeatures.Keys.ToArray()) if (area.Contains(p.ToPoint())) farm.terrainFeatures.Remove(p);
        foreach (var clump in farm.resourceClumps.ToArray())
            if (new Rectangle(area.X*64,area.Y*64,area.Width*64,area.Height*64).Intersects(clump.getBoundingBox())) farm.resourceClumps.Remove(clump);
        Game1.player.Position = new Vector2(44,23) * 64;
        mod.Config.TasksEnabled = true; mod.Config.EnableCommunication = false; mod.Config.EnableIdleAnimations = false;
        mod.Config.ForagingMode = TaskMode.Disabled; mod.Config.MiningMode = TaskMode.Disabled;
        mod.Config.AttackingMode = TaskMode.Autonomous;
        foreach (var spec in new[] { ("Abigail",43,18), ("Leah",49,17) }) {
            var npc = Members.FirstOrDefault(m=>m.Npc.Name==spec.Item1)?.Npc ?? Game1.getCharacterFromName(spec.Item1) ?? throw new InvalidOperationException("lab_npc_missing");
            var mate = mod.SquadManager.GetMember(npc) ?? mod.SquadMateFactory.Create(npc);
            mod.FollowerManager.ClearMateTaskAndReset(mate);
            Game1.warpCharacter(npc, farm, new Vector2(spec.Item2,spec.Item3));
            mod.RecruitmentManager.Recruit(mate, Game1.player, isSilent: true);
        }
        foreach (var tile in new[] { new Vector2(49,18), new Vector2(50,21), new Vector2(52,19) }) {
            var rock = new StardewValley.Object("343",1) { TileLocation = tile }; rock.minutesUntilReady.Value = 1;
            farm.objects[tile] = rock;
        }
        foreach(var x in new[]{46,47,48,49}) {
            var crop=new Crop("472",x,25,farm);
            if(x>=48)crop.growCompletely();
            farm.terrainFeatures[new Vector2(x,25)]=new HoeDirt(x>=48?HoeDirt.watered:HoeDirt.dry,farm){crop=crop};
        }
        var machine=ItemRegistry.Create<StardewValley.Object>("(BC)12");
        machine.TileLocation=new Vector2(52,25);machine.heldObject.Value=ItemRegistry.Create<StardewValley.Object>("(O)395");machine.readyForHarvest.Value=true;
        farm.objects[machine.TileLocation]=machine;
        foreach(var name in new[]{"Abigail","Leah"})Game1.player.team.GetOrCreateGlobalInventory($"Together_Pouch_{Game1.player.UniqueMultiplayerID}_{name}").Clear();
        var supplies=new StardewValley.Objects.Chest(true){TileLocation=new Vector2(44,26)};
        supplies.modData[ChestRoleKey]="supplies";
        supplies.Items.Add(ItemRegistry.Create("(O)378",10));supplies.Items.Add(ItemRegistry.Create("(O)382",2));
        farm.objects[supplies.TileLocation]=supplies;
        var output=new StardewValley.Objects.Chest(true){TileLocation=new Vector2(50,26)};
        output.modData[ChestRoleKey]="output";farm.objects[output.TileLocation]=output;
        var furnace=ItemRegistry.Create<StardewValley.Object>("(BC)13");furnace.TileLocation=new Vector2(54,25);farm.objects[furnace.TileLocation]=furnace;
        const long testAnimal=-449404282;
        farm.animals.Remove(testAnimal);
        var chicken=new FarmAnimal("White Chicken",testAnimal,Game1.player.UniqueMultiplayerID){Position=new Vector2(51,23)*64};
        chicken.currentLocation=farm;chicken.wasPet.Value=false;farm.animals.Add(testAnimal,chicken);
        return Json(new { fixture = "CompanionLab-v3", actors = 2, rocks = 3, dry_crops=2, mature_crops=2, note = "Explicit test setup; actions use Squad tasks and pathfinding." });
    }
    private sealed class Record {
        public int ShipCount;
        public Item? ShipCargo;
        public ResourceWork? Resources;
        public int[]? PickupTile;
        public Dictionary<string,int> ResourceChanges=new();
        public Dictionary<string,int>? CargoAfter;
        public string? ProcessingOutput;
        public string? Destination;
        public Point Stand;
        public double WorkSeconds;
        public double WindupSeconds;
        public string Id = "", Actor = "", Skill = "", Status = "running", TargetId = "";
        public string? Error; public ISquadMate Mate = null!; public GameLocation Location = null!;
        public object? Source; public StardewValley.Object? Rock; public SquadTask? Assigned; public Point Target;
        public int[] BeforeTile = Array.Empty<int>(); public int[]? AfterTile; public bool? TargetRemaining;
        public DateTime Started; public bool MinedByActor, EffectByActor, Cancel, CombatPaused; public int Resumes;
        public long LastTick=-1; public float ActiveSeconds,FishingSeconds,LastCatch; public int Duration=30;
        public Point LastPosition;public float StallSeconds;
        public List<string> Catches=new();
    }
}
