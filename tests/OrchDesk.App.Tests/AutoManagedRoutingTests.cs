using System.Runtime.CompilerServices;
using OrchDesk.App.Services;
using OrchDesk.App.ViewModels;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;
namespace OrchDesk.Tests;
public sealed class AutoManagedRoutingTests
{
    private sealed class Provider(string id,string model,ProviderHealth health=ProviderHealth.Online):IModelProvider {
        public string Id=>id; public string ModelId=>model; public string DisplayName=>model; public bool IsLocal=>true; public bool IsAgentic=>false; public int Calls;
        public Task<ProviderHealth> CheckHealthAsync(CancellationToken ct)=>Task.FromResult(health);
        public async IAsyncEnumerable<ChatEvent> StreamChatAsync(ChatRequest request,[EnumeratorCancellation] CancellationToken ct) {
            Calls++; await Task.Yield();
            if(model=="opus") {Assert.True(request.ReadOnly); Assert.DoesNotContain(request.Tools,t=>t.Name is "write_file" or "run_command");}
            yield return new TextDelta("Hello from "+model); yield return new Done();
        }
    }
    [Theory][InlineData(true)][InlineData(false)]
    public async Task EveryAutoRequestUsesConfiguredPmWhenAvailable(bool chatOnly) {
        var root=Path.Combine(Path.GetTempPath(),"OrchDesk-routing-"+Guid.NewGuid());
        try {
            using var store=new ConfigStore(root);
            var pm=new Provider("claude-outlook","opus"); var worker=new Provider("opencode-go","glm");
            var services=new AppServices(store:store,chatOnly:chatOnly,providers:[pm,worker]);
            var vm=new ChatViewModel(services,Path.Combine(root,"Scratch")) {Input="Hello",WorkModeIndex=1};
            await vm.SendCommand.ExecuteAsync(null);
            Assert.Equal(1,pm.Calls); Assert.Equal(0,worker.Calls);
            Assert.NotNull(vm.LastTeamJob); Assert.Contains("opus",vm.Agents.Planning);
            Assert.Contains(vm.Items.OfType<AssistantItem>(),a=>a.Text.Contains("Hello from opus"));
        } finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
    }
    [Fact] public async Task LoggedOutPmIsExcludedAndPlainFailoverBannerIsShown() {
        var root=Path.Combine(Path.GetTempPath(),"OrchDesk-routing-"+Guid.NewGuid());
        try {
            using var store=new ConfigStore(root);
            var pm=new Provider("claude-outlook","opus",ProviderHealth.NotConfigured); var worker=new Provider("opencode-go","glm");
            var services=new AppServices(store:store,chatOnly:true,providers:[pm,worker]);
            var vm=new ChatViewModel(services,Path.Combine(root,"Scratch")) {Input="Hello",WorkModeIndex=1};
            await vm.SendCommand.ExecuteAsync(null);
            Assert.Equal(0,pm.Calls); Assert.Equal(1,worker.Calls); Assert.Null(vm.LastTeamJob);
            Assert.Contains(vm.Items.OfType<StatusItem>(),s=>s.Text.Contains("Claude unavailable - plain failover"));
        } finally {if(Directory.Exists(root)) Directory.Delete(root,true);}
    }
}
