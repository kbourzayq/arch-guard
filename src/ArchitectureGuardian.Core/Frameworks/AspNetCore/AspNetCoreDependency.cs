using ArchitectureGuardian.Core.Diagnostics;

namespace ArchitectureGuardian.Core.Frameworks.AspNetCore;

/// <summary>A type that an endpoint declares or uses, with the bound identity that rules need.</summary>
public sealed record AspNetCoreDependency
{
    /// <summary>Creates a semantic dependency of an endpoint.</summary>
    /// <param name="symbol">The display name of the bound type.</param>
    /// <param name="lineage">The type's origin followed by its base-type origins; at least one entry.</param>
    /// <param name="isInjected">Whether the endpoint receives the type from dependency injection.</param>
    /// <param name="isAbstraction">Whether the type is an interface or abstract class.</param>
    /// <param name="declaringProject">The policy-relative source project declaring the type, or null for metadata types.</param>
    /// <param name="location">The use site, when available.</param>
    /// <exception cref="ArgumentException">The symbol is blank or the lineage is empty.</exception>
    public AspNetCoreDependency(string symbol, IEnumerable<AspNetCoreTypeOrigin> lineage, bool isInjected, bool isAbstraction,
        string? declaringProject = null, SourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(symbol);
        ArgumentNullException.ThrowIfNull(lineage);
        Symbol = symbol;
        Lineage = Array.AsReadOnly(lineage.ToArray());
        if (Lineage.Count == 0)
        {
            throw new ArgumentException("A dependency requires at least its own origin.", nameof(lineage));
        }

        IsInjected = isInjected;
        IsAbstraction = isAbstraction;
        DeclaringProject = declaringProject;
        Location = location;
    }

    /// <summary>Gets the display name of the bound type.</summary>
    public string Symbol { get; }
    /// <summary>Gets the type's own origin followed by its base types' origins.</summary>
    public IReadOnlyList<AspNetCoreTypeOrigin> Lineage { get; }
    /// <summary>Gets whether the endpoint receives this type from dependency injection.</summary>
    public bool IsInjected { get; }
    /// <summary>Gets whether the type is an interface or abstract class.</summary>
    public bool IsAbstraction { get; }
    /// <summary>Gets the source project that declares the type, or null for metadata types.</summary>
    public string? DeclaringProject { get; }
    /// <summary>Gets the use site, or null when unavailable.</summary>
    public SourceLocation? Location { get; }
}
