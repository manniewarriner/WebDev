using OrchDesk.Core.Config;

namespace OrchDesk.Core.Integrations;

public sealed record ProviderSyncResult(string Provider, string Status, int ModelCount);

public static class StartupModelSync
{
    public static async Task<List<ProviderSyncResult>> RunAsync(AppConfig config, IntegrationModelSync sync, CancellationToken ct = default)
    {
        return (await Task.WhenAll(config.Providers.Select(async provider =>
        {
            if (!provider.Enabled || provider.Kind is not (ProviderKind.OpenAiCompatible or ProviderKind.ClaudeCli))
                return new ProviderSyncResult(provider.Id, "skip", provider.AvailableModels.Count);
            using var timeout = CancellationTokenSource.CreateLinkedTokenSource(ct);
            timeout.CancelAfter(TimeSpan.FromSeconds(15));
            try
            {
                var result = await sync.RunAsync(provider, ct: timeout.Token).WaitAsync(timeout.Token);
                if (provider.Kind == ProviderKind.ClaudeCli) provider.AuthStatus = result.Ok ? "Signed in" : "Sign-in needed";
                // A failed Claude auth check must not display a stale connected profile.
                if (!result.Ok && provider.Kind == ProviderKind.ClaudeCli)
                {
                    provider.AvailableModels.Clear();
                    provider.ModelsSyncedAt = null;
                }
                return new ProviderSyncResult(provider.Id, result.Ok ? "pass" : "fail", provider.AvailableModels.Count);
            }
            catch (OperationCanceledException)
            {
                return new ProviderSyncResult(provider.Id, "fail", provider.AvailableModels.Count);
            }
        }))).ToList();
    }
}
