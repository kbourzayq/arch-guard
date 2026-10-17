namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Groups projects by policy-defined architectural layer across logical modules.</summary>
public sealed class RepositoryLayer
{
    internal RepositoryLayer(string id, IEnumerable<RepositoryProject> projects)
    {
        Id = id;
        Projects = RepositoryData.Unique(projects, project => project.Id);
    }

    /// <summary>Gets the policy-defined layer identity.</summary>
    public string Id { get; }
    /// <summary>Gets immutable member projects across logical modules.</summary>
    public IReadOnlyList<RepositoryProject> Projects { get; }
}
