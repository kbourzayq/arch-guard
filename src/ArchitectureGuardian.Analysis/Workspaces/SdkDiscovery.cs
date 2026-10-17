using System.Diagnostics;

namespace ArchitectureGuardian.Analysis.Workspaces;

/// <summary>Validates SDK selection in a child process so native discovery errors cannot write to host output streams.</summary>
internal static class SdkDiscovery
{
    /// <summary>Resolves the input directory's SDK while capturing diagnostics and honoring cancellation.</summary>
    internal static async Task<Version> ResolveAsync(string workingDirectory, CancellationToken cancellationToken)
    {
        var start = new ProcessStartInfo(Environment.GetEnvironmentVariable("DOTNET_HOST_PATH") ?? "dotnet")
        {
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
        };
        start.ArgumentList.Add("--version");
        start.Environment["DOTNET_NOLOGO"] = "1";
        start.Environment["DOTNET_CLI_TELEMETRY_OPTOUT"] = "1";
        using var process = Process.Start(start) ?? throw new InvalidOperationException("Could not start .NET SDK discovery.");
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

        var sdkVersion = (await output.ConfigureAwait(false)).Trim();
        var diagnostics = await error.ConfigureAwait(false);
        if (process.ExitCode != 0 || !Version.TryParse(sdkVersion.Split('-')[0], out var version))
        {
            throw new InvalidOperationException($"The input's .NET SDK is unavailable. Install the SDK required by global.json. {diagnostics} {sdkVersion}".Trim());
        }

        return version;
    }
}
