using System.Reflection;
using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Mcp.Tools;
using Microsoft.Extensions.DependencyInjection;

namespace ArchitectureGuardian.Mcp.Hosting;

/// <summary>Registers attributed MCP tools and the shared application dependencies they can request.</summary>
internal static class McpServiceCollectionExtensions
{
    internal static IMcpServerBuilder AddArchitectureGuardianMcp(this IServiceCollection services)
    {
        services.AddOptions<WorkspaceOptions>()
            .BindConfiguration("Workspace")
            .Validate(options => !string.IsNullOrWhiteSpace(options.Root)
                && Path.IsPathFullyQualified(options.Root) && Directory.Exists(options.Root),
                "Workspace:Root must be an existing absolute directory.")
            .ValidateOnStart();
        services.AddSingleton<IPolicyLoader, YamlPolicyLoader>();
        services.AddSingleton<WorkspaceFiles>();
        var version = typeof(McpServiceCollectionExtensions).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion.Split('+')[0]
            ?? throw new InvalidOperationException("Assembly version metadata is missing.");
        return services.AddMcpServer(server =>
        {
            server.ServerInfo = new() { Name = "architecture-guardian", Version = version };
            server.InitializationTimeout = TimeSpan.FromSeconds(10);
        }).WithToolsFromAssembly(typeof(McpServiceCollectionExtensions).Assembly);
    }
}
