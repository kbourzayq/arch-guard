namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>A resolved whole-project public contract.</summary>
/// <param name="Id">Module-scoped contract ID.</param>
/// <param name="Projects">Resolved policy-relative member paths.</param>
internal sealed record ContractContext(
    string Id,
    IReadOnlyList<string> Projects);
