using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Captures source declarations without assigning architectural ownership or permissions.</summary>
public sealed class RepositoryInventory
{
    /// <summary>Snapshots projects in ordinal path order; paths are relative to the discovery root.</summary>
    /// <param name="inputPath">The relative solution or project identity.</param>
    /// <param name="projects">Available projects, independently of any approved policy.</param>
    public RepositoryInventory(string inputPath, IEnumerable<RepositoryProject> projects)
    {
        Id = ProjectPattern.NormalizeIdentity(inputPath);
        Projects = RepositoryData.Unique(projects, project => project.Id);
    }

    /// <summary>Gets the relative input identity.</summary>
    public string Id { get; }
    /// <summary>Gets available projects; coverage belongs to the accompanying result.</summary>
    public IReadOnlyList<RepositoryProject> Projects { get; }
}
