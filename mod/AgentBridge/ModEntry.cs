using System.Collections.Concurrent;
using System.Net;
using System.Text;
using System.Text.Json;
using StardewModdingAPI;
using StardewModdingAPI.Events;
using StardewValley;

namespace StardewAgent;

public interface IFarmtronicsControl {
    string GetState();
    string GetMap(string actorId);
    string StartAction(string request);
    string PollAction(string id);
    string CancelAction(string id);
    string PrepareLab();
    void Reset();
}

public sealed class Config {
    public int Port { get; set; } = 18765;
    public string Token { get; set; } = "";
    public bool EnableLab { get; set; } = false;
}

public sealed class ModEntry : Mod {
    private Config config = new();
    private IFarmtronicsControl? api;
    private readonly ConcurrentQueue<Request> pending = new();
    private readonly Dictionary<string, string> commands = new();
    private readonly Dictionary<string, string> running = new();
    private string session = Guid.NewGuid().ToString("N");
    private HttpListener? listener;
    private int count;
    private static readonly JsonSerializerOptions Json = new() { PropertyNamingPolicy = JsonNamingPolicy.CamelCase };

    public override void Entry(IModHelper helper) {
        config = helper.ReadConfig<Config>();
        if (config.Token.Length < 24) { Monitor.Log("Missing local bridge token; run scripts/launch.py.", LogLevel.Error); return; }
        helper.Events.GameLoop.GameLaunched += (_, _) => {
            api = helper.ModRegistry.GetApi<IFarmtronicsControl>("strout.farmtronics");
            if (config.EnableLab) Game1.options.pauseWhenOutOfFocus = false;
            Monitor.Log(api == null ? "Farmtronics control API missing." : "Farmtronics control API connected.", api == null ? LogLevel.Error : LogLevel.Info);
        };
        helper.Events.GameLoop.SaveLoaded += (_, _) => ResetSession();
        helper.Events.GameLoop.SaveCreated += (_, _) => ResetSession();
        helper.Events.GameLoop.ReturnedToTitle += (_, _) => ResetSession();
        helper.Events.GameLoop.UpdateTicked += Tick;
        helper.ConsoleCommands.Add("agent_lab", "Initialize fixture in an AgentLab test save only.", (_, _) => {
            try { Monitor.Log(PrepareLab(), LogLevel.Info); } catch (Exception e) { Monitor.Log(e.Message, LogLevel.Error); }
        });
        helper.ConsoleCommands.Add("agent_new", "Create a NEW AgentLab test farmer from the title screen only.", (_, _) => {
            if (!config.EnableLab || Context.IsWorldReady || Game1.activeClickableMenu is not StardewValley.Menus.TitleMenu title) {
                Monitor.Log("agent_new requires lab mode and the title screen.", LogLevel.Error); return;
            }
            Game1.options.pauseWhenOutOfFocus = false;
            Game1.whichFarm = 0;
            Game1.player.Name = "AgentLab";
            Game1.player.farmName.Value = "AgentLab";
            Game1.player.favoriteThing.Value = "Robots";
            title.createdNewCharacter(true);
            Monitor.Log("Creating a separate AgentLab test save.", LogLevel.Info);
        });
        helper.ConsoleCommands.Add("agent_quit", "Exit this development session (AgentLab or title only).", (_, _) => {
            if (!Context.IsWorldReady || Game1.player.Name == "AgentLab") Game1.game1.Exit();
        });
        listener = new HttpListener();
        listener.Prefixes.Add($"http://127.0.0.1:{config.Port}/");
        listener.Start();
        _ = Task.Run(Listen);
        Monitor.Log($"Local state bridge listening on 127.0.0.1:{config.Port}; token is not logged.", LogLevel.Info);
    }

    private void ResetSession() {
        session = Guid.NewGuid().ToString("N");
        commands.Clear(); running.Clear(); api?.Reset();
    }

    private async Task Listen() {
        while (listener?.IsListening == true) {
            try { var ctx = await listener.GetContextAsync(); _ = Task.Run(() => Receive(ctx)); }
            catch (HttpListenerException) { break; }
        }
    }

    private async Task Receive(HttpListenerContext ctx) {
        bool counted = false;
        try {
            if (ctx.Request.Headers["Origin"] != null) { await Reply(ctx, 403, "{\"error\":\"browser_origin_rejected\"}"); return; }
            if (ctx.Request.Url?.AbsolutePath != "/health" && ctx.Request.Headers["Authorization"] != "Bearer " + config.Token) {
                await Reply(ctx, 401, "{\"error\":\"unauthorized\"}"); return;
            }
            if (Interlocked.Increment(ref count) > 32) { Interlocked.Decrement(ref count); await Reply(ctx, 429, "{\"error\":\"queue_full\"}"); return; }
            counted = true;
            if (ctx.Request.ContentLength64 > 16384 || ctx.Request.ContentLength64 < 0) { await Reply(ctx, 413, "{\"error\":\"body_too_large\"}"); return; }
            using var reader = new StreamReader(ctx.Request.InputStream, Encoding.UTF8);
            var body = await reader.ReadToEndAsync();
            var request = new Request(ctx.Request.HttpMethod, ctx.Request.Url!.AbsolutePath, ctx.Request.QueryString["actor_id"], body);
            pending.Enqueue(request);
            // If it has not started by timeout, the game thread must skip it.
            if (await Task.WhenAny(request.Result.Task, Task.Delay(5000)) != request.Result.Task) {
                request.Result.TrySetCanceled();
                await Reply(ctx, 504, "{\"error\":\"game_thread_timeout\",\"detail\":\"Query command ID before retrying a write.\"}");
            } else {
                var result = await request.Result.Task;
                await Reply(ctx, result.Status, result.Body);
            }
        } catch (Exception) {
            try { await Reply(ctx, 500, "{\"error\":\"request_failed\"}"); } catch { /* Client already disconnected. */ }
        } finally { if (counted) Interlocked.Decrement(ref count); ctx.Response.Close(); }
    }

