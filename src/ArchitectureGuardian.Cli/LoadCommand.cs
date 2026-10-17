using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Analysis;

namespace ArchitectureGuardian.Cli;

/// <summary>Maps workspace-loading arguments, cancellation and coverage to console output.</summary>
internal static class LoadCommand
{
    internal static async Task<int> RunAsync(string[] args)
    {
        if (args.Length == 0 || args[0].StartsWith('-'))
        {
            Console.Error.WriteLine("load requires a .sln, .slnx or .csproj input. Use --help for usage.");
            return 2;
        }

        var trusted = false;
        var configuration = "Debug";
        string? framework = null;
        var seen = new HashSet<string>(StringComparer.Ordinal);
        for (var index = 1; index < args.Length; index++)
        {
            var option = args[index];
            if (!seen.Add(option))
            {
                Console.Error.WriteLine($"Duplicate load option: {option}");
                return 2;
            }

            if (option == "--trusted")
            {
                trusted = true;
            }
            else if (option is "--configuration" or "--framework" && index + 1 < args.Length &&
                !string.IsNullOrWhiteSpace(args[index + 1]) && !args[index + 1].StartsWith('-'))
            {
                var value = args[++index];
                if (option == "--configuration")
                {
                    configuration = value;
                }
                else
                {
                    framework = value;
                }
            }
            else
            {
                Console.Error.WriteLine($"Invalid load option or missing value: {option}");
                return 2;
            }
        }

        using var cancellation = new CancellationTokenSource();
        ConsoleCancelEventHandler cancel = (_, args) =>
        {
            args.Cancel = true;
            cancellation.Cancel();
        };
        Console.CancelKeyPress += cancel;
        try
        {
            var request = new WorkspaceLoadRequest(args[0], trusted, configuration, framework);
            using var result = await new MSBuildWorkspaceLoader().LoadAsync(request, cancellation.Token);
            if (result.Status != AnalysisStatus.Completed)
            {
                Console.Error.WriteLine($"Workspace loading {result.Status.ToString().ToLowerInvariant()}.");
                foreach (var issue in result.Issues)
                {
                    Console.Error.WriteLine($"{issue.Code}: {issue.Message}");
                }

                return 2;
            }

            Console.WriteLine($"Workspace loaded: {result.Solution!.ProjectIds.Count} project(s), configuration {configuration}. No architecture rules evaluated.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            Console.Error.WriteLine($"Invalid load input: {exception.Message}");
            return 2;
        }
        finally
        {
            Console.CancelKeyPress -= cancel;
        }
    }
}
