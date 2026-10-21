using System.Text;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Mcp.Hosting;
using ArchitectureGuardian.Mcp.Requests;
using ModelContextProtocol;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Checks operator-owned access, shared policy loading and bounded, cancellable application requests.</summary>
public sealed class RequestBoundaryTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), "guardian-mcp-" + Guid.NewGuid().ToString("N"));

    /// <summary>Creates a temporary workspace containing no evaluated build logic.</summary>
    public RequestBoundaryTests() => Directory.CreateDirectory(_root);

    /// <summary>Rejects ambiguous startup inputs and requires an explicitly selected directory.</summary>
    [Fact]
    public void Startup_requires_an_explicit_root_and_rejects_unknown_options()
    {
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse([]));
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["--workspace-root", _root, "--http"]));
        Assert.Throws<ArgumentException>(() => ServerOptions.Parse(["--workspace-root", _root, "--workspace-root", _root]));
        Assert.False(ServerOptions.Parse(["--workspace-root", _root]).TrustWorkspace);
        Assert.True(ServerOptions.Parse(["--workspace-root", _root, "--trust-workspace"]).TrustWorkspace);
    }

    /// <summary>Rejects traversal, sibling-prefix and symbolic-link escapes, including intermediate links.</summary>
    [Fact]
    public void Files_must_remain_in_the_physical_workspace()
    {
        var outside = _root + "-outside";
        Directory.CreateDirectory(outside);
        try
        {
            File.WriteAllText(Path.Combine(outside, "policy.yaml"), "outside");
            File.WriteAllText(Path.Combine(_root, "policy.yaml"), "inside");
            var root = new WorkspaceRoot(_root);
            Assert.Equal(Path.Combine(root.Path, "policy.yaml"), root.ResolveFile("policy.yaml"));
            Assert.Throws<ArgumentException>(() => root.ResolveFile(Path.Combine(outside, "policy.yaml")));
            Assert.Throws<ArgumentException>(() => root.ResolveFile("../" + Path.GetFileName(outside) + "/policy.yaml"));
            Assert.Throws<ArgumentException>(() => root.ResolveFile("missing.yaml"));
            if (!OperatingSystem.IsWindows())
            {
                Directory.CreateSymbolicLink(Path.Combine(_root, "escape"), outside);
                File.CreateSymbolicLink(Path.Combine(_root, "link.yaml"), Path.Combine(outside, "policy.yaml"));
                Assert.Throws<ArgumentException>(() => root.ResolveFile("escape/policy.yaml"));
                Assert.Throws<ArgumentException>(() => root.ResolveFile("link.yaml"));
                File.CreateSymbolicLink(Path.Combine(_root, "indirect.yaml"), Path.Combine(root.Path, "escape", "policy.yaml"));
                Assert.Throws<ArgumentException>(() => root.ResolveFile("indirect.yaml"));
                Directory.CreateDirectory(Path.Combine(_root, "within"));
                File.WriteAllText(Path.Combine(_root, "within", "policy.yaml"), "inside");
                Directory.CreateSymbolicLink(Path.Combine(_root, "alias"), Path.Combine(root.Path, "within"));
                File.CreateSymbolicLink(Path.Combine(_root, "indirect-inside.yaml"), Path.Combine(root.Path, "alias", "policy.yaml"));
                Assert.Equal(Path.Combine(root.Path, "within", "policy.yaml"), root.ResolveFile("indirect-inside.yaml"));
                File.CreateSymbolicLink(Path.Combine(_root, "cycle.yaml"), Path.Combine(root.Path, "cycle.yaml"));
                Assert.Throws<ArgumentException>(() => root.ResolveFile("cycle.yaml"));
                Directory.Delete(Path.Combine(_root, "alias"));
                Directory.Delete(Path.Combine(_root, "escape"));
            }
        }
        finally
        {
            Directory.Delete(outside, recursive: true);
        }
    }

    /// <summary>Uses the shared YAML loader, limits policy size and keeps build trust under operator control.</summary>
    [Fact]
    public async Task Workspace_adapter_uses_the_shared_policy_contract()
    {
        var policyPath = Path.Combine(_root, "policy.yaml");
        const string policy = "version: 1\nmodules:\n  - id: App\n    projects: ['*.csproj']\nlayers:\n  - id: All\n    projects: ['*.csproj']\nrules:\n  AG001: error\n";
        await File.WriteAllTextAsync(policyPath, policy);
        var session = new WorkspaceSession(ServerOptions.Parse(["--workspace-root", _root]), new YamlPolicyLoader());
        Assert.Single((await session.LoadPolicyAsync("policy.yaml", default)).Modules);
        Assert.Equal(policy, await File.ReadAllTextAsync(policyPath));
        await File.WriteAllTextAsync(Path.Combine(_root, "App.csproj"), "<Project />");
        Assert.False(session.CreateLoadRequest("App.csproj").IsTrusted);
        await File.WriteAllBytesAsync(policyPath, new byte[ServerOptions.MaxMessageBytes + 1]);
        await Assert.ThrowsAsync<ArgumentException>(() => session.LoadPolicyAsync("policy.yaml", default));
    }

    /// <summary>Rejects concurrent work promptly and releases capacity after cancellation and failure.</summary>
    [Fact]
    public async Task Application_requests_are_bounded_and_cancellation_releases_capacity()
    {
        using var executor = new RequestExecutor(TimeSpan.FromSeconds(5));
        using var cancellation = new CancellationTokenSource();
        var started = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var running = executor.RunAsync(async token =>
        {
            started.SetResult();
            await Task.Delay(Timeout.Infinite, token);
            return 1;
        }, cancellation.Token);
        await started.Task;
        var busy = await Assert.ThrowsAsync<McpException>(() => executor.RunAsync(_ => Task.FromResult(2), default));
        Assert.StartsWith("request_busy:", busy.Message);
        cancellation.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => running);
        Assert.Equal(3, await executor.RunAsync(_ => Task.FromResult(3), default));
        await Assert.ThrowsAsync<InvalidOperationException>(() => executor.RunAsync<int>(_ => throw new InvalidOperationException(), default));
        Assert.Equal(4, await executor.RunAsync(_ => Task.FromResult(4), default));
    }

    /// <summary>Reports deadline expiry distinctly from client cancellation, including late results.</summary>
    [Fact]
    public async Task Deadline_does_not_turn_cancellation_into_success()
    {
        using var executor = new RequestExecutor(TimeSpan.FromMilliseconds(20));
        var timeout = await Assert.ThrowsAsync<McpException>(() => executor.RunAsync(async token =>
        {
            await Task.Delay(Timeout.Infinite, token);
            return 0;
        }, default));
        Assert.StartsWith("request_timeout:", timeout.Message);
        var late = await Assert.ThrowsAsync<McpException>(() => executor.RunAsync(async token =>
        {
            try { await Task.Delay(Timeout.Infinite, token); }
            catch (OperationCanceledException) { }
            return 0;
        }, default));
        Assert.StartsWith("request_timeout:", late.Message);
        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();
        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => executor.RunAsync(_ => Task.FromResult(0), cancelled.Token));
    }

    /// <summary>Bounds bytes per frame across reads and resets the budget after each newline.</summary>
    [Fact]
    public async Task Input_limit_is_per_frame_and_applies_before_parsing()
    {
        using var bytes = new MemoryStream(Encoding.UTF8.GetBytes(new string('x', ServerOptions.MaxMessageBytes) + "\n{}\n"));
        using var input = new BoundedInputStream(bytes);
        var buffer = new byte[4096];
        while (await input.ReadAsync(buffer.AsMemory()) != 0) { }
        using var oversized = new MemoryStream(new byte[ServerOptions.MaxMessageBytes + 1]);
        using var bounded = new BoundedInputStream(oversized);
        await Assert.ThrowsAsync<IOException>(async () =>
        {
            while (await bounded.ReadAsync(buffer.AsMemory()) != 0) { }
        });
    }

    /// <summary>Removes workspace test files.</summary>
    public void Dispose() => Directory.Delete(_root, recursive: true);
}
