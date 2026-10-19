namespace ArchitectureGuardian.Core.Policies;

/// <summary>Declares a logical module, its public contracts and explicitly permitted outgoing dependencies.</summary>
public sealed class ModulePolicy
{
    /// <summary>Snapshots a module definition; omitted dependency permissions deny cross-module access.</summary>
    /// <param name="id">The case-sensitive module identity.</param>
    /// <param name="projects">One or more project selectors.</param>
    /// <param name="contracts">Public contract declarations, or null for none.</param>
    /// <param name="allowedDependencies">Exact module:ID or contract:MODULE/ID references, or null for none.</param>
    /// <param name="forbiddenAssemblies">Exact assembly simple names forbidden at semantic use sites, or null for none.</param>
    /// <param name="forbiddenNamespaces">Namespace names including their descendants forbidden at semantic use sites, or null for none.</param>
    /// <exception cref="ArgumentException">An identity or selector is invalid, or a collection contains null or duplicates.</exception>
    public ModulePolicy(string id, IEnumerable<string> projects, IEnumerable<ContractPolicy>? contracts = null,
        IEnumerable<string>? allowedDependencies = null, IEnumerable<string>? forbiddenAssemblies = null,
        IEnumerable<string>? forbiddenNamespaces = null)
    {
        Id = PolicyData.Id(id);
        Projects = PolicyData.Patterns(projects);
        Contracts = PolicyData.Unique(contracts ?? [], contract => contract.Id, "contract");
        AllowedDependencies = PolicyData.Strings(allowedDependencies ?? []);
        ForbiddenAssemblies = PolicyData.ReferenceNames(forbiddenAssemblies ?? [], assembly: true);
        ForbiddenNamespaces = PolicyData.ReferenceNames(forbiddenNamespaces ?? [], assembly: false);
    }

    /// <summary>Gets the module identity.</summary>
    public string Id { get; }

    /// <summary>Gets the project selectors defining the module.</summary>
    public IReadOnlyList<ProjectPattern> Projects { get; }

    /// <summary>Gets the declared public contracts.</summary>
    public IReadOnlyList<ContractPolicy> Contracts { get; }

    /// <summary>Gets explicit outgoing module or public-contract permissions.</summary>
    public IReadOnlyList<string> AllowedDependencies { get; }

    /// <summary>Gets exact, case-sensitive assembly simple names forbidden by AG005.</summary>
    public IReadOnlyList<string> ForbiddenAssemblies { get; }

    /// <summary>Gets case-sensitive namespace restrictions; each includes its child namespaces.</summary>
    public IReadOnlyList<string> ForbiddenNamespaces { get; }
}
