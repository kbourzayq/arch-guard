using BenchmarkDotNet.Configs;
using BenchmarkDotNet.Running;

namespace ArchitectureGuardian.Benchmarks;

/// <summary>Prepares isolated repository inputs and runs the non-shipping BenchmarkDotNet suite.</summary>
internal static class Program
{
    private static async Task<int> Main(string[] args)
    {
        var repository = BenchmarkInputs.FindRepository();
        var config = ManualConfig.Create(DefaultConfig.Instance)
            .WithArtifactsPath(Path.Combine(repository, "artifacts", "performance"));
        var switcher = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly);
        if (args.Any(argument => argument is "--help" or "-h" or "--list"))
        {
            switcher.Run(args, config);
            return 0;
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, signal) => { signal.Cancel = true; cancellation.Cancel(); };
        Console.CancelKeyPress += cancel;
        BenchmarkInputs inputs;
        try { inputs = await BenchmarkInputs.PrepareAsync(repository, cancellation.Token); }
        catch (OperationCanceledException)
        {
            Console.Error.WriteLine(cancellation.IsCancellationRequested ? "Benchmark preparation cancelled." : "Benchmark preparation timed out.");
            return 2;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or InvalidOperationException)
        {
            Console.Error.WriteLine($"Benchmark preparation failed: {exception.Message}");
            return 2;
        }
        finally { Console.CancelKeyPress -= cancel; }

        using (inputs)
        {
            var previous = Environment.GetEnvironmentVariable(BenchmarkInputs.EnvironmentVariable);
            try
            {
                Environment.SetEnvironmentVariable(BenchmarkInputs.EnvironmentVariable, inputs.DirectoryPath);
                var summaries = switcher.Run(args.Length == 0 ? ["--filter", "*"] : args, config).ToArray();
                return summaries.Length > 0 && summaries.All(summary => !summary.HasCriticalValidationErrors
                    && summary.Reports.Any() && summary.Reports.All(report => report.Success && report.ResultStatistics is not null)) ? 0 : 2;
            }
            finally { Environment.SetEnvironmentVariable(BenchmarkInputs.EnvironmentVariable, previous); }
        }
    }
}
