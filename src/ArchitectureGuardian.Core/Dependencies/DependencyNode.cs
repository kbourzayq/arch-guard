using ArchitectureGuardian.Core.Canonicalization;
using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Dependencies;

/// <summary>A neutral graph endpoint with a stable, level-scoped identity.</summary>
public sealed record DependencyNode
{
    /// <summary>Creates an endpoint; metadata endpoints outside the repository have no project path.</summary>
    /// <param name="kind">The architectural level.</param>
    /// <param name="identity">The normalized repository identity or assembly-qualified metadata identity.</param>
    /// <param name="name">The display name; empty is allowed for the global namespace.</param>
    /// <param name="projectPath">The owning policy-relative source project, when available.</param>
    public DependencyNode(DependencyKind kind, string identity, string name, string? projectPath = null)
    {
        if (!Enum.IsDefined(kind))
        {
            throw new ArgumentOutOfRangeException(nameof(kind));
        }

        ArgumentException.ThrowIfNullOrWhiteSpace(identity);
        ArgumentNullException.ThrowIfNull(name);
        Kind = kind;
        Identity = identity;
        Name = name;
        ProjectPath = projectPath is null ? null : ProjectPattern.NormalizeIdentity(projectPath);
        Id = CanonicalData.Hash(CanonicalData.Encode(kind.ToString(), identity));
    }

    /// <summary>Gets the stable graph identity, scoped by architectural level.</summary>
    public string Id { get; }
    /// <summary>Gets the architectural level.</summary>
    public DependencyKind Kind { get; }
    /// <summary>Gets the original normalized identity.</summary>
    public string Identity { get; }
    /// <summary>Gets the display name.</summary>
    public string Name { get; }
    /// <summary>Gets the owning source project, or null for external metadata and logical aggregates.</summary>
    public string? ProjectPath { get; }
}
