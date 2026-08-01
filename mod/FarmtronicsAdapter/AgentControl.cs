// New project code compiled into a staged copy of the pinned MIT Farmtronics source.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using Farmtronics.Bot;
using Farmtronics.M1;
using Microsoft.Xna.Framework;
using StardewModdingAPI;
using StardewValley;
using StardewValley.TerrainFeatures;
using StardewValley.Tools;

namespace Farmtronics {
    public class AgentControl {
        private const string IdKey = "StardewAgent/ActorId";
        private readonly Dictionary<string, ActionRecord> records = new();
        private static string Json(object value) => JsonSerializer.Serialize(value);
        private static int[] Tile(BotObject bot) => new[] { (int)bot.TileLocation.X, (int)bot.TileLocation.Y };
        private static IEnumerable<BotObject> Bots => BotManager.instances.Where(b => b.currentLocation == Game1.currentLocation);
        private static string Id(BotObject bot) {
            if (!bot.modData.TryGetValue(IdKey, out string id)) bot.modData[IdKey] = id = Guid.NewGuid().ToString("N");
            return id;
        }
        private static BotObject Bot(string id) => Bots.FirstOrDefault(b => Id(b) == id) ?? throw new InvalidOperationException("actor_not_in_current_location");
        private static object Inventory(BotObject bot) => bot.inventory.Select((item, slot) => item == null ? null : new {
            slot, id = item.QualifiedItemId, name = item.Name, type = item.GetType().Name,
            quantity = item.Stack, water = item is WateringCan w ? (int?)w.WaterLeft : null
        }).Where(i => i != null).ToArray();
        private static object Actor(BotObject bot) => new {
            id = Id(bot), name = bot.Name, tile = Tile(bot), pixels = new[] {bot.Position.X, bot.Position.Y},
            moving = bot.IsMoving(), using_tool = bot.isUsingTool, facing = bot.facingDirection,
            energy = bot.energy, capacity = bot.GetActualCapacity(), inventory = Inventory(bot)
        };
        private static object CropState(GameLocation loc, Vector2 p) {
            var dirt = loc.terrainFeatures.TryGetValue(p, out var t) ? t as HoeDirt : null;
            return new {
                tile = new[] {(int)p.X, (int)p.Y}, has_crop = dirt?.crop != null,
                needs_water = dirt?.crop != null && !dirt.crop.dead.Value && dirt.state.Value != 1,
                harvestable = dirt?.crop != null && !dirt.crop.dead.Value && dirt.readyForHarvest(),
                product = dirt?.crop?.indexOfHarvest.Value,
                dead = dirt?.crop?.dead.Value ?? false
            };
        }
        public string GetState() {
            if (!Context.IsWorldReady) return Json(new {ready = false});
            var loc = Game1.currentLocation;
            return Json(new {
                ready = true, location = loc.NameOrUniqueName, game_time = Game1.timeOfDay,
                player = new {name = Game1.player.Name, tile = new[] {(int)Game1.player.Tile.X, (int)Game1.player.Tile.Y}},
                actors = Bots.Select(Actor).ToArray(),
                crops = loc.terrainFeatures.Pairs.Where(p => p.Value is HoeDirt d && d.crop != null).Select(p => CropState(loc, p.Key)).ToArray(),
                paused = Game1.paused || Game1.activeClickableMenu != null
            });
        }
        public string GetMap(string actorId) {
            var bot = Bot(actorId);
            var loc = bot.currentLocation;
            int width = loc.Map.Layers[0].LayerWidth, height = loc.Map.Layers[0].LayerHeight;
            var passable = new List<int[]>();
            for (int y = 0; y < height; y++) for (int x = 0; x < width; x++) {
                var tile = new Vector2(x, y);
                if (tile == bot.TileLocation || TileInfo.IsPassable(loc, tile)) passable.Add(new[] {x, y});
            }
            return Json(new {location = loc.NameOrUniqueName, width, height, passable});
        }
        public void Reset() { records.Clear(); }
        public string StartAction(string request) {
            using var doc = JsonDocument.Parse(request);
            var r = doc.RootElement;
            string id = r.GetProperty("command_id").GetString();
            if (records.ContainsKey(id)) return PollAction(id);
            string actorId = r.GetProperty("actor_id").GetString();
            var bot = Bot(actorId);
            if (bot.IsMoving() || bot.isUsingTool || records.Values.Any(a => a.Actor == actorId && a.Status == "running")) throw new InvalidOperationException("actor_busy");
            string skill = r.GetProperty("skill").GetString();
            var p = r.GetProperty("target").EnumerateArray().Select(v => v.GetInt32()).ToArray();
            if (p.Length != 2) throw new InvalidOperationException("invalid_target");
            Vector2 target = new(p[0], p[1]);
            var loc = bot.currentLocation;
            if (p[0] < 0 || p[1] < 0 || p[0] >= loc.Map.Layers[0].LayerWidth || p[1] >= loc.Map.Layers[0].LayerHeight) throw new InvalidOperationException("invalid_target");
            Vector2 delta = target - bot.TileLocation;
            if (Math.Abs(delta.X) + Math.Abs(delta.Y) != 1) throw new InvalidOperationException("target_not_adjacent");
            int direction = delta.Y < 0 ? 0 : delta.X > 0 ? 1 : delta.Y > 0 ? 2 : 3;
            if (!new[] {"step", "water", "harvest"}.Contains(skill)) throw new InvalidOperationException("unknown_skill");
            var action = new ActionRecord {Id = id, Actor = actorId, Skill = skill, Target = target, Location = loc,
                Before = new {actor = Actor(bot), crop = CropState(loc, target)}, Started = DateTime.UtcNow};
            bot.facingDirection = direction;
            if (skill == "step") {
                if (!TileInfo.IsPassable(loc, target)) throw new InvalidOperationException("unreachable");
                bot.Move((int)delta.X, (int)delta.Y);
            } else {
                if (!loc.terrainFeatures.TryGetValue(target, out var feature) || feature is not HoeDirt dirt || dirt.crop == null || dirt.crop.dead.Value) throw new InvalidOperationException("target_not_crop");
                if (skill == "water") {
                    if (dirt.state.Value == 1) throw new InvalidOperationException("already_watered");
                    int slot = bot.inventory.ToList().FindIndex(i => i is WateringCan);
                    if (slot < 0) throw new InvalidOperationException("missing_tool");
                    if (((WateringCan)bot.inventory[slot]).WaterLeft <= 0 || bot.energy <= 0) throw new InvalidOperationException("resource_insufficient");
                    bot.currentToolIndex = slot;
                    bot.UseTool();
                } else {
                    if (!dirt.readyForHarvest()) throw new InvalidOperationException("not_harvestable");
                    if (dirt.crop.GetHarvestMethod() != StardewValley.GameData.Crops.HarvestMethod.Grab) throw new InvalidOperationException("unsupported_harvest_method");
                    // Conservative full-inventory guard. Actual receipt is checked separately.
                    if (bot.inventory.Count(i => i != null) >= bot.GetActualCapacity()) throw new InvalidOperationException("inventory_full");
                    action.Product = "(O)" + dirt.crop.indexOfHarvest.Value;
                    action.InitialQuantity = bot.inventory.Where(i => i?.QualifiedItemId == action.Product).Sum(i => i.Stack);
                    bot.shouldPickupDebris = true;
                    // Use native harvest-to-debris behavior, then the existing robot pickup mechanic.
                    // Never swap Game1.player: newer versions dispose a replaced player instance.
                    if (dirt.crop.harvest(p[0], p[1], dirt, null, true)) dirt.destroyCrop(false);
                }
            }
            records[id] = action;
            return PollAction(id);
        }
        public string PollAction(string id) {
            if (!records.TryGetValue(id, out var a)) return Json(new {status = "unknown", command_id = id});
            var bot = Bot(a.Actor);
            if (a.Status == "running" && !bot.IsMoving() && !bot.isUsingTool) {
                var dirt = a.Location.terrainFeatures.TryGetValue(a.Target, out var t) ? t as HoeDirt : null;
                bool success = a.Skill switch {
                    "step" => bot.TileLocation == a.Target && bot.Position == a.Target * Game1.tileSize,
                    "water" => dirt?.state.Value == 1,
                    "harvest" => (dirt?.crop == null || !dirt.readyForHarvest()) && bot.inventory.Where(i => i?.QualifiedItemId == a.Product).Sum(i => i.Stack) > a.InitialQuantity,
                    _ => false
                };
                if (a.Skill == "harvest" && !success && (DateTime.UtcNow - a.Started).TotalSeconds < 8) return Json(new {command_id = id, status = "running", waiting_for = "inventory_receipt"});
                a.Status = success ? "succeeded" : a.Cancel ? "cancelled" : "failed";
                a.Error = success || a.Cancel ? null : "effect_not_observed";
                a.After = new {actor = Actor(bot), crop = CropState(a.Location, a.Target)};
            }
            return Json(new {command_id = id, actor_id = a.Actor, skill = a.Skill, status = a.Status, error = a.Error,
                cancellation_requested = a.Cancel, evidence = new {before = a.Before, after = a.After}});
        }
        public string CancelAction(string id) {
            if (records.TryGetValue(id, out var action)) action.Cancel = true;
            // A step finishes at its next tile boundary; no position changes for cancellation.
            return PollAction(id);
        }
        public string PrepareLab() {
            if (!Context.IsWorldReady || Game1.player.Name != "AgentLab" || Context.IsMultiplayer) throw new InvalidOperationException("lab_requires_singleplayer_AgentLab_save");
            if (records.Values.Any(a => a.Status == "running")) throw new InvalidOperationException("lab_has_active_commands");
            var farm = Game1.getFarm();
            var area = new Rectangle(42, 16, 15, 12);
            foreach (var old in BotManager.instances.Where(b => b.currentLocation == farm && area.Contains((int)b.TileLocation.X, (int)b.TileLocation.Y)).ToArray()) BotManager.instances.Remove(old);
            foreach (var p in farm.objects.Keys.Where(p => area.Contains((int)p.X, (int)p.Y)).ToArray()) farm.objects.Remove(p);
            foreach (var p in farm.terrainFeatures.Keys.Where(p => area.Contains((int)p.X, (int)p.Y)).ToArray()) farm.terrainFeatures.Remove(p);
            foreach (var clump in farm.resourceClumps.ToArray()) if (clump.getBoundingBox().Intersects(new Rectangle(area.X*64, area.Y*64, area.Width*64, area.Height*64))) farm.resourceClumps.Remove(clump);
            Game1.warpFarmer("Farm", 44, 25, false);
            for (int n = 0; n < 2; n++) {
                var tile = new Vector2(43, 18 + n*4);
                var bot = new BotObject(tile, farm);
                bot.owner.Value = Game1.player.UniqueMultiplayerID;
                bot.modData[IdKey] = "bot-" + (n+1);
                bot.Name = "Agent Bot " + (n+1);
                bot.shouldPickupDebris = true;
                foreach (var can in bot.inventory.OfType<WateringCan>()) can.WaterLeft = can.waterCanMax;
                farm.setObject(tile, bot);
                BotManager.instances.Add(bot);
                bot.InitShell();
            }
            for (int y = 18; y <= 22; y += 2) for (int x = 48; x <= 51; x++) {
                var dirt = new HoeDirt(0, farm);
                dirt.crop = new Crop("472", x, y, farm);
                if (x >= 50) { dirt.crop.currentPhase.Value = dirt.crop.phaseDays.Count - 1; dirt.crop.dayOfCurrentPhase.Value = 0; }
                farm.terrainFeatures[new Vector2(x, y)] = dirt;
            }
            records.Clear();
            return Json(new {fixture = "AgentLab-v1", initialized = true, crops = 12, bots = 2, note = "Fixture setup only; subsequent actions use normal movement/tools."});
        }
        private class ActionRecord {
            public string Id, Actor, Skill, Status = "running", Error, Product;
            public int InitialQuantity;
            public Vector2 Target;
            public GameLocation Location;
            public object Before, After;
            public DateTime Started;
            public bool Cancel;
        }
    }
}
