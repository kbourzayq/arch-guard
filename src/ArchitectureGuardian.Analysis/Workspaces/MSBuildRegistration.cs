using Microsoft.Build.Locator;

namespace ArchitectureGuardian.Analysis.Workspaces;

/// <summary>Serializes process-wide SDK registration before any MSBuild assembly is loaded.</summary>
internal static class MSBuildRegistration
{
    private static readonly object Sync = new();
    private static string? _registeredPath;

    /// <summary>Registers exactly the SDK selected by the input, or rejects incompatible process state.</summary>
    internal static void EnsureRegistered(string workingDirectory, Version sdkVersion)
    {
        lock (Sync)
        {
            var instance = MSBuildLocator.QueryVisualStudioInstances(new VisualStudioInstanceQueryOptions
            {
                DiscoveryTypes = DiscoveryType.DotNetSdk,
                WorkingDirectory = workingDirectory,
            }).FirstOrDefault(instance => instance.Version == sdkVersion) ?? throw new InvalidOperationException(
                "No compatible .NET SDK was found. Install the SDK required by the input's global.json.");

            if (_registeredPath is not null)
            {
                if (!StringComparer.OrdinalIgnoreCase.Equals(_registeredPath, instance.MSBuildPath))
                {
                    throw new InvalidOperationException("This input requires a different SDK. Load it in a new process.");
                }

                return;
            }

            if (MSBuildLocator.IsRegistered)
            {
                throw new InvalidOperationException("MSBuild was registered by another host. Use a dedicated workspace-loading process.");
            }

            MSBuildLocator.RegisterInstance(instance);
            _registeredPath = instance.MSBuildPath;
        }
    }
}
