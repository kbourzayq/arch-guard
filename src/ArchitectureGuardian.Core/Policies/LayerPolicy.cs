namespace ArchitectureGuardian.Core.Policies;

/// <summary>Declares an architectural layer independently of logical modules.</summary>
public sealed class LayerPolicy
{
    /// <summary>Snapshots a layer; omitted dependency permissions deny cross-layer access.</summary>
    /// <param name="id">The case-sensitive layer identity.</param>
    /// <param name="projects">One or more project selectors.</param>
    /// <param name="allowedDependencies">Exact declared layer IDs, or null for none.</param>
    /// <exception cref="ArgumentException">An identity or selector is invalid, or a collection contains null or duplicates.</exception>
    public LayerPolicy(string id, IEnumerable<string> projects, IEnumerable<string>? allowedDependencies = null)
    {
        Id = PolicyData.Id(id);
        Projects = PolicyData.Patterns(projects);
        AllowedDependencies = PolicyData.Strings(allowedDependencies ?? []);
    }

    /// <summary>Gets the layer identity.</summary>
    public string Id { get; }

    /// <summary>Gets the selectors defining the layer.</summary>
    public IReadOnlyList<ProjectPattern> Projects { get; }

    /// <summary>Gets the permitted outgoing layer IDs.</summary>
    public IReadOnlyList<string> AllowedDependencies { get; }
}
