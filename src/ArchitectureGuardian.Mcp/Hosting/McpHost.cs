using ArchitectureGuardian.Analysis.Orchestration;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Analysis;
using ArchitectureGuardian.Core.Dependencies;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Core.Rules;
using ArchitectureGuardian.Mcp.Requests;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using ModelContextProtocol;
using ModelContextProtocol.Protocol;

namespace ArchitectureGuardian.Mcp.Hosting;

/// <summary>Composes the SDK transport and application ports; tool behavior is added by subsequent features.</summary>
internal static class McpHost
{
    internal static async Task RunAsync(ServerOptions options)
    {
        var builder = Host.CreateApplicationBuilder(new HostApplicationBuilderSettings { DisableDefaults = true });
        builder.Logging.ClearProviders();
        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddSingleton(options);
        builder.Services.AddSingleton<IPolicyLoader, YamlPolicyLoader>();
        builder.Services.AddSingleton<WorkspaceSession>();
        builder.Services.AddSingleton(_ => new RequestExecutor(ServerOptions.RequestTimeout));
        builder.Services.AddSingleton<IAnalysisService<DependencyBuildResult>>(
            new AnalysisService<DependencyBuildResult>([new ModuleDependenciesRule(), new LayerDependenciesRule(),
                new ModuleCyclesRule(), new ImplementationBoundariesRule(), new ForbiddenReferencesRule(), new DomainInfrastructureRule()]));

        using var input = new BoundedInputStream(Console.OpenStandardInput());
        builder.Services.AddMcpServer(server =>
        {
            server.ServerInfo = new() { Name = "architecture-guardian", Version = "0.1.0" };
            server.InitializationTimeout = TimeSpan.FromSeconds(10);
            server.ServerInstructions = "Read-only local architecture adapter. No tools are registered in F17; architecture tools follow in F18. "
                + "The workspace root and build trust are fixed by the operator; client roots cannot widen them. "
                + "Application requests are limited to one at a time with a 30-second cooperative deadline.";
        })
            .WithStreamServerTransport(input, Console.OpenStandardOutput())
            .WithListToolsHandler((request, cancellationToken) => new ValueTask<ListToolsResult>(
                request.Services!.GetRequiredService<RequestExecutor>().RunAsync(
                    token => Task.FromResult(new ListToolsResult { Tools = [] }), cancellationToken)))
            .WithCallToolHandler((_, _) => throw new McpProtocolException("Unknown tool. No tools are registered in F17.", McpErrorCode.InvalidParams));

        using var host = builder.Build();
        await host.RunAsync();
    }
}
