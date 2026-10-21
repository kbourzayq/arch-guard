using ArchitectureGuardian.Mcp.Hosting;

namespace ArchitectureGuardian.Mcp;

/// <summary>Starts the local, read-only MCP adapter over standard input and output.</summary>
internal static class Program
{
    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = ServerOptions.Parse(args);
            await McpHost.RunAsync(options);
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or IOException or UnauthorizedAccessException)
        {
            await Console.Error.WriteLineAsync($"MCP startup/transport failed: {exception.Message}");
            return 2;
        }
    }
}
