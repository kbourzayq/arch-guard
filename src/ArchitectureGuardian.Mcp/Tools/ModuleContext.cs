namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Resolved module membership and direct outgoing constraints.</summary>
/// <param name="Id">Case-sensitive module ID.</param>
/// <param name="Projects">Resolved member paths.</param>
/// <param name="Contracts">Whole-project public contracts.</param>
/// <param name="AllowedDependencies">Direct module or contract permissions.</param>
/// <param name="ForbiddenAssemblies">AG005 exact assembly restrictions.</param>
/// <param name="ForbiddenNamespaces">AG005 namespace restrictions including descendants.</param>
internal sealed record ModuleContext(
    string Id,
    IReadOnlyList<string> Projects,
    IReadOnlyList<ContractContext> Contracts,
    IReadOnlyList<string> AllowedDependencies,
    IReadOnlyList<string> ForbiddenAssemblies,
    IReadOnlyList<string> ForbiddenNamespaces);
