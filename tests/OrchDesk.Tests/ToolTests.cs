using System.Text.Json.Nodes;
using OrchDesk.Core.Tools;

namespace OrchDesk.Tests;

public class WorkspacePathTests
{
    [Theory]
    [InlineData("../secret.txt")]
    [InlineData("src/../../x")]
    [InlineData("..\\x")]
    public void Rejects_dotdot(string rel)
    {
        using var dir = new TempDir();
        Assert.Throws<PathOutsideWorkspaceException>(() => WorkspacePath.Resolve(dir.Path, rel));
    }

    [Fact]
    public void Rejects_absolute_path_outside()
    {
        using var dir = new TempDir();
        var outside = Path.GetTempPath();
        Assert.Throws<PathOutsideWorkspaceException>(() => WorkspacePath.Resolve(dir.Path, outside));
    }

    [Fact]
    public void Rejects_sibling_folder_with_same_prefix()
    {
        using var dir = new TempDir();
        Assert.Throws<PathOutsideWorkspaceException>(() => WorkspacePath.Resolve(dir.Path, dir.Path + "-evil/x.txt"));
    }

    [Fact]
    public void Accepts_paths_inside()
    {
        using var dir = new TempDir();
        Assert.Equal(Path.Combine(dir.Path, "src", "a.cs"), WorkspacePath.Resolve(dir.Path, "src/a.cs"));
        Assert.Equal(Path.Combine(dir.Path, "b.cs"), WorkspacePath.Resolve(dir.Path, Path.Combine(dir.Path, "b.cs")));
    }

    [Fact]
    public void Rejects_symlink_escape()
    {
        using var dir = new TempDir();
        using var outside = new TempDir();
        outside.File("secret.txt", "s");
        var link = Path.Combine(dir.Path, "link");
        try { Directory.CreateSymbolicLink(link, outside.Path); }
        catch (Exception) { return; } // symlinks may need privileges on Windows; skip there
        Assert.Throws<PathOutsideWorkspaceException>(() => WorkspacePath.Resolve(dir.Path, "link/secret.txt"));
    }
}

public class FileToolTests
{
    private static JsonObject J(string json) => (JsonObject)JsonNode.Parse(json)!;

    [Fact]
    public async Task Write_read_edit_roundtrip()
    {
        using var dir = new TempDir();
        var ctx = new ToolContext(dir.Path);
        await new WriteFileTool().ExecuteAsync(J("""{"path":"hello.txt","content":"Hello\nWorld\n"}"""), ctx, default);
        var read = await new ReadFileTool().ExecuteAsync(J("""{"path":"hello.txt"}"""), ctx, default);
        Assert.Contains("1\tHello", read);

        await new EditFileTool().ExecuteAsync(J("""{"path":"hello.txt","old_string":"World","new_string":"OrchDesk"}"""), ctx, default);
        Assert.Equal("Hello\nOrchDesk\n", File.ReadAllText(Path.Combine(dir.Path, "hello.txt")));
    }

    [Fact]
    public async Task Edit_requires_unique_match()
    {
        using var dir = new TempDir();
        dir.File("a.txt", "x x x");
        var ctx = new ToolContext(dir.Path);
        var ex = await Assert.ThrowsAsync<ToolException>(() =>
            new EditFileTool().ExecuteAsync(J("""{"path":"a.txt","old_string":"x","new_string":"y"}"""), ctx, default));
        Assert.Contains("3 times", ex.Message);

        await new EditFileTool().ExecuteAsync(J("""{"path":"a.txt","old_string":"x","new_string":"y","replace_all":true}"""), ctx, default);
        Assert.Equal("y y y", File.ReadAllText(Path.Combine(dir.Path, "a.txt")));
    }

    [Fact]
    public async Task Edit_matches_lf_against_crlf_file()
    {
        using var dir = new TempDir();
        dir.File("w.cs", "a\r\nb\r\nc\r\n");
        await new EditFileTool().ExecuteAsync(J("""{"path":"w.cs","old_string":"a\nb","new_string":"a\nB"}"""), new ToolContext(dir.Path), default);
        Assert.Equal("a\r\nB\r\nc\r\n", File.ReadAllText(Path.Combine(dir.Path, "w.cs")));
    }

    [Fact]
    public async Task List_and_search_skip_ignored_folders()
    {
        using var dir = new TempDir();
        dir.File("src/Program.cs", "class Program { }");
        dir.File("bin/Debug/Program.cs", "class Program { }");
        var ctx = new ToolContext(dir.Path);
        var list = await new ListFilesTool().ExecuteAsync(new JsonObject(), ctx, default);
        Assert.Contains("src/Program.cs", list);
        Assert.DoesNotContain("bin/", list);

        var hits = await new SearchTool().ExecuteAsync(J("""{"pattern":"class\\s+Program"}"""), ctx, default);
        Assert.Equal("src/Program.cs:1: class Program { }", hits);
    }

    [Fact]
    public async Task Write_outside_is_rejected()
    {
        using var dir = new TempDir();
        await Assert.ThrowsAsync<PathOutsideWorkspaceException>(() =>
            new WriteFileTool().ExecuteAsync(J("""{"path":"../escape.txt","content":"x"}"""), new ToolContext(dir.Path), default));
    }

    [Fact]
    public async Task Run_command_captures_output_and_exit_code()
    {
        using var dir = new TempDir();
        var output = await new RunCommandTool().ExecuteAsync(J("""{"command":"echo hi"}"""), new ToolContext(dir.Path), default);
        Assert.Contains("Exit code: 0", output);
        Assert.Contains("hi", output);
    }

    [Fact]
    public async Task Run_command_times_out()
    {
        using var dir = new TempDir();
        var cmd = OperatingSystem.IsWindows() ? "ping -n 30 127.0.0.1" : "sleep 30";
        var output = await new RunCommandTool().ExecuteAsync(new JsonObject { ["command"] = cmd, ["timeout_seconds"] = 1 }, new ToolContext(dir.Path), default);
        Assert.StartsWith("TIMED OUT", output);
    }

    [Fact]
    public async Task Web_fetch_refuses_local_addresses()
    {
        using var dir = new TempDir();
        await Assert.ThrowsAsync<ToolException>(() =>
            new WebFetchTool().ExecuteAsync(J("""{"url":"http://127.0.0.1:11434/"}"""), new ToolContext(dir.Path), default));
        await Assert.ThrowsAsync<ToolException>(() =>
            new WebFetchTool().ExecuteAsync(J("""{"url":"file:///etc/passwd"}"""), new ToolContext(dir.Path), default));
    }

    [Fact]
    public void Html_to_text_drops_scripts_and_tags()
    {
        Assert.Equal("Hi & bye", WebFetchTool.HtmlToText("<p>Hi &amp;</p><script>evil()</script><b>bye</b>"));
    }
}
