using System.Net.Http.Json;
using System.Security.Cryptography;
using System.Text.Json;
using OrchDesk.Core.Config;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Safety;

namespace OrchDesk.Core.Automation;

// What the channel needs from OrchDesk. Every remote request goes through here, so the permission rules stay in one place.
public interface IRemoteHost
{
    bool Busy { get; }
    string StatusText();
    // Runs the request in the orchestra and returns the reply. `planOnly` forces read-only planning.
    Task<string> RunAsync(string text, bool planOnly, CancellationToken ct);
    void Stop();
    // Resolves one pending approval and returns the reply text. With an id it resolves exactly that request; without one it only works when a single request is waiting.
    string Resolve(bool approve, string? id);
}

// Over chat only low-risk approvals are allowed: file edits inside the project and tests. Shell commands, deletes, paths outside the project, memory,
// paid generation and anything else need you at the desktop, so a hijacked chat can never approve them.
public static class RemotePolicy
{
    private static readonly HashSet<string> Approvable = ["write_file", "edit_file"];
    /// <summary>
    /// File writes inside the project, and build/test commands that the policy would allow on their own, may be approved from the paired chat.
    /// Paths outside the project are already denied by the policy before anyone is asked.
    /// </summary>
    public static bool CanApproveRemotely(ApprovalRequest r)
    {
        if (r.Reason.Contains("outside", StringComparison.OrdinalIgnoreCase) || r.Reason.StartsWith("Absolute or parent path", StringComparison.Ordinal)) return false;
        if (Approvable.Contains(r.ToolName)) return true;
        return r.ToolName == "run_command" && r.Summary.StartsWith("Run: ", StringComparison.Ordinal)
            && new PermissionPolicy().EvaluateCommand(r.Summary[5..]).Verdict == Verdict.Allow;
    }

    /// <summary>
    /// The mode a remote request runs in: never more permissive than the folder's own setting, and Full access only when the user
    /// explicitly allowed it for remote use (RemoteAllowAuto). "/plan" is always read-only.
    /// </summary>
    public static (bool PlanOnly, WorkspacePermission Permission) ModeFor(AppConfig config, string project, bool planOnly)
    {
        var folder = config.PermissionFor(project);
        if (planOnly) return (true, WorkspacePermission.Manual);
        return (false, folder == WorkspacePermission.FullAccess && !config.RemoteAllowAuto ? WorkspacePermission.Manual : folder);
    }
}

// Telegram over long polling: OrchDesk only makes outbound HTTPS calls, never opens a port. Strict by design:
//  - only a private chat that was paired with a one-time code in the app can give orders; everyone else is ignored without a reply
//  - the pairing code lasts 10 minutes and is burned after 5 wrong guesses
//  - request size and rate are capped, and replies are secret-scrubbed and clipped
public sealed class TelegramChannel(HttpClient http, string token, AppConfig config, IRemoteHost host, Action<long> onPaired, Func<string, string>? redact = null, Func<DateTime>? clock = null)
{
    public const int MaxRequestChars = 2000, MaxReplyChars = 3500, MaxPairingTries = 5;
    private static readonly TimeSpan CodeLife = TimeSpan.FromMinutes(10);
    private readonly Func<DateTime> now = clock ?? (() => DateTime.UtcNow);
    private readonly Queue<DateTime> runTimes = new();
    private string? code; private DateTime codeExpires; private int wrongTries;
    private long offset;

    public bool PairingOpen => code != null && now() < codeExpires && wrongTries < MaxPairingTries;
    public string NewPairingCode() { code = RandomNumberGenerator.GetInt32(100000, 1000000).ToString(); codeExpires = now() + CodeLife; wrongTries = 0; return code; }
    private string Clean(string s) => (redact ?? Redaction.Clean)(s);
    private string Url(string method) => $"https://api.telegram.org/bot{token}/{method}";

    // What the app shows next to the Telegram switch, so a failure is never silent.
    public string Status { get; private set; } = "Connecting to Telegram…";
    public string? BotUsername { get; private set; }
    public bool Connected { get; private set; }
    // Shown only in the app: what the bot saw from a chat that is not paired. Strangers still get no reply in Telegram.
    public string Notice { get; private set; } = "";
    private void SetNotice(string text) { Notice = text; StatusChanged?.Invoke(); }
    public event Action? StatusChanged;
    private void SetStatus(string text, bool connected = false) { if (Status == text && Connected == connected) return; Status = text; Connected = connected; StatusChanged?.Invoke(); }

