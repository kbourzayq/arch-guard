using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Represents a project's output assembly and its source namespaces; referenced metadata is excluded.</summary>
public sealed class RepositoryAssembly
{
    /// <summary>Snapshots namespaces and verifies their project ownership.</summary>
    /// <param name="projectPath">The policy-relative project identity.</param>
    /// <param name="name">The evaluated assembly name, which need not equal the project name.</param>
    /// <param name="namespaces">Source namespaces, including the global namespace.</param>
    /// <exception cref="ArgumentException">An identity is invalid, duplicated or belongs to another project.</exception>
    public RepositoryAssembly(string projectPath, string name, IEnumerable<RepositoryNamespace> namespaces)
    {
        ProjectPath = ProjectPattern.NormalizeIdentity(projectPath);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        Name = name;
        Namespaces = RepositoryData.Unique(namespaces, item => item.Id);
        if (Namespaces.Any(item => item.ProjectPath != ProjectPath))
        {
            throw new ArgumentException("Assembly namespaces must belong to its project.", nameof(namespaces));
        }

        Id = RepositoryData.Identity("assembly", ProjectPath, Name);
    }

    /// <summary>Gets the stable project-scoped assembly identity, excluding version and output paths.</summary>
    public string Id { get; }
    /// <summary>Gets the producing project identity.</summary>
    public string ProjectPath { get; }
    /// <summary>Gets the evaluated assembly name.</summary>
    public string Name { get; }
    /// <summary>Gets immutable source namespaces in ordinal identity order.</summary>
    public IReadOnlyList<RepositoryNamespace> Namespaces { get; }
}
