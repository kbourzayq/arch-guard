namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Resolved project ownership in the selected context.</summary>
/// <param name="Path">Policy-relative project path.</param>
/// <param name="Module">Owning module ID.</param>
/// <param name="Layer">Owning layer ID.</param>
/// <param name="Contract">Optional module-scoped public contract ID.</param>
internal sealed record ProjectContext(
    string Path,
    string Module,
    string Layer,
    string? Contract);
