using System.Reflection;

namespace ArchitectureGuardian.Cli;

/// <summary>Provides the console entry point and maps supported arguments to output and exit codes.</summary>
internal static class Program
{
    /// <summary>Prints help or version information, or reports unsupported arguments.</summary>
    /// <param name="args">The command-line arguments supplied by the process host.</param>
    /// <returns>Zero for help or version output; two for unsupported arguments.</returns>
    private static int Main(string[] args)
    {
        if (args is [] or ["--help"] or ["-h"])
        {
            Console.WriteLine("""
                Architecture Guardian for .NET

                Usage: arch-guard [--help | --version]

                  -h, --help   Show usage
                  --version    Show version

                The solution scaffold is ready. Analysis commands are not available yet.
                """);
            return 0;
        }

        if (args is ["--version"])
        {
            Console.WriteLine($"Architecture Guardian {Assembly.GetExecutingAssembly().GetName().Version?.ToString(3)}");
            return 0;
        }

        // Unsupported analysis commands must fail explicitly until a real analysis use case is available.
        Console.Error.WriteLine($"Unknown arguments: {string.Join(' ', args)}");
        Console.Error.WriteLine("Use --help for usage.");
        return 2;
    }
}
