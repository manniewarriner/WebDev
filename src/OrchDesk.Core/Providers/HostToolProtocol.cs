using System.Text.Json;
using OrchDesk.Core.Models;

namespace OrchDesk.Core.Providers;

public static class HostToolProtocol
{
    public static string Instruction(ChatRequest request) => "\n\nOrchDesk controls all tools and approvals. Native tools are disabled. To request one tool, output only <tool_call>{\"name\":\"tool_name\",\"arguments\":{...}}</tool_call>, then stop and wait for the result. Otherwise answer normally. Available tools: " + JsonSerializer.Serialize(request.Tools);
}
