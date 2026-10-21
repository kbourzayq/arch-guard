namespace ArchitectureGuardian.Mcp.Hosting;

/// <summary>Operator-owned configuration; protocol requests cannot widen workspace access or grant build trust.</summary>
internal sealed record ServerOptions(WorkspaceRoot Workspace, bool TrustWorkspace)
{
    internal const int MaxMessageBytes = 1024 * 1024;
    internal static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(30);

    internal static ServerOptions Parse(string[] args)
    {
        string? root = null;
        var trusted = false;
        for (var index = 0; index < args.Length; index++)
        {
            switch (args[index])
            {
                case "--workspace-root" when root is null && index + 1 < args.Length:
                    root = args[++index];
                    break;
                case "--trust-workspace" when !trusted:
                    trusted = true;
                    break;
                default:
                    throw new ArgumentException("Usage: ArchitectureGuardian.Mcp --workspace-root <existing-directory> [--trust-workspace]");
            }
        }

        return new(new WorkspaceRoot(root ?? throw new ArgumentException("--workspace-root is required.")), trusted);
    }
}
