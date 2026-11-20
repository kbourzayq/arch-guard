namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>Identifies where a bound type, or one of its base types, is declared.</summary>
/// <param name="Assembly">The bound assembly simple name.</param>
/// <param name="Namespace">The bound namespace, empty for the global namespace.</param>
public sealed record AspNetCoreTypeOrigin(string Assembly, string Namespace);
