using System.Text.Json.Nodes;
using OrchDesk.Core.Config;
using OrchDesk.Core.Models;
using OrchDesk.Core.Providers;

namespace OrchDesk.Tests;

public sealed class OpenCodeV2Tests
{
    [Fact] public void Text_line_becomes_text_delta() =>
        Assert.Equal("PONG", Assert.IsType<TextDelta>(Assert.Single(OpenCodeStreamParser.Parse("""{"type":"text","part":{"type":"text","text":"PONG","time":{"start":1}}}"""))).Text);

    [Fact] public void Step_start_and_tool_use_emit_nothing()
    {
        Assert.Empty(OpenCodeStreamParser.Parse("""{"type":"step_start","part":{"type":"step-start"}}"""));
        Assert.Empty(OpenCodeStreamParser.Parse("""{"type":"tool_use","part":{"type":"tool","tool":"write","state":{"status":"completed"}}}"""));
    }

    [Fact] public void Step_finish_parses_nested_cache_usage() =>
        Assert.Equal(new ResponseUsage(120, 8), Assert.IsType<UsageReported>(Assert.Single(OpenCodeStreamParser.Parse(
            """{"type":"step_finish","part":{"type":"step-finish","reason":"stop","cost":0,"tokens":{"input":120,"output":8,"reasoning":3,"cache":{"read":50,"write":2}}}}"""))).Usage);

    [Theory][InlineData(401)][InlineData(403)]
    public void Auth_errors_become_provider_unavailable(int status)
    {
        var ev = Assert.Single(OpenCodeStreamParser.Parse($$$"""{"type":"error","error":{"type":"provider.auth","message":"free tier can only be used from within OpenCode","status":{{{status}}}}}""", "opencode-cli"));
        var unavailable = Assert.IsType<ProviderUnavailable>(ev);
        Assert.Equal("opencode-cli", unavailable.ProviderId);
        Assert.Contains(status.ToString(), unavailable.Message);
    }

    [Fact] public void Rate_limit_errors_become_usage_limit_hit()
    {
        Assert.IsType<UsageLimitHit>(Assert.Single(OpenCodeStreamParser.Parse("""{"type":"error","error":{"type":"provider.rate","message":"Too many requests","status":429}}""")));
        Assert.IsType<UsageLimitHit>(Assert.Single(OpenCodeStreamParser.Parse("""{"type":"error","error":{"message":"Monthly usage limit reached"}}""")));
    }

    [Fact] public void Other_errors_stay_error_events() =>
        Assert.Equal("boom", Assert.IsType<ErrorEvent>(Assert.Single(OpenCodeStreamParser.Parse("""{"type":"error","error":{"message":"boom","status":500}}"""))).Message);

    [Fact] public void Read_only_run_uses_standalone_json_and_v2_permissions()
    {
        var (args, env) = GenericCliProvider.PrepareRun("opencode", ["run", "--model", "opencode-go/kimi-k2#high"], null, restricted: true);
        Assert.Equal(["run", "--model", "opencode-go/kimi-k2#high", "--standalone", "--format", "json"], args);
        Assert.Equal("{\"*\":\"deny\"}", env["OPENCODE_PERMISSION"]);
        var permissions = JsonNode.Parse(env["OPENCODE_CONFIG_CONTENT"]!)!["permissions"]!.AsArray();
        Assert.Equal(("*", "deny"), (permissions[0]!["action"]!.ToString(), permissions[0]!["effect"]!.ToString()));
        Assert.Contains(permissions, p => p!["action"]!.ToString() == "read" && p["effect"]!.ToString() == "allow");
        Assert.DoesNotContain(permissions, p => p!["action"]!.ToString() is "edit" or "shell" && p["effect"]!.ToString() == "allow");
        Assert.Equal("external_directory", permissions[^1]!["action"]!.ToString());
        Assert.Equal("deny", permissions[^1]!["effect"]!.ToString());
    }

    [Fact] public void Unrestricted_opencode_run_has_no_permission_env()
    {
        var (args, env) = GenericCliProvider.PrepareRun("opencode.cmd", ["run"], null, restricted: false);
        Assert.Contains("--standalone", args);
        Assert.DoesNotContain("OPENCODE_CONFIG_CONTENT", env.Keys);
    }

    [Fact] public void Codex_run_is_unchanged_except_sandbox()
    {
        var (args, env) = GenericCliProvider.PrepareRun("codex", ["exec", "--sandbox", "workspace-write", "-"], null, restricted: true);
        Assert.Equal(["exec", "--sandbox", "read-only", "-"], args);
        Assert.Empty(env);
    }

    [Fact] public async Task Factory_passes_provider_model_and_variant_through()
    {
        var factory = new ProviderFactory(new PassThroughSecrets(), new HttpClient());
        var cfg = new ProviderConfig { Id = "opencode-cli", DisplayName = "OpenCode CLI", Kind = ProviderKind.OpenCodeCli, Executable = "opencode", Model = "opencode/big-pickle#high" };
        var provider = factory.Create(cfg)!;
        Assert.Equal("opencode/big-pickle#high", provider.ModelId);
        await Task.CompletedTask;
    }

    private sealed class PassThroughSecrets : ISecretProtector
    {
        public string Protect(string plaintext) => plaintext;
        public string Unprotect(string protectedText) => protectedText;
    }
}
