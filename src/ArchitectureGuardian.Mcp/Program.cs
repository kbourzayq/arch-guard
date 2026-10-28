using ArchitectureGuardian.Analysis.Telemetry;
using ArchitectureGuardian.Mcp.Hosting;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ArchitectureGuardian.Mcp;

/// <summary>Starts the standard MCP stdio host with all console logging directed to stderr.</summary>
internal static class Program
{
    public static async Task Main(string[] args)
    {
        using var telemetry = LocalTelemetryListener.FromEnvironment(Console.Error);
        var builder = Host.CreateApplicationBuilder(args);
        builder.Logging.AddConsole(console => console.LogToStandardErrorThreshold = LogLevel.Trace);
        builder.Services.AddArchitectureGuardianMcp().WithStdioServerTransport();
        using var host = builder.Build();
        await host.RunAsync();
    }
}
