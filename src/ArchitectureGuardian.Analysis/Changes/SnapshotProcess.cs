using System.Diagnostics;

namespace ArchitectureGuardian.Analysis.Changes;

/// <summary>Runs captured Git/SDK operations without shell interpretation or protocol-stream output.</summary>
internal static class SnapshotProcess
{
    internal static async Task<string> RunAsync(string command, string directory, IEnumerable<string> arguments,
        CancellationToken cancellationToken, string? indexPath = null)
    {
        var start = new ProcessStartInfo(command)
        {
            WorkingDirectory = directory,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            start.ArgumentList.Add(argument);
        }

        start.Environment["GIT_TERMINAL_PROMPT"] = "0";
        start.Environment["GIT_OPTIONAL_LOCKS"] = "0";
        if (indexPath is not null)
        {
            start.Environment["GIT_INDEX_FILE"] = indexPath;
        }

        start.Environment["MSBUILDDISABLENODEREUSE"] = "1";
        start.Environment["DOTNET_CLI_USE_MSBUILD_SERVER"] = "0";
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var timeout = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        timeout.CancelAfter(TimeSpan.FromMinutes(2));
        using var process = Process.Start(start) ?? throw new IOException($"Could not start {command}.");
        // Child tools must not inherit the live MCP input pipe. Git for Windows inspects
        // inherited pipes during startup and can block behind the server's pending read.
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(timeout.Token);
        var error = process.StandardError.ReadToEndAsync(timeout.Token);
        var waitingFor = "process exit";
        try
        {
            await process.WaitForExitAsync(timeout.Token).ConfigureAwait(false);
            waitingFor = "stdout/stderr EOF";
            await Task.WhenAll(output, error).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            try
            {
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            cancellationToken.ThrowIfCancellationRequested();
            throw new IOException($"{command} {string.Join(' ', start.ArgumentList)} exceeded the two-minute snapshot-operation deadline while waiting for {waitingFor}.");
        }

        if (process.ExitCode != 0)
        {
            throw new IOException(
                $"{command} exited {process.ExitCode}: {await error.ConfigureAwait(false)} {await output.ConfigureAwait(false)}"
                    .Trim());
        }

        return await output.ConfigureAwait(false);
    }
}
