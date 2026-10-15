using OrchDesk.Core.Providers;
namespace OrchDesk.Core.Orchestration;
public static class TeamRoles
{
    public static IModelProvider? Planner(IEnumerable<IModelProvider> available,TeamConfig config) =>
        available.Where(p=>p.ModelId.Contains("opus",StringComparison.OrdinalIgnoreCase)).OrderByDescending(p=>p.Id==config.PmProvider).FirstOrDefault()
        ?? available.Where(p=>p.Id.StartsWith("opencode",StringComparison.OrdinalIgnoreCase)).OrderByDescending(p=>Strength(p.ModelId)).FirstOrDefault();
    private static int Strength(string model)=>model.Contains("glm",StringComparison.OrdinalIgnoreCase)?100:model.Contains("kimi",StringComparison.OrdinalIgnoreCase)?90:model.Contains("deepseek",StringComparison.OrdinalIgnoreCase)?80:50;
    public static bool IsWorker(IModelProvider p)=>p.IsLocal || p.Id=="remote-pc" || p.Id.StartsWith("opencode",StringComparison.OrdinalIgnoreCase) || p.ModelId.Contains("haiku",StringComparison.OrdinalIgnoreCase);
}
