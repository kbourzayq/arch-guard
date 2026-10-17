namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Groups projects across architectural layers into one policy-defined logical module.</summary>
public sealed class RepositoryModule
{
    internal RepositoryModule(string id, IEnumerable<RepositoryProject> projects, IEnumerable<RepositoryContract> contracts)
    {
        Id = id;
        Projects = RepositoryData.Unique(projects, project => project.Id);
        Contracts = RepositoryData.Unique(contracts, contract => contract.Id);
    }

    /// <summary>Gets the policy-defined module identity.</summary>
    public string Id { get; }
    /// <summary>Gets immutable member projects spanning any number of layers.</summary>
    public IReadOnlyList<RepositoryProject> Projects { get; }
    /// <summary>Gets immutable public contracts scoped to this module.</summary>
    public IReadOnlyList<RepositoryContract> Contracts { get; }
}
