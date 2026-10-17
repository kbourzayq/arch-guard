using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Groups source types in a namespace within one project, independently of similarly named namespaces elsewhere.</summary>
public sealed class RepositoryNamespace
{
    /// <summary>Snapshots types and verifies that each belongs to this project and namespace.</summary>
    /// <param name="projectPath">The policy-relative project identity.</param>
    /// <param name="name">The full namespace name; empty denotes the global namespace.</param>
    /// <param name="types">Explicit source types, including nested types.</param>
    /// <exception cref="ArgumentException">Identities are invalid, duplicated or owned by another scope.</exception>
    public RepositoryNamespace(string projectPath, string name, IEnumerable<RepositoryType> types)
    {
        ProjectPath = ProjectPattern.NormalizeIdentity(projectPath);
        ArgumentNullException.ThrowIfNull(name);
        Name = name;
        Types = RepositoryData.Unique(types, type => type.Id);
        if (Types.Any(type => type.ProjectPath != ProjectPath || type.NamespaceName != Name))
        {
            throw new ArgumentException("Namespace types must belong to the same project and namespace.", nameof(types));
        }

        Id = RepositoryData.Identity("namespace", ProjectPath, Name);
    }

    /// <summary>Gets the stable project-scoped namespace identity.</summary>
    public string Id { get; }
    /// <summary>Gets the owning project identity.</summary>
    public string ProjectPath { get; }
    /// <summary>Gets the full namespace name, or empty for the global namespace.</summary>
    public string Name { get; }
    /// <summary>Gets immutable types in ordinal identity order.</summary>
    public IReadOnlyList<RepositoryType> Types { get; }
}