    // Checks the token (getMe) and clears any webhook, which would otherwise block polling with a 409 error. Returns true when the bot is reachable.
    public async Task<bool> ConnectAsync(CancellationToken ct)
    {
        try
        {
            using var me = await http.PostAsJsonAsync(Url("getMe"), new { }, ct);
            if (me.StatusCode == System.Net.HttpStatusCode.Unauthorized || me.StatusCode == System.Net.HttpStatusCode.NotFound) { SetStatus("Telegram rejected the bot token. Copy it again from @BotFather (the whole thing, like 123456789:AA...)."); return false; }
            if (!me.IsSuccessStatusCode) { SetStatus($"Telegram answered {(int)me.StatusCode} when checking the token. Try again in a moment."); return false; }
            using var doc = JsonDocument.Parse(await me.Content.ReadAsStringAsync(ct));
            BotUsername = doc.RootElement.TryGetProperty("result", out var r) && r.TryGetProperty("username", out var u) ? u.GetString() : null;
            try { using var _ = await http.PostAsJsonAsync(Url("deleteWebhook"), new { drop_pending_updates = false }, ct); } catch (HttpRequestException) { /* polling reports a real problem below */ }
            SetStatus(BotUsername == null ? "Connected" : "Connected as @" + BotUsername, true); return true;
        }
        catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
        catch (Exception e) when (e is HttpRequestException or TaskCanceledException or JsonException)
        { SetStatus("Cannot reach Telegram (api.telegram.org): " + Clean(e.InnerException?.Message ?? e.Message) + ". Check your internet connection, VPN or firewall."); return false; }
    }

