using ArchitectureGuardian.Core.Policies;

namespace ArchitectureGuardian.Core.Repositories;

/// <summary>Captures neutral source declarations and logical ownership for one solution or project input.</summary>
/// <remarks>Coverage belongs to the build result. This model alone does not establish complete analysis or policy compliance.</remarks>
public sealed class RepositoryModel
{
    /// <summary>Snapshots an inventory and resolves ownership using the strict v1 configuration policy.</summary>
    /// <param name="inputPath">The solution or project input relative to the policy directory.</param>
    /// <param name="policy">Validated module, layer, contract and permission declarations.</param>
    /// <param name="projects">The complete available project inventory.</param>
    /// <exception cref="ArgumentException">The inventory is empty, duplicated, unmapped, ambiguous or inconsistent with policy selectors.</exception>
    public RepositoryModel(string inputPath, ArchitecturePolicy policy, IEnumerable<RepositoryProject> projects)
    {
        Id = ProjectPattern.NormalizeIdentity(inputPath);
        ArgumentNullException.ThrowIfNull(policy);
        Policy = policy;
        Projects = RepositoryData.Unique(projects, project => project.Id);
        ResolvedPolicy = new ResolvedPolicy(policy, Projects.Select(project => project.Id));
        Modules = Array.AsReadOnly(policy.Modules.Select(module =>
        {
            var members = Projects.Where(project => ResolvedPolicy.Projects[project.Id].ModuleId == module.Id).ToArray();
            var contracts = module.Contracts.Select(contract => new RepositoryContract(module.Id, contract.Id,
                members.Where(project => ResolvedPolicy.Projects[project.Id].ContractId == contract.Id)));
            return new RepositoryModule(module.Id, members, contracts);
        }).ToArray());
        Layers = Array.AsReadOnly(policy.Layers.Select(layer => new RepositoryLayer(layer.Id,
            Projects.Where(project => ResolvedPolicy.Projects[project.Id].LayerId == layer.Id))).ToArray());
    }

    /// <summary>Gets the policy-relative solution or project input identity.</summary>
    public string Id { get; }
    /// <summary>Gets the immutable policy defining this model's ownership and permissions.</summary>
    public ArchitecturePolicy Policy { get; }
    /// <summary>Gets exact module, layer and optional contract membership and direct permission checks.</summary>
    public ResolvedPolicy ResolvedPolicy { get; }
    /// <summary>Gets immutable projects in ordinal path order.</summary>
    public IReadOnlyList<RepositoryProject> Projects { get; }
    /// <summary>Gets immutable logical modules in ordinal policy identity order.</summary>
    public IReadOnlyList<RepositoryModule> Modules { get; }
    /// <summary>Gets immutable architectural layers in ordinal policy identity order.</summary>
    public IReadOnlyList<RepositoryLayer> Layers { get; }
}
