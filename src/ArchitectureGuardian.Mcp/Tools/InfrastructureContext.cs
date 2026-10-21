namespace ArchitectureGuardian.Mcp.Tools;

/// <summary>Layer-scoped AG006 restrictions and namespace exceptions.</summary>
/// <param name="ForbiddenAssemblies">Exact forbidden assembly names.</param>
/// <param name="ForbiddenNamespaces">Forbidden namespaces including descendants.</param>
/// <param name="AllowedNamespaces">AG006-only namespace exceptions including descendants.</param>
internal sealed record InfrastructureContext(
    IReadOnlyList<string> ForbiddenAssemblies,
    IReadOnlyList<string> ForbiddenNamespaces,
    IReadOnlyList<string> AllowedNamespaces);
