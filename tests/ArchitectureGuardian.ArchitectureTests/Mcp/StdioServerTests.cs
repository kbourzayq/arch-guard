using System.Diagnostics;
using System.Text.Json;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Exercises the actual executable's SDK handshake, protocol streams and session lifecycle.</summary>
[Trait("Category", "Integration")]
public sealed class StdioServerTests
{
    /// <summary>Initializes locally, discovers the bounded read-only tools, rejects unsupported operations and exits on EOF.</summary>
    [Fact]
    public async Task Stdio_initializes_without_logging_on_stdout_and_exits_on_eof()
    {
        using var process = Start("--Workspace:Root", Repository.Root);
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await Send(process, new { jsonrpc = "2.0", id = 1, method = "initialize", @params = new { protocolVersion = "2025-11-25", capabilities = new { roots = new { listChanged = true } }, clientInfo = new { name = "guardian-test", version = "1.0" } } });
            using var initialized = await Read(process, timeout.Token);
            var result = initialized.RootElement.GetProperty("result");
            Assert.Equal("2025-11-25", result.GetProperty("protocolVersion").GetString());
            Assert.Equal("architecture-guardian", result.GetProperty("serverInfo").GetProperty("name").GetString());
            Assert.True(result.GetProperty("capabilities").TryGetProperty("tools", out _));
            Assert.False(result.GetProperty("capabilities").TryGetProperty("resources", out _));
            Assert.False(result.GetProperty("capabilities").TryGetProperty("prompts", out _));
            await Send(process, new { jsonrpc = "2.0", method = "notifications/initialized" });
            await Send(process, new { jsonrpc = "2.0", id = 2, method = "tools/list" });
            using var listed = await Read(process, timeout.Token);
            var tools = listed.RootElement.GetProperty("result").GetProperty("tools").EnumerateArray().ToArray();
            Assert.Equal(new[] { "get_architecture", "list_rules" }, tools.Select(tool => tool.GetProperty("name").GetString()).Order());
            Assert.All(tools, tool =>
            {
                Assert.True(tool.GetProperty("annotations").GetProperty("readOnlyHint").GetBoolean());
                Assert.True(tool.GetProperty("outputSchema").TryGetProperty("properties", out _));
            });
            await Send(process, new { jsonrpc = "2.0", id = 3, method = "tools/call", @params = new { name = "write_baseline", arguments = new { } } });
            using var rejected = await Read(process, timeout.Token);
            Assert.True(rejected.RootElement.TryGetProperty("error", out _) ||
                rejected.RootElement.GetProperty("result").GetProperty("isError").GetBoolean());
            await Send(process, new { jsonrpc = "2.0", id = 4, method = "source/write", @params = new { } });
            using var unknown = await Read(process, timeout.Token);
            Assert.Equal(-32601, unknown.RootElement.GetProperty("error").GetProperty("code").GetInt32());
            await Send(process, new { jsonrpc = "2.0", method = "notifications/cancelled", @params = new { requestId = 999, reason = "already finished" } });
            await Send(process, new { jsonrpc = "2.0", id = 5, method = "ping" });
            using var ping = await Read(process, timeout.Token);
            Assert.True(ping.RootElement.TryGetProperty("result", out _));
            process.StandardInput.Close();
            await process.WaitForExitAsync(timeout.Token);
            Assert.Equal("", await process.StandardOutput.ReadToEndAsync(timeout.Token));
            Assert.Equal(0, process.ExitCode);
            Assert.Contains("Application started", await error);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
        }
    }

    /// <summary>Reports invalid operator configuration only on stderr with a nonzero exit code.</summary>
    [Fact]
    public async Task Invalid_startup_keeps_stdout_empty()
    {
        using var process = Start("--Workspace:Root", Path.Combine(Repository.Root, "missing-" + Guid.NewGuid()));
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(20));
        try
        {
            await process.WaitForExitAsync(timeout.Token);
            Assert.NotEqual(0, process.ExitCode);
            Assert.Equal("", await output);
            Assert.Contains("Workspace:Root", await error);
        }
        finally
        {
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
        }
    }

    private static Process Start(params string[] args)
    {
        var start = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Repository.Root,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add(Path.Combine(AppContext.BaseDirectory, "ArchitectureGuardian.Mcp.dll"));
        foreach (var arg in args) { start.ArgumentList.Add(arg); }
        return Process.Start(start)!;
    }

    private static async Task Send(Process process, object message)
    {
        await process.StandardInput.WriteLineAsync(JsonSerializer.Serialize(message));
        await process.StandardInput.FlushAsync();
    }

    private static async Task<JsonDocument> Read(Process process, CancellationToken token) =>
        JsonDocument.Parse(await process.StandardOutput.ReadLineAsync(token) ?? throw new IOException("Server exited before responding."));
}
