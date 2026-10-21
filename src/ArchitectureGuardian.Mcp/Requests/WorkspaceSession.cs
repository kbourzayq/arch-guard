using ArchitectureGuardian.Analysis.Workspaces;
using ArchitectureGuardian.Core.Policies;
using ArchitectureGuardian.Mcp.Hosting;

namespace ArchitectureGuardian.Mcp.Requests;

/// <summary>Adapts operator-owned workspace settings to shared services; contains no architecture rule logic.</summary>
internal sealed class WorkspaceSession(ServerOptions options, IPolicyLoader policies)
{
    internal Task<ArchitecturePolicy> LoadPolicyAsync(string path, CancellationToken cancellationToken)
    {
        var full = options.Workspace.ResolveFile(path);
        if (new FileInfo(full).Length > ServerOptions.MaxMessageBytes)
        {
            throw new ArgumentException("Policy exceeds the 1 MiB limit.");
        }

        return policies.LoadAsync(full, cancellationToken);
    }

    internal WorkspaceLoadRequest CreateLoadRequest(string path, string configuration = "Debug", string? framework = null) =>
        new(options.Workspace.ResolveFile(path), options.TrustWorkspace, configuration, framework);
}
