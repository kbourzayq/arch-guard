namespace ArchitectureGuardian.Mcp.Hosting;

/// <summary>Server-owned configuration available to tools through the standard options service.</summary>
internal sealed class WorkspaceOptions
{
    public string Root { get; set; } = "";
    public bool Trusted { get; set; }
}
