// Project-owned adapter, compiled only into a local staged Squad checkout.
using System.Runtime.CompilerServices;
using System.Text.Json;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using TheStardewSquad.Framework.Squad;
using TheStardewSquad.Pathfinding;

namespace TheStardewSquad;

public sealed class CompanionControl {
    private readonly ModEntry mod;
    private static CompanionControl? instance;
    private readonly Dictionary<string, Record> records = new();
    private readonly HashSet<string> managed = new();
    private readonly ConditionalWeakTable<StardewValley.Object, Identity> targets = new();
    private sealed class Identity { public string Id = Guid.NewGuid().ToString("N"); }
    public CompanionControl(ModEntry mod) { this.mod = mod; instance = this; }
    private static string Json(object value) => JsonSerializer.Serialize(value);
    private static int[] Tile(Point p) => new[] { p.X, p.Y };
    private static string Id(ISquadMate mate) => $"{Game1.uniqueIDForThisGame}:{mate.RecruiterUniqueId}:{mate.Npc.Name}";
    private string TargetId(StardewValley.Object rock) => targets.GetOrCreateValue(rock).Id;
    private IEnumerable<ISquadMate> Members => mod.SquadManager.Members.Where(m => m.RecruiterUniqueId == Game1.player.UniqueMultiplayerID);
    private ISquadMate Mate(string id) => Members.FirstOrDefault(m => Id(m) == id) ?? throw new InvalidOperationException("actor_not_recruited");
    public static bool IsManaged(ISquadMate mate) => instance?.managed.Contains(Id(mate)) == true;
    public static void BeforeWarp(ISquadMate mate) => instance?.FailActive(mate, "recovery_warp");
    private void FailActive(ISquadMate mate, string error) {
        foreach (var r in records.Values.Where(r => r.Actor == Id(mate) && r.Status == "running")) Finish(r, "failed", error);
    }
    public static void ObserveMine(ISquadMate mate, Point tile, StardewValley.Object rock, GameLocation location) {
        if (instance == null || location.objects.ContainsKey(tile.ToVector2())) return;
        foreach (var r in instance.records.Values.Where(r => r.Status == "running" && r.Actor == Id(mate)
                 && r.Target == tile && ReferenceEquals(r.Rock, rock) && ReferenceEquals(r.Location, location))) {
            r.MinedByActor = true;
        }
    }
    private Point? StandingSpot(ISquadMate mate, Point point) => AStarPathfinder.FindClosestPassableNeighbor(
        mate.Npc.currentLocation, point, mate.Npc, null, validateReachability: true);
    private object[] Candidates(ISquadMate mate) {
        if (mate.Npc.currentLocation != Game1.currentLocation || !mate.CanPerformTask(TaskType.Mining)) return Array.Empty<object>();
        return mate.Npc.currentLocation.objects.Pairs.Where(p => p.Value.BaseName == "Stone"
                && Vector2.Distance(p.Key, mate.Npc.Tile) <= 8 && Vector2.Distance(p.Key, Game1.player.Tile) <= 10)
            .OrderBy(p => Vector2.DistanceSquared(p.Key, mate.Npc.Tile)).Take(16)
            .Where(p => StandingSpot(mate, p.Key.ToPoint()).HasValue)
            .Select(p => (object)new { target_id = TargetId(p.Value), tile = Tile(p.Key.ToPoint()), item_id = p.Value.QualifiedItemId, skill = "mine" }).ToArray();
    }
    private object Actor(ISquadMate mate) {
        Game1.player.friendshipData.TryGetValue(mate.Npc.Name, out var friendship);
        return new { id = Id(mate), name = mate.Npc.Name, display_name = mate.Npc.displayName,
            location = mate.Npc.currentLocation?.NameOrUniqueName, tile = Tile(mate.Npc.TilePoint),
            task = mate.Task?.Type.ToString(), moving = mate.Npc.isMoving(), cooldown = mate.ActionCooldown,
            managed = managed.Contains(Id(mate)), candidates = Candidates(mate),
            relationship = new { points = friendship?.Points ?? 0, dating = friendship?.IsDating() ?? false, married = friendship?.IsMarried() ?? false } };
    }
    public string GetState() => Json(new { backend = "squad", save_id = Game1.uniqueIDForThisGame.ToString(),
        player_id = Game1.player.UniqueMultiplayerID.ToString(), location = Game1.currentLocation.NameOrUniqueName,
        game_time = Game1.timeOfDay, player = new { name = Game1.player.Name, tile = Tile(Game1.player.TilePoint) },
        actors = Members.Select(Actor).ToArray(), commands = records.Values.TakeLast(20).Select(Result).ToArray() });
    public string GetMap(string actorId) {
        var mate = Mate(actorId); var location = mate.Npc.currentLocation;
        int w = location.Map.Layers[0].LayerWidth, h = location.Map.Layers[0].LayerHeight;
        var passable = new List<int[]>();
        for (int y = 0; y < h; y++) for (int x = 0; x < w; x++)
            if (AStarPathfinder.IsTilePassableForFollower(location, new Point(x,y), mate.Npc)) passable.Add(new[] {x,y});
        return Json(new { location = location.NameOrUniqueName, width = w, height = h, passable });
    }
    public string StartAction(string request) {
        if (Context.IsMultiplayer) throw new InvalidOperationException("singleplayer_only");
        using var doc = JsonDocument.Parse(request); var root = doc.RootElement;
        string id = root.GetProperty("command_id").GetString()!;
        if (records.ContainsKey(id)) return PollAction(id);
        string actor = root.GetProperty("actor_id").GetString()!;
        var mate = Mate(actor);
        if (records.Values.Any(r => r.Actor == actor && r.Status == "running")) throw new InvalidOperationException("actor_busy");
        string skill = root.GetProperty("skill").GetString()!;
        if (skill is not ("mine" or "follow")) throw new InvalidOperationException("unsupported_skill");
        var record = new Record { Id = id, Actor = actor, Mate = mate, Skill = skill, Location = mate.Npc.currentLocation,
            Started = DateTime.UtcNow, BeforeTile = Tile(mate.Npc.TilePoint) };
        if (skill == "mine") {
            if (mate.Npc.currentLocation != Game1.currentLocation) throw new InvalidOperationException("different_location");
            if (!mate.CanPerformTask(TaskType.Mining)) throw new InvalidOperationException("unsupported_actor_skill");
            string target = root.GetProperty("target_id").GetString()!;
            var pair = mate.Npc.currentLocation.objects.Pairs.FirstOrDefault(p => TargetId(p.Value) == target);
            if (pair.Value == null || pair.Value.BaseName != "Stone") throw new InvalidOperationException("stale_target");
            if (Vector2.Distance(pair.Key, mate.Npc.Tile) > 8 || Vector2.Distance(pair.Key, Game1.player.Tile) > 10)
                throw new InvalidOperationException("target_out_of_range");
            var spot = StandingSpot(mate, pair.Key.ToPoint()) ?? throw new InvalidOperationException("unreachable");
            if (records.Values.Any(r => r.Status == "running" && ReferenceEquals(r.Rock, pair.Value))) throw new InvalidOperationException("target_claimed");
            record.Target = pair.Key.ToPoint(); record.Rock = pair.Value; record.TargetId = target;
            managed.Add(actor);
            mod.FollowerManager.ClearMateTaskAndReset(mate);
            mate.IsCatchingUp = false;
            mod.FollowerManager.AssignAgentTask(mate, new SquadTask(TaskType.Mining, record.Target, spot, isManual: true));
            record.Assigned = mate.Task;
        } else {
            managed.Add(actor);
            mod.FollowerManager.ClearMateTaskAndReset(mate);
            Finish(record, "succeeded"); // Result is switching mode, not an assertion of arrival.
        }
        records[id] = record;
        return Json(Result(record));
    }
    private void Finish(Record r, string status, string? error = null) {
        if (ReferenceEquals(r.Mate.Task, r.Assigned)) mod.FollowerManager.ClearMateTaskAndReset(r.Mate);
        r.Status = status; r.Error = error;
        r.AfterTile = Tile(r.Mate.Npc.TilePoint);
        r.TargetRemaining = r.Rock != null && r.Location.objects.ContainsKey(r.Target.ToVector2());
    }
    public string PollAction(string id) {
        if (!records.TryGetValue(id, out var r)) return Json(new { command_id = id, status = "unknown" });
        if (r.Status == "running") {
            if (!Members.Contains(r.Mate)) Finish(r, "failed", "actor_dismissed");
            else if (r.Mate.Npc.currentLocation != r.Location || r.Location != Game1.currentLocation) Finish(r, "failed", "location_changed");
            else if (r.MinedByActor && !r.Mate.IsOnCooldown()) Finish(r, "succeeded");
            else if (!r.MinedByActor && (!r.Location.objects.TryGetValue(r.Target.ToVector2(), out var rock) || !ReferenceEquals(rock, r.Rock))) Finish(r, "failed", "target_changed_without_actor_evidence");
            else if (!ReferenceEquals(r.Mate.Task, r.Assigned) && !r.MinedByActor) Finish(r, "failed", "task_interrupted");
            else if ((DateTime.UtcNow - r.Started).TotalSeconds > 30) Finish(r, "failed", "task_timeout");
        }
        return Json(Result(r));
    }
    public string CancelAction(string id) {
        if (!records.TryGetValue(id, out var r)) throw new InvalidOperationException("unknown_command");
        PollAction(id);
        if (r.Status == "running") { r.Cancel = true; Finish(r, r.MinedByActor ? "succeeded" : "cancelled"); }
        return Json(Result(r));
    }
    private object Result(Record r) => new { command_id = r.Id, actor_id = r.Actor, skill = r.Skill,
        status = r.Status, error = r.Error, cancellation_requested = r.Cancel,
        evidence = new { target_id = r.TargetId, target = Tile(r.Target),
            actor_before = r.BeforeTile, actor_after = r.AfterTile ?? Tile(r.Mate.Npc.TilePoint),
            mined_by_actor = r.MinedByActor, target_remaining = r.TargetRemaining ?? (r.Rock != null && r.Location.objects.ContainsKey(r.Target.ToVector2())),
            follow_mode_enabled = r.Skill == "follow" && r.Status == "succeeded" } };
    public void Reset() {
        foreach (var r in records.Values.Where(r => r.Status == "running").ToArray()) Finish(r, "cancelled", "session_reset");
        records.Clear(); managed.Clear();
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
            var npc = Game1.getCharacterFromName(spec.Item1) ?? throw new InvalidOperationException("lab_npc_missing");
            var mate = mod.SquadManager.GetMember(npc) ?? mod.SquadMateFactory.Create(npc);
            mod.FollowerManager.ClearMateTaskAndReset(mate);
            Game1.warpCharacter(npc, farm, new Vector2(spec.Item2,spec.Item3));
            mod.RecruitmentManager.Recruit(mate, Game1.player, isSilent: true);
        }
        foreach (var tile in new[] { new Vector2(49,18), new Vector2(50,21), new Vector2(52,19) }) {
            var rock = new StardewValley.Object("343",1) { TileLocation = tile }; rock.minutesUntilReady.Value = 1;
            farm.objects[tile] = rock;
        }
        return Json(new { fixture = "CompanionLab-v1", actors = 2, rocks = 3, note = "Explicit test setup; actions use Squad mining and pathfinding." });
    }
    private sealed class Record {
        public string Id = "", Actor = "", Skill = "", Status = "running", TargetId = "";
        public string? Error; public ISquadMate Mate = null!; public GameLocation Location = null!;
        public StardewValley.Object? Rock; public SquadTask? Assigned; public Point Target;
        public int[] BeforeTile = Array.Empty<int>(); public int[]? AfterTile; public bool? TargetRemaining;
        public DateTime Started; public bool MinedByActor, Cancel;
    }
}