    private static async Task Reply(HttpListenerContext ctx, int status, string body) {
        ctx.Response.StatusCode = status;
        ctx.Response.ContentType = "application/json; charset=utf-8";
        byte[] bytes = Encoding.UTF8.GetBytes(body);
        ctx.Response.ContentLength64 = bytes.Length;
        await ctx.Response.OutputStream.WriteAsync(bytes);
    }

    private void Tick(object? sender, UpdateTickedEventArgs e) {
        if (api != null && Context.IsWorldReady) {
            foreach (var id in running.Keys.ToArray()) {
                try {
                    string result = api.PollAction(id);
                    using var doc = JsonDocument.Parse(result);
                    if (doc.RootElement.GetProperty("status").GetString() != "running") running.Remove(id);
                } catch (Exception ex) { Monitor.Log("Action observation failed: " + ex.Message, LogLevel.Warn); running.Remove(id); }
            }
        }
        for (int n = 0; n < 8 && pending.TryDequeue(out var request); n++) {
            if (request.Result.Task.IsCanceled) continue;
            try { request.Result.TrySetResult(Process(request)); }
            catch (JsonException) { request.Result.TrySetResult(new(400, "{\"error\":\"invalid_json\"}")); }
            catch (Exception ex) { request.Result.TrySetResult(new(409, JsonSerializer.Serialize(new {error = ex.GetBaseException().Message}, Json))); }
        }
    }

    private Response Process(Request r) {
        if (r.Method == "GET" && r.Path == "/health") return Ok(new {ready = Context.IsWorldReady, api_connected = api != null, session_id = session, protocol = 1});
        if (api == null || !Context.IsWorldReady) return new(503, "{\"error\":\"world_not_ready\"}");
        if (r.Method == "GET" && r.Path == "/state") {
            var state = JsonSerializer.Deserialize<Dictionary<string, object>>(api.GetState())!;
            state["session_id"] = session;
            return Ok(state);
        }
        if (r.Method == "GET" && r.Path == "/map") return new(200, api.GetMap(r.Actor ?? "bot-1"));
        if (r.Method == "POST" && r.Path == "/lab/reset") {
            RequireSession(r.Body);
            if (running.Count > 0) throw new InvalidOperationException("lab_has_active_commands");
            return new(200, PrepareLab());
        }
        if (r.Method == "POST" && r.Path == "/commands") {
            RequireSession(r.Body);
            using var doc = JsonDocument.Parse(r.Body);
            var root = doc.RootElement;
            string id = root.GetProperty("command_id").GetString() ?? "";
            if (id.Length is < 1 or > 100) throw new InvalidOperationException("invalid_command_id");
            if (commands.TryGetValue(id, out string? original)) {
                if (original != r.Body) throw new InvalidOperationException("command_id_conflict");
                return new(200, api.PollAction(id));
            }
            if (commands.Count >= 10000) throw new InvalidOperationException("session_command_limit");
            string result = api.StartAction(r.Body);
            commands[id] = r.Body;
            using var value = JsonDocument.Parse(result);
            if (value.RootElement.GetProperty("status").GetString() == "running") running[id] = r.Body;
            return new(202, result);
        }
        var segments = r.Path.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (segments.Length >= 2 && segments[0] == "commands") {
            string id = segments[1];
            if (!commands.ContainsKey(id)) return new(404, "{\"error\":\"unknown_command\"}");
            if (r.Method == "GET" && segments.Length == 2) return new(200, api.PollAction(id));
            if (r.Method == "POST" && segments.Length == 3 && segments[2] == "cancel") { RequireSession(r.Body); return new(200, api.CancelAction(id)); }
        }
        return new(404, "{\"error\":\"unknown_route\"}");
    }

    private string PrepareLab() {
        if (!config.EnableLab || api == null) throw new InvalidOperationException("lab_disabled");
        var result = api.PrepareLab();
        ResetSession();
        return result;
    }
    private void RequireSession(string body) {
        using var doc = JsonDocument.Parse(body);
        if (doc.RootElement.GetProperty("session_id").GetString() != session) throw new InvalidOperationException("stale_session");
    }
    private static Response Ok(object value) => new(200, JsonSerializer.Serialize(value, Json));
    private record Response(int Status, string Body);
    private record Request(string Method, string Path, string? Actor, string Body) {
        public TaskCompletionSource<Response> Result { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }
}
