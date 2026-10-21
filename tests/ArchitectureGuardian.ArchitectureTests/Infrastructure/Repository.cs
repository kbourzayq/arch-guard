using System.Diagnostics;

namespace ArchitectureGuardian.ArchitectureTests.Infrastructure;

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
    public static Task<CommandResult> Dotnet(params string[] arguments) => Dotnet(TimeSpan.FromSeconds(30), arguments);

    /// <summary>Executes the .NET CLI with an explicit deadline for integration scenarios that perform workspace evaluation.</summary>
    /// <param name="timeoutDuration">The positive process deadline; the entire process tree is terminated when it expires.</param>
    /// <param name="arguments">Individual arguments passed without shell interpretation.</param>
    /// <returns>The process exit code, standard output and standard error after completion.</returns>
    /// <exception cref="ArgumentOutOfRangeException">The deadline is not positive.</exception>
    /// <exception cref="InvalidOperationException">The .NET process cannot be started.</exception>
    /// <exception cref="TimeoutException">The process exceeds its test deadline and is terminated.</exception>
    public static async Task<CommandResult> Dotnet(TimeSpan timeoutDuration, params string[] arguments)
    {
        ArgumentOutOfRangeException.ThrowIfLessThanOrEqual(timeoutDuration, TimeSpan.Zero);
        var startInfo = new ProcessStartInfo("dotnet")
        {
            WorkingDirectory = Root,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            Environment =
            {
                // Build workers must not outlive a command and retain its redirected pipe handles.
                ["MSBUILDDISABLENODEREUSE"] = "1",
                ["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0"
            }
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException("Could not start dotnet.");
        using var timeout = new CancellationTokenSource(timeoutDuration);
        // The deadline includes draining pipes, which can remain open after the parent exits.
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);

        try
        {
            await process.WaitForExitAsync(timeout.Token);
            await Task.WhenAll(output, error);
        }
        catch (OperationCanceledException)
        {
            // Stop child processes too, so a timed-out build cannot outlive its test.
            if (!process.HasExited) { process.Kill(entireProcessTree: true); }
            // Cleanup has its own bound; the command's deadline has already expired.
            await process.WaitForExitAsync(CancellationToken.None).WaitAsync(TimeSpan.FromSeconds(5), CancellationToken.None);
            throw new TimeoutException($"dotnet {string.Join(' ', arguments)} timed out after {timeoutDuration}.");
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
