namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Represents a named whole-project public surface within one logical module.</summary>
public sealed class RepositoryContract
{
    internal RepositoryContract(string moduleId, string name, IEnumerable<RepositoryProject> projects)
    {
        Id = moduleId + "/" + name;
        ModuleId = moduleId;
        Name = name;
        Projects = RepositoryData.Unique(projects, project => project.Id);
    }

    /// <summary>Gets the module-qualified identity MODULE/CONTRACT.</summary>
    public string Id { get; }
    /// <summary>Gets the owning module identity.</summary>
    public string ModuleId { get; }
    /// <summary>Gets the contract name local to its module.</summary>
    public string Name { get; }
    /// <summary>Gets the immutable projects constituting this contract.</summary>
    public IReadOnlyList<RepositoryProject> Projects { get; }
}
