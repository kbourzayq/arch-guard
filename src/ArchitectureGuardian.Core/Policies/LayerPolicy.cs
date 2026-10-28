#if PORTABLE_POLICY
namespace ArchitectureGuardian.Policy.Documents;
#else
namespace ArchitectureGuardian.Core.Policies;
#endif

/// <summary>Declares an architectural layer independently of logical modules.</summary>
public sealed class LayerPolicy
{
    /// <summary>Snapshots a layer; omitted dependency permissions deny cross-layer access.</summary>
    /// <param name="id">The case-sensitive layer identity.</param>
    /// <param name="projects">One or more project selectors.</param>
    /// <param name="allowedDependencies">Exact declared layer IDs, or null for none.</param>
    /// <param name="forbiddenAssemblies">Exact assembly simple names forbidden at semantic use sites, or null for none.</param>
    /// <param name="forbiddenNamespaces">Namespace names including their descendants forbidden at semantic use sites, or null for none.</param>
    /// <param name="domainInfrastructure">Optional AG006 restrictions for this source layer, independent of its name.</param>
    /// <exception cref="ArgumentException">An identity or selector is invalid, or a collection contains null or duplicates.</exception>
    public LayerPolicy(string id, IEnumerable<string> projects, IEnumerable<string>? allowedDependencies = null, IEnumerable<string>? forbiddenAssemblies = null,
        IEnumerable<string>? forbiddenNamespaces = null, DomainInfrastructurePolicy? domainInfrastructure = null)
    {
        DomainInfrastructure = domainInfrastructure;
        Id = PolicyData.Id(id);
        Projects = PolicyData.Patterns(projects);
        AllowedDependencies = PolicyData.Strings(allowedDependencies ?? []);
        ForbiddenAssemblies = PolicyData.ReferenceNames(forbiddenAssemblies ?? [], assembly: true);
        ForbiddenNamespaces = PolicyData.ReferenceNames(forbiddenNamespaces ?? [], assembly: false);
    }

    /// <summary>Gets the layer identity.</summary>
    public string Id { get; }

    /// <summary>Gets the selectors defining the layer.</summary>
    public IReadOnlyList<ProjectPattern> Projects { get; }

    /// <summary>Gets the permitted outgoing layer IDs.</summary>
    public IReadOnlyList<string> AllowedDependencies { get; }

    /// <summary>Gets exact, case-sensitive assembly simple names forbidden by AG005.</summary>
    public IReadOnlyList<string> ForbiddenAssemblies { get; }

    /// <summary>Gets case-sensitive namespace restrictions; each includes its child namespaces.</summary>
    public IReadOnlyList<string> ForbiddenNamespaces { get; }
    /// <summary>Gets the optional layer-scoped AG006 technology restrictions and exceptions.</summary>
    public DomainInfrastructurePolicy? DomainInfrastructure { get; }
}
