using ArchitectureGuardian.Core.Canonicalization;

namespace ArchitectureGuardian.Core.Dependencies;

/// <summary>A direct, directional dependency with immutable origin evidence.</summary>
public sealed class DependencyEdge
{
    /// <summary>Creates an edge between endpoints at the same architectural level.</summary>
    /// <param name="source">The dependent endpoint.</param>
    /// <param name="target">The referenced endpoint.</param>
    /// <param name="evidence">One or more facts supporting the edge.</param>
    public DependencyEdge(DependencyNode source, DependencyNode target, IEnumerable<DependencyEvidence> evidence)
    {
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(target);
        if (source.Kind != target.Kind)
        {
            throw new ArgumentException("Dependency endpoints must have the same architectural level.");
        }

        Source = source;
        Target = target;
        Evidence = CanonicalData.Snapshot(evidence, item => item.SortKey).Distinct().ToList().AsReadOnly();
        if (Evidence.Count == 0)
        {
            throw new ArgumentException("A dependency must retain origin evidence.", nameof(evidence));
        }

        Id = CanonicalData.Hash(CanonicalData.Encode(source.Id, target.Id));
    }

    /// <summary>Gets the identity of this direct endpoint pair, independently of its evidence count.</summary>
    public string Id { get; }
    /// <summary>Gets the architectural level of the dependency.</summary>
    public DependencyKind Kind => Source.Kind;
    /// <summary>Gets the dependent endpoint.</summary>
    public DependencyNode Source { get; }
    /// <summary>Gets the referenced endpoint.</summary>
    public DependencyNode Target { get; }
    /// <summary>Gets deduplicated facts in canonical order, including all aggregated origins.</summary>
    public IReadOnlyList<DependencyEvidence> Evidence { get; }
}
