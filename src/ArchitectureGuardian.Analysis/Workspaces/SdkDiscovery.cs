using System.Diagnostics;

namespace ArchitectureGuardian.Analysis.Workspaces;

/// <summary>Validates SDK selection in a child process so native discovery errors cannot write to host output streams.</summary>
internal static class SdkDiscovery
{
    /// <summary>Resolves the input directory's SDK while capturing diagnostics and honoring cancellation.</summary>
    internal static async Task<Version> ResolveAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var sdkVersion = (await QueryAsync("--version", workingDirectory, cancellationToken).ConfigureAwait(false)).Trim();
        if (!Version.TryParse(sdkVersion.Split('-')[0], out var version))
        {
            throw new InvalidOperationException("The input's .NET SDK version could not be read. Install the SDK required by global.json.");
        }
        return version;
    }

    /// <summary>Queries host metadata only; never invokes project evaluation, restore or build.</summary>
    internal static async Task<string> QueryAsync(string argument, string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardInput = true,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add(argument);
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start .NET SDK discovery.");
        process.StandardInput.Close();
        var output = process.StandardOutput.ReadToEndAsync(cancellationToken);
        var error = process.StandardError.ReadToEndAsync(cancellationToken);
        try
        {
            await process.WaitForExitAsync(cancellationToken).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            if (!process.HasExited)
            {
                process.Kill(entireProcessTree: true);
            }

            // Finish draining the cancelled reads before releasing the process and its redirected streams.
            try
            {
                await Task.WhenAll(output, error).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
            }

            throw;
        }

        var response = await output.ConfigureAwait(false);
        var diagnostics = await error.ConfigureAwait(false);
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException($"The input's .NET host/SDK is unavailable. Install the SDK required by global.json. {diagnostics} {response}".Trim());
        }

        return response;
    }
}
