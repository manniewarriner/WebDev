using System.Text.Json.Nodes;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Overnight;
using OrchDesk.Core.Providers;

namespace OrchDesk.Tests;

public class ProviderParsingTests
{
    private static async IAsyncEnumerable<string> Lines(params string[] lines)
    {
        foreach (var l in lines) { await Task.Yield(); yield return l; }
    }

    [Fact]
    public async Task Sse_text_and_fragmented_tool_calls()
    {
        var events = await SseChatParser.ParseAsync(Lines(
            """data: {"choices":[{"delta":{"content":"Hel"}}]}""",
            "",
            """data: {"choices":[{"delta":{"content":"lo"}}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"id":"call_a","function":{"name":"read_file","arguments":"{\"pa"}}]}}]}""",
            """data: {"choices":[{"delta":{"tool_calls":[{"index":0,"function":{"arguments":"th\":\"a.cs\"}"}}]}}]}""",
            "data: [DONE]")).ToListAsync();

        Assert.Equal("Hello", string.Concat(events.OfType<TextDelta>().Select(e => e.Text)));
        var call = events.OfType<ToolCallRequested>().Single().Call;
        Assert.Equal(("call_a", "read_file", """{"path":"a.cs"}"""), (call.Id, call.Name, call.ArgumentsJson));
        Assert.IsType<Done>(events[^1]);
    }

    [Fact]
    public async Task Sse_error_object_becomes_error_event()
    {
        var events = await SseChatParser.ParseAsync(Lines("""data: {"error":{"message":"model not found"}}""")).ToListAsync();
        Assert.Equal("model not found", Assert.IsType<ErrorEvent>(Assert.Single(events)).Message);
    }

    [Fact]
    public void Request_body_round_trips_tool_messages()
    {
        var req = new ChatRequest(
            [ChatMessage.User("hi"), ChatMessage.Assistant(null, [new ToolCall("c1", "read_file", "{}")]), ChatMessage.ToolResult("c1", "read_file", "data")],
            [new ToolDef("read_file", "d", new JsonObject { ["type"] = "object" })]);
        var body = OpenAiCompatProvider.BuildRequestBody(new OpenAiCompatOptions("http://x/v1", "qwen"), req);
        Assert.Equal("qwen", body["model"]!.GetValue<string>());
        Assert.Equal("c1", body["messages"]![1]!["tool_calls"]![0]!["id"]!.GetValue<string>());
        Assert.Equal("tool", body["messages"]![2]!["role"]!.GetValue<string>());
        Assert.Equal("c1", body["messages"]![2]!["tool_call_id"]!.GetValue<string>());
        Assert.Equal("read_file", body["tools"]![0]!["function"]!["name"]!.GetValue<string>());
    }

    [Fact]
    public void Claude_stream_json_events()
    {
        var init = ClaudeStreamParser.Parse("""{"type":"system","subtype":"init","session_id":"s-1"}""", "c");
        Assert.Equal("s-1", init.SessionId);

        var asst = ClaudeStreamParser.Parse(
            """{"type":"assistant","message":{"content":[{"type":"text","text":"Reading"},{"type":"tool_use","id":"t1","name":"Read","input":{"file_path":"a.cs"}}]}}""", "c");
        Assert.IsType<TextDelta>(asst.Events[0]);
        Assert.Equal("Read", Assert.IsType<ToolCallRequested>(asst.Events[1]).Call.Name);

        var res = ClaudeStreamParser.Parse("""{"type":"user","message":{"content":[{"type":"tool_result","tool_use_id":"t1","content":"ok"}]}}""", "c");
        Assert.Equal("ok", Assert.IsType<ToolCallResult>(Assert.Single(res.Events)).Output);

        var done = ClaudeStreamParser.Parse("""{"type":"result","subtype":"success","is_error":false,"result":"All done"}""", "c");
        Assert.Equal("All done", Assert.IsType<Done>(Assert.Single(done.Events)).FinalText);
    }

    [Theory]
    [InlineData("""{"type":"result","subtype":"error_during_execution","is_error":true,"result":"Claude AI usage limit reached|1760000000"}""")]
    [InlineData("""{"type":"rate_limit_event","rate_limit_info":{"status":"rejected","resetsAt":1760000000}}""")]
    [InlineData("You've hit your limit · resets 3am")]
    public void Claude_usage_limit_is_detected(string line) =>
        Assert.IsType<UsageLimitHit>(Assert.Single(ClaudeStreamParser.Parse(line, "claude-outlook").Events));

    [Fact]
    public void Claude_cli_strips_paid_api_env_and_sets_profile()
    {
        var p = new ClaudeCliProvider("c", "C", new ClaudeCliOptions(@"C:\Users\me\.claude-gmail"));
        var env = p.BuildEnvironment();
        Assert.Equal(@"C:\Users\me\.claude-gmail", env["CLAUDE_CONFIG_DIR"]);
        Assert.True(env.ContainsKey("ANTHROPIC_API_KEY") && env["ANTHROPIC_API_KEY"] is null);
        Assert.True(env.ContainsKey("ANTHROPIC_AUTH_TOKEN") && env["ANTHROPIC_AUTH_TOKEN"] is null);

        var args = p.BuildArgs(new ChatRequest([], [], ReadOnly: true), "sess");
        Assert.Equal(["-p", "--output-format", "stream-json", "--verbose", "--permission-mode", "plan", "--resume", "sess"], args);
        Assert.DoesNotContain("--bare", args); // --bare would ignore the subscription login
    }

    [Fact]
    public void Transcript_flattens_history_for_cli_handoff()
    {
        var text = Transcript.Flatten([
            ChatMessage.User("fix bug"), ChatMessage.Assistant("looking", [new ToolCall("1", "read_file", """{"path":"a"}""")]),
            ChatMessage.ToolResult("1", "read_file", "contents"), ChatMessage.User("continue")]);
        Assert.Contains("[user]\nfix bug", text);
        Assert.Contains("[result of read_file]\ncontents", text);
        Assert.EndsWith("[user]\ncontinue\n\n", text);
    }

    [Fact]
    public void Config_roundtrip_and_defaults()
    {
        using var dir = new TempDir();
        var store = new ConfigStore(dir.Path);
        var cfg = store.Load();
        Assert.Equal("local-pc", cfg.RotationOrder[0]);
        cfg.Theme = "Light";
        store.Save(cfg);
        Assert.Equal("Light", new ConfigStore(dir.Path).Load().Theme);
        Assert.Contains("\"kind\": \"ClaudeCli\"", File.ReadAllText(store.FilePath));
    }

    [Fact]
    public void Secret_scanner_flags_added_keys_only()
    {
        var diff = "+++ b/a.cs\n+var k = \"sk-ant-api03-abcdefghijklmnopqrstuvwxyz\";\n-old ghp_abcdefghijklmnopqrstuvwxyz0123456789\n";
        Assert.Equal(["Anthropic key"], SecretScanner.FindSecrets(diff));
        Assert.Empty(SecretScanner.FindSecrets("+just code\n"));
    }

    [Fact]
    public void Overnight_schedule_xml_wakes_pc()
    {
        var xml = OvernightSchedule.BuildTaskXml(@"C:\Tools\OrchDesk.exe", new TimeOnly(1, 30));
        Assert.Contains("<WakeToRun>true</WakeToRun>", xml);
        Assert.Contains("T01:30:00", xml);
        Assert.Contains("--overnight", xml);
        System.Xml.Linq.XDocument.Parse(xml.Replace("UTF-16", "UTF-8")); // well-formed
    }
}
