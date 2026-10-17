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
            await Console.Error.WriteLineAsync("load requires a .sln, .slnx or .csproj input. Use --help for usage.");
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
                await Console.Error.WriteLineAsync($"Duplicate load option: {option}");
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
                await Console.Error.WriteLineAsync($"Invalid load option or missing value: {option}");
                return 2;
            }
        }

        using var cancellation = new ConsoleCancellation();
        try
        {
            var request = new WorkspaceLoadRequest(args[0], trusted, configuration, framework);
            using var result = await new MSBuildWorkspaceLoader().LoadAsync(request, cancellation.Token);
            if (result.Status != AnalysisStatus.Completed)
            {
                await Console.Error.WriteLineAsync($"Workspace loading {result.Status.ToString().ToLowerInvariant()}.");
                foreach (var issue in result.Issues)
                {
                    await Console.Error.WriteLineAsync($"{issue.Code}: {issue.Message}");
                }

                return 2;
            }

            await Console.Out.WriteLineAsync($"Workspace loaded: {result.Solution!.ProjectIds.Count} project(s), configuration {configuration}. No architecture rules evaluated.");
            return 0;
        }
        catch (Exception exception) when (exception is ArgumentException or NotSupportedException or IOException)
        {
            await Console.Error.WriteLineAsync($"Invalid load input: {exception.Message}");
            return 2;
        }
    }

    /// <summary>Owns the console handler and prevents cancellation from racing with source disposal.</summary>
    private sealed class ConsoleCancellation : IDisposable
    {
        private readonly CancellationTokenSource _source = new();
        private readonly object _sync = new();
        private bool _disposed;

        /// <summary>Creates the cancellation source and subscribes its owning handler.</summary>
        public ConsoleCancellation()
        {
            Token = _source.Token;
            Console.CancelKeyPress += HandleCancel;
        }

        /// <summary>Gets the token supplied to the workspace loader.</summary>
        public CancellationToken Token { get; }

        /// <summary>Unsubscribes the handler and disposes its source after any active cancellation finishes.</summary>
        public void Dispose()
        {
            Console.CancelKeyPress -= HandleCancel;
            lock (_sync)
            {
                if (_disposed)
                {
                    return;
                }

                _disposed = true;
                _source.Dispose();
            }
        }

        private void HandleCancel(object? sender, ConsoleCancelEventArgs args)
        {
            args.Cancel = true;
            lock (_sync)
            {
                // Unsubscription cannot retract an event invocation that was already queued.
                if (!_disposed)
                {
                    _source.Cancel();
                }
            }
        }
    }
}
