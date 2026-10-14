using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using OrchDesk.Core.Accounts;
using OrchDesk.Core.Agent;
using OrchDesk.Core.Models;
using OrchDesk.Core.Orchestration;
using OrchDesk.Core.Permissions;
using OrchDesk.Core.Providers;
using OrchDesk.Core.Tools;

namespace OrchDesk.Core.Verification;

public sealed record SelfTestItem(string Name, bool Pass, string Detail);
public sealed record SelfTestReport(string Provider, DateTimeOffset CompletedAt, IReadOnlyList<SelfTestItem> Items)
{
    public bool Passed => Items.All(i => i.Pass);
}

public static class SelfTestRunner
{
    public static async Task<SelfTestReport> RunAsync(string folder, IModelProvider provider,
        CancellationToken ct = default, Func<ToolContext, CancellationToken, Task<string>>? fetch = null,
        IReadOnlyList<IModelProvider>? accounts = null)
    {
        Directory.CreateDirectory(folder);
        var ctx = new ToolContext(folder);
        var items = new List<SelfTestItem>();
        var tools = ToolRegistry.CreateDefault();
        var policy = new PermissionPolicy();
        var loop = new AgentLoop(tools, new PermissionGate(policy, null), new AgentLoopOptions { MaxSteps = 12 });
        async Task Check(string name, Func<Task<bool>> test, string detail)
        {
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            try {
                var pass = await test().WaitAsync(timeout.Token);
                items.Add(new(name, pass, pass ? detail : "Failed expectation: " + detail));
            }
            catch (Exception ex) { items.Add(new(name, false, ex.GetType().Name)); }
            // Report contains fixed summaries and exception types, never provider text, headers or secrets.
        }
        await Check("model_reply", async () => {
            var text = false; var failed = false;
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(2));
            await foreach (var ev in provider.StreamChatAsync(new ChatRequest([ChatMessage.User("Reply with hello in one short sentence.")], [], ctx.WorkspaceRoot), timeout.Token)) {
                text |= ev is TextDelta { Text.Length: > 0 };
                failed |= ev is ErrorEvent or ProviderUnavailable or UsageLimitHit;
            }
            return text && !failed;
        }, "Provider returned answer text without an error.");
        await Check("agent_create_edit", async () => {
            // A fresh subfolder protects any existing hello.txt in the supplied report folder.
            var work = Path.Combine(folder, "agent-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(work);
            var results = new List<ToolCallResult>();
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromMinutes(3));
            await foreach (var ev in loop.RunAsync(provider,
                [ChatMessage.User("Use write_file to create hello.txt containing exactly hello. Then use edit_file to replace hello with hello world. Do not run commands. Finish after both tools succeed.")],
                new ToolContext(work), timeout.Token)) if (ev is ToolCallResult r) results.Add(r);
            return results.Any(r => r.Name == "write_file" && !r.IsError) && results.Any(r => r.Name == "edit_file" && !r.IsError) &&
                File.Exists(Path.Combine(work, "hello.txt")) && (await File.ReadAllTextAsync(Path.Combine(work, "hello.txt"), ct)).Trim() == "hello world";
        }, "Model created and edited hello.txt through the agent loop.");
        await Check("file_read_edit_save", async () => {
            var path = "file-" + Guid.NewGuid().ToString("N") + ".txt";
            await new WriteFileTool().ExecuteAsync(new JsonObject { ["path"] = path, ["content"] = "before" }, ctx, ct);
            var read = await new ReadFileTool().ExecuteAsync(new JsonObject { ["path"] = path }, ctx, ct);
            await new EditFileTool().ExecuteAsync(new JsonObject { ["path"] = path, ["old_string"] = "before", ["new_string"] = "after" }, ctx, ct);
            return read.Contains("before") && await File.ReadAllTextAsync(WorkspacePath.Resolve(folder, path), ct) == "after";
        }, "Workspace file was read, edited and saved.");
        await Check("delete_permission", () => Task.FromResult(policy.Evaluate("delete_file", new JsonObject { ["path"] = "hello.txt" }).Verdict == Verdict.Ask), "Delete returns Ask.");
        await Check("accounts_rotation", async () => {
            var fallback = new VerificationProvider("fake-worker");
            var rotator = new AccountRotator([provider, fallback]);
            await rotator.RefreshHealthAsync(ct);
            var states = string.Join(", ", (accounts ?? [provider]).Select(p =>
                $"{p.Id}: {(p.Id == provider.Id ? rotator.Find(provider.Id)!.Health.ToString() : "Not checked (selftest avoids CLI profiles)")}"));
            rotator.MarkLimitHit(provider.Id, "selftest simulated limit");
            var pass = rotator.Current()?.Id == fallback.Id;
            items.Add(new("accounts_status", true, states));
            rotator.ClearLimit(provider.Id);
            return pass;
        }, "A simulated limit rotates to fake-worker; cooldown can be cleared.");
        await Check("supervisor_round", async () => {
            var supervisorPolicy = new PermissionPolicy();
            supervisorPolicy.ExtraAllowedCommandPrefixes.Add("echo selftest");
            var supervisor = new Supervisor(new AgentLoop(tools, new PermissionGate(supervisorPolicy, null))) { MaxRounds = 1 };
            await foreach (var ev in supervisor.RunAsync("Verification only; make no edits.", new VerificationProvider("fake-planner"), new VerificationProvider("fake-worker"), "echo selftest", folder, ct)) { }
            return supervisor.Result is { Passed: true, Rounds: 1 };
        }, "One supervisor round with fake planner and worker ran a real verification command.");
        await Check("web_fetch", async () => {
            var output = fetch is null ? await new WebFetchTool().ExecuteAsync(new JsonObject { ["url"] = "https://example.com" }, ctx, ct) : await fetch(ctx, ct);
            return output.Contains("<untrusted_web_content") && output.Contains("Example Domain");
        }, "GET https://example.com returned content marked untrusted.");
        var report = new SelfTestReport(provider.Id, DateTimeOffset.UtcNow, items);
        await File.WriteAllTextAsync(Path.Combine(folder, "selftest.json"), JsonSerializer.Serialize(report, new JsonSerializerOptions { WriteIndented = true, PropertyNamingPolicy = JsonNamingPolicy.CamelCase }), ct);
        return report;
    }

    private sealed class VerificationProvider(string id) : IModelProvider
    {
        public string Id => id;
        public string DisplayName => id;
        public bool IsAgentic => false;
        public bool IsLocal => true;
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) => Task.FromResult(ProviderHealth.Online);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            await Task.CompletedTask;
            yield return new TextDelta("Verification complete.");
            yield return new Done();
        }
    }
}
