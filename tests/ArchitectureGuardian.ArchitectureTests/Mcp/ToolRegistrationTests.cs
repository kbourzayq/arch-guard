using System.IO.Pipelines;
using System.Text.Json.Nodes;
using ArchitectureGuardian.ArchitectureTests.Infrastructure;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Mcp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Options;
using Microsoft.Extensions.Hosting;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Xunit;

namespace ArchitectureGuardian.ArchitectureTests.Mcp;

/// <summary>Exercises attributed tools through the SDK with injected application services and protocol cancellation.</summary>
public sealed class ToolRegistrationTests
{
    /// <summary>The SDK discovers tool metadata, injects constructor and method dependencies, and invokes the shared loader.</summary>
    [Fact]
    public async Task Attributed_tool_uses_injected_application_dependencies()
    {
        await WithServer(async (client, token) =>
        {
            var tool = Assert.Single(await client.ListToolsAsync(cancellationToken: token), tool => tool.Name == "probe_policy");
            Assert.Equal("probe_policy", tool.Name);
            Assert.True(tool.ProtocolTool.Annotations!.ReadOnlyHint);
            var properties = tool.JsonSchema.GetProperty("properties");
            Assert.Single(properties.EnumerateObject());
            Assert.True(properties.TryGetProperty("path", out _));
            var result = await client.CallToolAsync(tool.Name,
                new Dictionary<string, object?> { ["path"] = Path.Combine(Repository.Root, "architecture.guardian.yaml") },
                cancellationToken: token);
            Assert.NotEqual(true, result.IsError);
            Assert.Equal($"{Repository.Root}: 2 modules", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        });
    }

    /// <summary>Client cancellation reaches the attributed tool and its dependency through the SDK's request token.</summary>
    [Theory]
    [InlineData("list_rules")]
    [InlineData("analyze_solution")]
    [InlineData("analyze_changes")]
    public async Task Sdk_cancellation_reaches_the_injected_service(string toolName)
    {
        var loader = new BlockingPolicyLoader();
        await WithServer(async (client, token) =>
        {
            var requestId = new RequestId("cancel-policy");
            var arguments = new JsonObject { ["policyPath"] = "architecture.guardian.yaml" };
            if (toolName != "list_rules") { arguments["inputPath"] = "ArchitectureGuardian.slnx"; }
            if (toolName == "analyze_changes")
            {
                arguments["baseReference"] = "HEAD~1";
                arguments["scope"] = "solution";
            }
            var call = client.SendRequestAsync(new JsonRpcRequest
            {
                Id = requestId,
                Method = RequestMethods.ToolsCall,
                Params = new JsonObject { ["name"] = toolName, ["arguments"] = arguments },
            }, token);
            await loader.Started.Task.WaitAsync(token);
            await client.SendNotificationAsync(NotificationMethods.CancelledNotification,
                new CancelledNotificationParams { RequestId = requestId }, cancellationToken: token);
            await loader.Cancelled.Task.WaitAsync(token);
            await Assert.ThrowsAnyAsync<OperationCanceledException>(() => call);
        }, loader);
    }

    /// <summary>Application failures use the SDK's tool-error result without exposing internal exception messages.</summary>
    [Fact]
    public async Task Sdk_translates_application_failures_to_tool_errors()
    {
        await WithServer(async (client, token) =>
        {
            var result = await client.CallToolAsync("probe_policy", new Dictionary<string, object?> { ["path"] = "unused" },
                cancellationToken: token);
            Assert.True(result.IsError);
            Assert.DoesNotContain("private failure detail", Assert.IsType<TextContentBlock>(Assert.Single(result.Content)).Text);
        }, new FailingPolicyLoader());
    }

    internal static async Task WithServer(Func<McpClient, CancellationToken, Task> action, IPolicyLoader? loader = null,
        string? root = null, bool trusted = false)
    {
        // Trusted scenarios perform several full workspace loads; this is an aggregate session budget.
        using var timeout = new CancellationTokenSource(trusted ? TimeSpan.FromMinutes(3) : TimeSpan.FromSeconds(60));
        var incoming = new Pipe();
        var outgoing = new Pipe();
        await using var serverInput = incoming.Reader.AsStream();
        await using var clientOutput = incoming.Writer.AsStream();
        await using var clientInput = outgoing.Reader.AsStream();
        await using var serverOutput = outgoing.Writer.AsStream();
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Configuration.AddInMemoryCollection(new Dictionary<string, string?>
        {
            ["Workspace:Root"] = root ?? Repository.Root,
            ["Workspace:Trusted"] = trusted.ToString(),
        });
        builder.Services.AddArchitectureGuardianMcp()
            .WithStreamServerTransport(serverInput, serverOutput)
            .WithTools<PolicyTools>();
        if (loader is not null) { builder.Services.AddSingleton(loader); }
        using var host = builder.Build();
        await host.StartAsync(timeout.Token);
        try
        {
            await using var client = await McpClient.CreateAsync(new StreamClientTransport(clientOutput, clientInput),
                new McpClientOptions { ProtocolVersion = "2025-11-25" }, cancellationToken: timeout.Token);
            await action(client, timeout.Token);
        }
        finally
        {
            await host.StopAsync(timeout.Token);
        }
    }

    /// <summary>Test-only tool; production discovery is restricted to the MCP executable's assembly.</summary>
    [McpServerToolType]
    private sealed class PolicyTools(IPolicyLoader loader)
    {
        [McpServerTool(Name = "probe_policy", ReadOnly = true, Destructive = false)]
        public async Task<string> LoadPolicyAsync(string path, IOptions<WorkspaceOptions> options, CancellationToken cancellationToken)
        {
            var policy = await loader.LoadAsync(path, cancellationToken);
            return $"{options.Value.Root}: {policy.Modules.Count} modules";
        }
    }

    private sealed class BlockingPolicyLoader : IPolicyLoader
    {
        internal TaskCompletionSource Started { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Cancelled { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ArchitecturePolicy> LoadAsync(string path, CancellationToken cancellationToken = default)
        {
            Started.TrySetResult();
            try
            {
                await Task.Delay(Timeout.Infinite, cancellationToken);
                throw new InvalidOperationException("Unexpected delay completion.");
            }
            finally
            {
                if (cancellationToken.IsCancellationRequested) { Cancelled.TrySetResult(); }
            }
        }
    }

    private sealed class FailingPolicyLoader : IPolicyLoader
    {
        public Task<ArchitecturePolicy> LoadAsync(string path, CancellationToken cancellationToken = default) =>
            throw new InvalidOperationException("private failure detail");
    }
}
