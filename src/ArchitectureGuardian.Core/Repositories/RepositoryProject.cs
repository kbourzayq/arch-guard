using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Represents one evaluated project and its output assembly for a single build context.</summary>
public sealed class RepositoryProject
{
    /// <summary>Creates a project identified by its policy-relative path rather than its display or assembly name.</summary>
    /// <param name="path">The project path relative to the policy directory.</param>
    /// <param name="name">The workspace display name.</param>
    /// <param name="assembly">The normalized source assembly.</param>
    /// <exception cref="ArgumentException">The path/name is invalid or the assembly belongs to another project.</exception>
    public RepositoryProject(string path, string name, RepositoryAssembly assembly)
    {
        Id = ProjectPattern.NormalizeIdentity(path);
        ArgumentException.ThrowIfNullOrWhiteSpace(name);
        ArgumentNullException.ThrowIfNull(assembly);
        if (assembly.ProjectPath != Id)
        {
            throw new ArgumentException("The assembly must belong to this project.", nameof(assembly));
        }

        Name = name;
        Assembly = assembly;
    }

    /// <summary>Gets the slash-separated, policy-relative project path.</summary>
    public string Id { get; }
    /// <summary>Gets the workspace display name; it does not determine ownership.</summary>
    public string Name { get; }
    /// <summary>Gets the output assembly and source declarations.</summary>
    public RepositoryAssembly Assembly { get; }
}
