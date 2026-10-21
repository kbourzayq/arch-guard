namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Resolved layer membership and direct outgoing constraints.</summary>
/// <param name="Id">Case-sensitive layer ID.</param>
/// <param name="Projects">Resolved member paths.</param>
/// <param name="AllowedDependencies">Permitted outgoing layer IDs.</param>
/// <param name="ForbiddenAssemblies">AG005 exact assembly restrictions.</param>
/// <param name="ForbiddenNamespaces">AG005 namespace restrictions including descendants.</param>
/// <param name="DomainInfrastructure">Optional AG006 restrictions.</param>
internal sealed record LayerContext(
    string Id,
    IReadOnlyList<string> Projects,
    IReadOnlyList<string> AllowedDependencies,
    IReadOnlyList<string> ForbiddenAssemblies,
    IReadOnlyList<string> ForbiddenNamespaces,
    InfrastructureContext? DomainInfrastructure);
