using BenchmarkDotNet.Running;

namespace ArchitectureGuardian.Performance;

/// <summary>Runs the non-shipping BenchmarkDotNet suite prepared by eng/benchmark.py.</summary>
internal static class Program
{
    private static int Main(string[] args)
    {
        var summaries = BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args).ToArray();
        return summaries.Length > 0 && summaries.All(summary => !summary.HasCriticalValidationErrors
            && summary.Reports.All(report => report.Success)) ? 0 : 2;
    }
}
