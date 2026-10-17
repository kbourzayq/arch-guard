using ArchitectureGuardian.Core.Canonicalization;

namespace ArchitectureGuardian.Core.Diagnostics;

/// <summary>Records a supporting fact and optional source range that help a consumer verify a finding.</summary>
public sealed record DiagnosticEvidence
{
    /// <summary>Creates evidence without requiring a compiler-specific syntax or symbol object.</summary>
    /// <param name="description">The observed dependency, access or other fact supporting the violation.</param>
    /// <param name="location">The source range establishing that fact, when available.</param>
    /// <exception cref="ArgumentNullException"><paramref name="description"/> is null.</exception>
    /// <exception cref="ArgumentException"><paramref name="description"/> is blank.</exception>
    public DiagnosticEvidence(string description, SourceLocation? location = null)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(description);
        Description = description;
        Location = location;
    }

    /// <summary>Gets the supporting fact in a form that can be rendered by any host.</summary>
    public string Description { get; }

    /// <summary>Gets the source range for the fact, or <see langword="null"/> when unavailable.</summary>
    public SourceLocation? Location { get; }

    internal string CanonicalKey => CanonicalData.Encode(Description, Location?.CanonicalKey);
}
