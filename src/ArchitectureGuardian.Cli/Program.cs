using System.Reflection;

namespace ArchitectureGuardian.Cli;

/// <summary>Provides the console entry point and maps supported arguments to output and exit codes.</summary>
internal static class Program
{
    /// <summary>Handles informational arguments and workspace loading.</summary>
    /// <param name="args">The command-line arguments supplied by the process host.</param>
    /// <returns>Zero for successful commands; two for invalid arguments or incomplete/cancelled loading.</returns>
    private static async Task<int> Main(string[] args)
    {
        if (args is [] or ["--help"] or ["-h"])
        {
            Console.WriteLine("""
                Architecture Guardian for .NET

                Usage: arch-guard [--help | --version]
                       arch-guard load <input.sln|input.slnx|input.csproj> --trusted
                           [--configuration Debug|Release] [--framework <tfm>]

                  -h, --help   Show usage
                  --version    Show version

                load validates workspace context; it does not evaluate architecture rules.
                MSBuild can execute repository tasks. Isolate untrusted repositories first.
                """);
            return 0;
        }

        if (args is ["--version"])
        {
            Console.WriteLine($"Architecture Guardian {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");
            return 0;
        }

        if (args is ["load", ..])
        {
            return await LoadCommand.RunAsync(args[1..]);
        }

        // Unsupported analysis commands must fail explicitly until a real analysis use case is available.
        Console.Error.WriteLine($"Unknown arguments: {string.Join(' ', args)}");
        Console.Error.WriteLine("Use --help for usage.");
        return 2;
    }
}
