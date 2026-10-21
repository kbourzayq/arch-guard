using ArchitectureGuardian.Configuration.Yaml;
using ArchitectureGuardian.Core.Policies;
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
        return services.AddMcpServer(server =>
        {
            server.ServerInfo = new() { Name = "architecture-guardian", Version = "0.1.0" };
            server.InitializationTimeout = TimeSpan.FromSeconds(10);
        }).WithToolsFromAssembly(typeof(McpServiceCollectionExtensions).Assembly);
    }
}
