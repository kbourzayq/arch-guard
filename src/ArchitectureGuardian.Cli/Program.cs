using ArchitectureGuardian.Analysis.Telemetry;

namespace ArchitectureGuardian.Cli;

/// <summary>Provides the console entry point for policy-driven workspace commands.</summary>
internal static class Program
{
    /// <summary>Invokes the CLI with process streams and cooperative console cancellation.</summary>
    /// <param name="args">The arguments supplied by the process host.</param>
    /// <returns>Zero for completion/pass, one for a completed gate failure, or two for unreliable work.</returns>
    private static async Task<int> Main(string[] args)
    {
        using var telemetry = LocalTelemetryListener.FromEnvironment(Console.Error);
        return await CliApplication.RunAsync(args, Console.Out, Console.Error);
    }
}
