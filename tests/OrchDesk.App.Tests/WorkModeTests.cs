using System.Runtime.CompilerServices;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;

namespace OrchDesk.Tests;

public sealed class WorkModeTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "orchdesk-plan-" + Guid.NewGuid());
    [Fact]
    public async Task Edited_plan_approval_starts_agent_run_with_approved_context()
    {
        var provider = new PlanProvider();
        var services = new AppServices(store: new ConfigStore(_root), chatOnly: true, providers: [provider]);
        var vm = new ChatViewModel(services, Path.Combine(_root, "workspace")) { WorkModeIndex = 0, Input = "Fix the app" };
        await vm.SendCommand.ExecuteAsync(null);
        var plan = Assert.Single(vm.Items.OfType<PlanItem>());
        Assert.Equal(2, plan.Steps.Count);
        Assert.True(provider.Requests[0].ReadOnly);
        plan.Steps[0].Text = "Implement the reviewed change";
        plan.AddStepCommand.Execute(null);
        plan.Steps[^1].Text = "Verify all tests";
        await plan.ApproveCommand.ExecuteAsync(null);
        Assert.Equal(1, vm.WorkModeIndex);
        Assert.False(plan.IsPending);
        Assert.False(provider.Requests[^1].ReadOnly);
        var task = provider.Requests[^1].Messages.Last(m => m.Role == ChatRole.User).Content!;
        Assert.Contains("1. Implement the reviewed change", task);
        Assert.Contains("3. Verify all tests", task);
        Assert.Contains(vm.Items.OfType<AssistantItem>(), a => a.ThoughtSummary.Contains("~") && a.UsageTooltip.Contains("planner"));
        Assert.Equal(WorkMode.Agent, services.Store.Load().LastWorkMode);
    }

    [Fact]
    public async Task Cancelled_plan_does_not_execute()
    {
        var calls = 0;
        var plan = new PlanItem(["Inspect"], _ => { calls++; return Task.CompletedTask; });
        plan.CancelCommand.Execute(null);
        await plan.ApproveCommand.ExecuteAsync(null);
        Assert.Equal(0, calls);
    }

    private sealed class PlanProvider : IModelProvider
    {
        public string Id => "planner";
        public string DisplayName => Id;
        public bool IsAgentic => false;
        public bool IsLocal => true;
        public List<ChatRequest> Requests { get; } = [];
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct) => Task.FromResult(ProviderHealth.Online);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request, [EnumeratorCancellation] CancellationToken ct)
        {
            Requests.Add(request with { Messages = request.Messages.ToArray() });
            yield return new TextDelta(request.ReadOnly ? "1. Inspect files\n2. Implement fix" : "Completed approved plan.");
            await Task.CompletedTask;
            yield return new Done();
        }
    }
    public void Dispose() { if (Directory.Exists(_root)) Directory.Delete(_root, true); }
}