    public async Task RunAsync(CancellationToken ct)
    {
        try { while (!await ConnectAsync(ct)) await Task.Delay(TimeSpan.FromSeconds(15), ct); } catch (OperationCanceledException) { return; }
        while (!ct.IsCancellationRequested)
        {
            try
            {
                using var response = await http.PostAsJsonAsync(Url("getUpdates"), new { offset, timeout = 25, allowed_updates = new[] { "message" } }, ct);
                if (!response.IsSuccessStatusCode)
                {
                    SetStatus((int)response.StatusCode switch
                    {
                        409 => "Another program is already reading this bot's messages (for example a second OrchDesk window or an old copy). Close it, or make a new bot with @BotFather.",
                        401 or 404 => "Telegram rejected the bot token. Copy it again from @BotFather.",
                        var code => $"Telegram answered {code}. Retrying."
                    });
                    await Task.Delay(TimeSpan.FromSeconds(10), ct); continue;
                }
                SetStatus(BotUsername == null ? "Connected" : "Connected as @" + BotUsername, true);
                using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct));
                if (!doc.RootElement.TryGetProperty("result", out var result) || result.ValueKind != JsonValueKind.Array) continue;
                foreach (var update in result.EnumerateArray())
                {
                    if (update.TryGetProperty("update_id", out var id) && id.TryGetInt64(out var n)) offset = Math.Max(offset, n + 1);
                    try { await HandleAsync(update, ct); } catch (Exception e) when (e is not OperationCanceledException) { /* one bad message must not stop the channel */ }
                }
            }
            catch (OperationCanceledException) { break; }
            catch (Exception e) when (e is HttpRequestException or JsonException or TaskCanceledException)
            {
                SetStatus("Lost the connection to Telegram: " + Clean(e.InnerException?.Message ?? e.Message) + ". Retrying.");
                try { await Task.Delay(TimeSpan.FromSeconds(10), ct); } catch (OperationCanceledException) { break; }
            }
        }
    }
    // Public so tests can feed updates without a network.
    public async Task HandleAsync(JsonElement update, CancellationToken ct)
    {
        if (!update.TryGetProperty("message", out var message) || !message.TryGetProperty("chat", out var chat)) return;
        if (!chat.TryGetProperty("id", out var idEl) || !idEl.TryGetInt64(out var chatId)) return;
        if (!(chat.TryGetProperty("type", out var type) && type.GetString() == "private")) return; // never act in groups or channels
        var text = message.TryGetProperty("text", out var t) && t.ValueKind == JsonValueKind.String ? TextSafety.Strip(t.GetString()!).Trim() : ""; if (text.Length == 0) return;

        if (config.TelegramChatId == 0)
        {
            if (text.StartsWith("/pair ", StringComparison.Ordinal)) await PairAsync(chatId, text[6..].Trim(), ct);
            else if (text.StartsWith("/pair", StringComparison.Ordinal) || text.StartsWith("/start", StringComparison.Ordinal)) SetNotice("The bot got your message. Press 'Pair a chat' in OrchDesk, then send the whole command it shows, like /pair 123456.");
            else SetNotice("A message arrived from a chat that is not paired and was ignored.");
            return; // unpaired chats get no response in Telegram
        }
        if (chatId != config.TelegramChatId) return; // strangers are ignored silently

        if (StatusMessageBuilder.IsStatusCommand(text)) { await SendAsync(chatId, host.StatusText(), ct); return; }
        var (command, argument) = Split(text);
        switch (command)
        {
            case "/help": case "/start": await SendAsync(chatId, "Send a task to run it. /plan <task> plans without changing anything. /status, /stop, /approve <number>, /deny <number>.", ct); return;
            case "/status": await SendAsync(chatId, host.StatusText(), ct); return;
            case "/stop": host.Stop(); await SendAsync(chatId, "Stopping.", ct); return;
            case "/approve": case "/deny": await SendAsync(chatId, host.Resolve(command == "/approve", argument.Length > 0 ? argument.TrimStart('#') : null), ct); return;
        }
        bool planOnly = command == "/plan"; string request = planOnly ? argument : command == "/run" ? argument : text;
        if (request.Length == 0) { await SendAsync(chatId, "Send the task after the command.", ct); return; }
        if (request.Length > MaxRequestChars) { await SendAsync(chatId, $"Too long: {request.Length} characters (limit {MaxRequestChars}).", ct); return; }
        if (host.Busy) { await SendAsync(chatId, "OrchDesk is busy with another run. /status to check, /stop to cancel.", ct); return; }
        if (!AllowRun()) { await SendAsync(chatId, $"Rate limit: {config.RemoteMaxPerHour} runs per hour.", ct); return; }
        await SendAsync(chatId, planOnly ? "Planning (read-only)…" : "On it. I will message you when it finishes; /status for progress.", ct);
        // The run goes on in the background so /status, /stop and /approve are still read while it works.
        CurrentRun = Task.Run(async () =>
        {
            string reply; try { reply = await host.RunAsync(request, planOnly, ct); } catch (OperationCanceledException) { reply = "Stopped."; } catch (Exception e) { reply = "Failed: " + e.Message; }
            if (!config.TelegramNotifyJobDone) return; // completion is delivered by the shared job notifier
            if (host is not IJobNotificationHost) await SendAsync(chatId, reply, CancellationToken.None);
        }, CancellationToken.None);
    }
    public Task? CurrentRun { get; private set; }

    private async Task PairAsync(long chatId, string attempt, CancellationToken ct)
    {
        if (!PairingOpen) { SetNotice("Got /pair, but pairing is not open (it lasts 10 minutes, or was used up by wrong guesses). Press 'Pair a chat' in OrchDesk for a new code."); return; }
        if (!CryptographicOperations.FixedTimeEquals(System.Text.Encoding.UTF8.GetBytes(attempt), System.Text.Encoding.UTF8.GetBytes(code!))) { wrongTries++; SetNotice("Got /pair with the wrong code. Send the code exactly as OrchDesk shows it."); return; }
        code = null; SetNotice(""); config.TelegramChatId = chatId; onPaired(chatId);
        await SendAsync(chatId, "Paired. This chat can now send tasks to OrchDesk. Unpair in the Automation page.", ct);
    }

    private bool AllowRun()
    {
        var cutoff = now() - TimeSpan.FromHours(1); while (runTimes.Count > 0 && runTimes.Peek() < cutoff) runTimes.Dequeue();
        if (runTimes.Count >= Math.Max(1, config.RemoteMaxPerHour)) return false;
        runTimes.Enqueue(now()); return true;
    }

    private static (string Command, string Argument) Split(string text)
    {
        if (!text.StartsWith('/')) return ("", text);
        int space = text.IndexOfAny([' ', '\n']); var command = (space < 0 ? text : text[..space]).ToLowerInvariant(); int at = command.IndexOf('@'); if (at > 0) command = command[..at];
        return (command, space < 0 ? "" : text[(space + 1)..].Trim());
    }

    public async Task SendAsync(long chatId, string text, CancellationToken ct)
    {
        text = Clean(text);
        if (text.Length > MaxReplyChars) text = JobMessageBuilder.Clip(text, MaxReplyChars - 50) + "\n[clipped; open OrchDesk for the full answer]";
        try { using var _ = await http.PostAsJsonAsync(Url("sendMessage"), new { chat_id = chatId, text, disable_web_page_preview = true }, ct); }
        catch (HttpRequestException) { /* the reply is lost, the run is not */ }
    }
}
