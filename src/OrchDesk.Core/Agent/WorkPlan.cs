using System.Text.RegularExpressions;

namespace OrchDesk.Core.Agent;

public static class WorkPlan
{
    public const string Instruction = "You are in Plan mode. Inspect using read-only tools only. Do not edit files or execute commands. Return a numbered step-by-step implementation plan, one step per line, for the user to edit and approve.";
    public static List<string> Parse(string text) => Regex.Matches(text, @"(?m)^\s*\d+[.)]\s+(.+)$").Select(m => m.Groups[1].Value.Trim()).ToList();
    public static string ApprovedTask(IEnumerable<string> steps) => "Execute this user-approved plan until complete:\n" + string.Join("\n", steps.Where(s => !string.IsNullOrWhiteSpace(s)).Select((s, i) => $"{i + 1}. {s}"));
}
