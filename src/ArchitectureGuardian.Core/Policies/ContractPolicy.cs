#if PORTABLE_POLICY
namespace ArchitectureGuardian.Policy.Documents;
#else
namespace ArchitectureGuardian.Core.Policies;
#endif

/// <summary>Declares a public contract as a named subset of its module's projects.</summary>
public sealed class ContractPolicy
{
    /// <summary>Snapshots a contract identity and its project selectors.</summary>
    /// <param name="id">A case-sensitive identifier local to the owning module.</param>
    /// <param name="projects">One or more project selectors.</param>
    /// <exception cref="ArgumentException">The ID or selectors are invalid, empty or duplicated.</exception>
    public ContractPolicy(string id, IEnumerable<string> projects)
    {
        Id = PolicyData.Id(id);
        Projects = PolicyData.Patterns(projects);
    }

    /// <summary>Gets the contract's local identifier.</summary>
    public string Id { get; }

    /// <summary>Gets the immutable project selectors.</summary>
    public IReadOnlyList<ProjectPattern> Projects { get; }
}
