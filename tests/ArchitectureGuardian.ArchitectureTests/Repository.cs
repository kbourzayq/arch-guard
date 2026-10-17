using System.Diagnostics;

namespace ArchitectureGuardian.ArchitectureTests;

/// <summary>Provides repository discovery and bounded .NET process execution for integration-style tests.</summary>
internal static class Repository
{
    /// <summary>Gets the repository root located by walking upward from the test assembly.</summary>
    public static string Root { get; } = FindRoot();

    /// <summary>Executes the .NET CLI from the repository root and captures both output streams.</summary>
    /// <param name="arguments">Individual arguments passed without shell interpretation.</param>
    /// <returns>The process exit code, standard output and standard error after completion.</returns>
    /// <exception cref="InvalidOperationException">The .NET process cannot be started.</exception>
    /// <exception cref="TimeoutException">The process exceeds the 30-second test deadline and is terminated.</exception>
    public static async Task<CommandResult> Dotnet(params string[] arguments)
    {
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet.");
        // Drain both pipes concurrently so a child process cannot block while waiting for an unread buffer.
        var output = process.StandardOutput.ReadToEndAsync();
        var error = process.StandardError.ReadToEndAsync();
        using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(30));

        try
        {
            await process.WaitForExitAsync(timeout.Token);
        }
        catch (OperationCanceledException)
        {
            // Stop child processes too, so a timed-out build cannot outlive its test.
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} timed out.");
        }

        return new CommandResult(process.ExitCode, await output, await error);
    }

    /// <summary>Finds the checkout containing the test assembly by its solution marker.</summary>
    /// <returns>The absolute directory containing the repository's solution file.</returns>
    /// <exception cref="DirectoryNotFoundException">No ancestor contains the expected solution marker.</exception>
    private static string FindRoot()
    {
        for (var directory = new DirectoryInfo(AppContext.BaseDirectory); directory is not null; directory = directory.Parent)
        {
            if (File.Exists(Path.Combine(directory.FullName, "ArchitectureGuardian.slnx")))
            {
                return directory.FullName;
            }
        }

        throw new DirectoryNotFoundException("Could not find ArchitectureGuardian.slnx.");
    }
}

/// <summary>Captures process completion separately from standard output and error for test assertions.</summary>
/// <param name="ExitCode">The process exit status.</param>
/// <param name="Output">The complete standard output stream.</param>
/// <param name="Error">The complete standard error stream.</param>
internal sealed record CommandResult(int ExitCode, string Output, string Error);
